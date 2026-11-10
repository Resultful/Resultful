using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Matchmark.Testing;

/// <summary>
/// Behaviour across calls, inferred from standard HTTP in the spec (LAYERS.md): idempotency keys, conditional changes,
/// paging, work that finishes later, rate limits and deprecation. Each is checked where the spec declares it.
/// </summary>
internal sealed partial class RegressionRunner
{
    private static readonly string[] Running = ["running", "pending", "queued", "in_progress", "inprogress", "processing", "notstarted", "started", "accepted"];
    internal static readonly string[] Cursors = ["cursor", "pagetoken", "page_token", "after", "continuationtoken", "continuation_token", "starting_after"];
    private static readonly string[] Nexts = ["next", "nextcursor", "next_cursor", "nextpagetoken", "next_page_token", "continuationtoken"];
    private static readonly string[] ItemsNames = ["items", "data", "results", "values", "value"];

    private int _behaviour;

    /// <summary>The behaviours one case's response can show: idempotency, work accepted for later, deprecation.</summary>
    private async Task BehavioursAsync(RegressionCase @case, Exchange exchange, List<Finding> findings, CancellationToken cancellationToken)
    {
        var operation = @case.Operation;
        var success = exchange.Status is >= 200 and < 300;
        if (success && operation.Idempotent && @case.Kind is CaseKind.Valid or CaseKind.Example or CaseKind.Boundary)
        {
            await IdempotencyAsync(@case, exchange, findings, cancellationToken).ConfigureAwait(false);
        }

        if (exchange.Status == 202)
        {
            await PollAsync(exchange, operation, @case.Name, findings, null, cancellationToken).ConfigureAwait(false);
        }

        // Unique keys need a target that keeps state; a stateless fake is run with workflows off, and so without these.
        if (success && options.Workflows && @case.Kind is CaseKind.Valid or CaseKind.Example or CaseKind.Boundary && !options.Skip.Contains(Checks.UniqueEnforced)
            && operation.Method == "POST" && Traits.Unique(suite.Spec, Traits.ResourceSchema(operation)) is { Count: > 0 } keys
            && _uniqueChecked.Add(operation.Id))
        {
            await UniqueAsync(@case, keys, findings, cancellationToken).ConfigureAwait(false);
        }

        // Derived fields (x-matchmark-derived): a create's response holds each one as its expression gives it.
        if (success && operation.Method == "POST" && !options.Skip.Contains(Checks.DerivedHolds) && exchange.Json() is JsonObject made)
        {
            foreach (var derived in Traits.DerivedFields(suite.Spec, Traits.ResourceSchema(operation)).Where(d => d.OnCreate))
            {
                var self = (JsonObject)made.DeepClone();
                self.Remove(derived.Field);
                if (Traits.Evaluate(derived, RequestJson(@case.Request, operation), self, null) is { } expected
                    && !JsonNode.DeepEquals(expected, made[derived.Field]))
                {
                    Add(findings, Checks.DerivedHolds, @case.Name, exchange,
                        $"{derived.Field} is {made[derived.Field]?.ToJsonString() ?? "missing"}; the spec derives it as {derived.Expression}, which gives {expected.ToJsonString()}");
                }
            }
        }

        // A declared order (x-matchmark-order), or the order a sort parameter's value asks for: every list is in it.
        var sorted = Traits.SortParameter(operation) is { } sort ? Traits.SortOrder(@case.Request.Query.FirstOrDefault(q => q.Key == sort.Name).Value) : [];
        if (success && operation.Method == "GET" && !options.Skip.Contains(Checks.ListOrdered) && (sorted.Count > 0 ? sorted : Traits.Order(operation)) is { Count: > 0 } order
            && exchange.Json() is { } listed && Items(listed) is { Count: > 1 } items)
        {
            for (var i = 1; i < items.Count; i++)
            {
                if (Traits.Compare(items[i - 1], items[i], order) > 0)
                {
                    Add(findings, Checks.ListOrdered, @case.Name, exchange,
                        $"item {i} comes before item {i - 1} in the declared order ({string.Join(", ", order.Select(o => o.Field + (o.Descending ? " desc" : string.Empty)))})");
                    break;
                }
            }
        }

        if (success && options.Workflows && @case.Kind is CaseKind.Valid or CaseKind.Example or CaseKind.Boundary && !options.Skip.Contains(Checks.ReferenceHolds)
            && operation.Method == "POST" && Traits.References(suite.Spec, Traits.ResourceSchema(operation)) is { Count: > 0 } references
            && _referencesChecked.Add(operation.Id))
        {
            await ReferencesAsync(@case, references, findings, cancellationToken).ConfigureAwait(false);
        }

        if (success && options.Workflows && @case.Kind is CaseKind.Valid or CaseKind.Example or CaseKind.Boundary && !options.Skip.Contains(Checks.ScopeHolds)
            && operation.Method == "POST" && Traits.ScopedOf(suite.Spec, Traits.CollectionSchema(suite.Spec, operation.Path)) is { } scoped
            && _scopeChecked.Add(operation.Id))
        {
            await ScopeAsync(@case, exchange, scoped, findings, cancellationToken).ConfigureAwait(false);
        }

        if (success && options.Workflows && @case.Kind is CaseKind.Valid or CaseKind.Example or CaseKind.Boundary && !options.Skip.Contains(Checks.LimitEnforced)
            && operation.Method == "POST" && Traits.LimitOf(suite.Spec, Traits.ResourceSchema(operation)) is { Max: <= MaxLimitExercised } limit
            && _limitChecked.Add(operation.Id))
        {
            await LimitAsync(@case, exchange, limit, findings, cancellationToken).ConfigureAwait(false);
        }
    }

    private readonly HashSet<string> _scopeChecked = new(StringComparer.Ordinal);

    /// <summary>
    /// A scope (x-matchmark-scoped): as the second principal, the resource the first just created isn't in the list, and
    /// reading, changing and deleting it are refused; then, as the first, it's still there. Without a second principal,
    /// a warning that the scope is unchecked.
    /// </summary>
    private async Task ScopeAsync(RegressionCase @case, Exchange exchange, Scoped scoped, List<Finding> findings, CancellationToken cancellationToken)
    {
        var operation = @case.Operation;
        var subject = $"{@case.Name}, scoped to its principal";
        if (options.Principals.Count < 2)
        {
            Add(findings, Checks.ScopeHolds, subject, exchange,
                "the resource is scoped to its principal, but the run has no second principal to try it as, so the scope is unchecked (RegressionOptions.Principals, --principal twice)", Severity.Warning);
            return;
        }

        var item = suite.Spec.Operations.Select(o => (Operation: o, Match: System.Text.RegularExpressions.Regex.Match(o.Path,
                "^" + System.Text.RegularExpressions.Regex.Escape(operation.Path.TrimEnd('/')) + @"/\{(?<p>[^}]+)\}$")))
            .Where(m => m.Match.Success).ToList();
        if (item.Count == 0 || Id(exchange.Json(), item[0].Match.Groups["p"].Value) is not { } id)
        {
            return;
        }

        var other = options.Principals[1];
        var prefix = @case.Request.Path.Split('?')[0].Trim('/');
        var path = $"{prefix}/{Uri.EscapeDataString(id)}";
        List<KeyValuePair<string, string>> As(KeyValuePair<string, string> principal) => [principal];

        // The list, as the other principal: the resource isn't in it.
        if (suite.Spec.Operations.FirstOrDefault(o => o.Method == "GET" && o.Path == operation.Path) is not null)
        {
            var listed = await SendAsync(new RegressionRequest("GET", prefix, [], As(other), null, null), cancellationToken).ConfigureAwait(false);
            var parameter = item[0].Match.Groups["p"].Value;
            if (listed.Status is >= 200 and < 300 && listed.Json() is { } body && Items(body).Any(i => Id(i, parameter) == id))
            {
                Add(findings, Checks.ScopeHolds, $"{subject}, listed", listed, $"another principal's list includes {path}, which belongs to the principal that created it");
            }
        }

        // Read, change and delete, as the other principal: each refused.
        foreach (var (target, _) in item.Where(i => i.Operation.Method is "GET" or "PUT" or "PATCH" or "DELETE").OrderBy(i => i.Operation.Method == "DELETE"))
        {
            var body = target.Method is "PUT" or "PATCH" ? @case.Request.Body : null;
            var answer = await SendAsync(new RegressionRequest(target.Method, path.TrimStart('/'), [], As(other), body, body is null ? null : @case.Request.ContentType),
                cancellationToken).ConfigureAwait(false);
            var expected = Traits.Status(target, Traits.ScopedStatuses, scoped.Declared);
            if (answer.Status is >= 200 and < 300)
            {
                Add(findings, Checks.ScopeHolds, $"{subject}, {target.Method}", answer,
                    $"another principal's {target.Method} of {path} was accepted with {answer.Status}; it belongs to the principal that created it, so it must be refused ({expected})");
            }
            else if (answer.Status != expected && answer.Status is not (400 or 422))
            {
                Add(findings, Checks.ScopeHolds, $"{subject}, {target.Method}", answer, $"another principal's {target.Method} was refused with {answer.Status}; the spec documents {expected} for it", Severity.Warning);
            }
        }

        // As its own principal, it's still there.
        if (item.FirstOrDefault(i => i.Operation.Method == "GET").Operation is not null)
        {
            var mine = await SendAsync(new RegressionRequest("GET", path.TrimStart('/'), [], As(options.Principals[0]), null, null), cancellationToken).ConfigureAwait(false);
            if (mine.Status == 404)
            {
                Add(findings, Checks.ScopeHolds, $"{subject}, still there", mine, $"{path} was gone for the principal that created it after another principal tried to delete it");
            }
        }
    }

    /// <summary>A limit above this isn't exercised: the creates would make the run slow (the audit says so).</summary>
    internal const int MaxLimitExercised = 50;

    private readonly HashSet<string> _limitChecked = new(StringComparer.Ordinal);

    /// <summary>
    /// A limit (x-matchmark-limit): creates until one is refused, which must happen by the limit plus one, with the
    /// documented status. What it created (not the case's own resource) is deleted after, so later cases still have room; without a delete on the
    /// item path, it isn't exercised.
    /// </summary>
    private async Task LimitAsync(RegressionCase @case, Exchange exchange, Limit limit, List<Finding> findings, CancellationToken cancellationToken)
    {
        var operation = @case.Operation;
        var delete = suite.Spec.Operations.FirstOrDefault(o => o.Method == "DELETE"
            && System.Text.RegularExpressions.Regex.IsMatch(o.Path, "^" + System.Text.RegularExpressions.Regex.Escape(operation.Path.TrimEnd('/')) + @"/\{[^}]+\}$"));
        if (delete is null || operation.Body is not { } body || RequestJson(@case.Request, operation) is not JsonObject sent)
        {
            return;
        }

        var parameter = System.Text.RegularExpressions.Regex.Match(delete.Path, @"\{(?<p>[^}]+)\}$").Groups["p"].Value;
        var prefix = @case.Request.Path.Split('?')[0].TrimEnd('/');
        var made = new List<string>();

        var expected = Traits.Status(operation, Traits.LimitStatuses, limit.Declared);
        var subject = $"{@case.Name}, limit {limit.Max} reached";
        try
        {
            for (var i = 0; i <= limit.Max; i++)
            {
                var next = suite.Samples.Unique(sent, body.Schema, 80_000 + _behaviour++) ?? sent;
                var answer = await SendAsync(@case.Request with { Body = RequestText(@case.Request, next), Headers = FreshKey(@case.Request.Headers) }, cancellationToken)
                    .ConfigureAwait(false);
                if (answer.Status is >= 200 and < 300)
                {
                    if (Id(answer.Json(), parameter) is { } id)
                    {
                        made.Add(id);
                    }

                    continue;
                }

                if (answer.Status != expected)
                {
                    Add(findings, Checks.LimitEnforced, subject, answer, $"a create past the limit ({limit}) was refused with {answer.Status}; the spec documents {expected} for it", Severity.Warning);
                }

                return;
            }

            Add(findings, Checks.LimitEnforced, subject, exchange,
                $"{limit.Max + 2} creates were accepted; the spec limits it to {limit}, so a create past it must be refused ({expected})");
        }
        finally
        {
            foreach (var id in made)
            {
                var path = delete.Path.Replace("{" + parameter + "}", Uri.EscapeDataString(id), StringComparison.Ordinal);
                path = prefix + path[operation.Path.TrimEnd('/').Length..];
                await SendAsync(new RegressionRequest("DELETE", path.TrimStart('/'), [], @case.Request.Headers.Where(h => string.Equals(h.Key, "Authorization", StringComparison.OrdinalIgnoreCase)).ToList(), null, null),
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>A created resource's id: the item path's parameter, else <c>id</c>.</summary>
    private static string? Id(JsonNode? created, string parameter)
        => (created?[parameter] ?? created?["id"]) is { } id ? (id.GetValueKind() == JsonValueKind.String ? id.GetValue<string>() : id.ToJsonString()) : null;

    /// <summary>A list's items: the body when it's an array, else its first array property (items, data…).</summary>
    private static List<JsonObject> Items(JsonNode body)
        => (body as JsonArray ?? (body as JsonObject)?.Select(p => p.Value).OfType<JsonArray>().FirstOrDefault() ?? []).OfType<JsonObject>().ToList();

    private readonly HashSet<string> _uniqueChecked = new(StringComparer.Ordinal);
    private readonly HashSet<string> _referencesChecked = new(StringComparer.Ordinal);

    /// <summary>
    /// References (x-matchmark-references): a create naming a resource that doesn't exist is refused, and a delete of a
    /// resource still referred to is refused when the reference restricts it.
    /// </summary>
    private async Task ReferencesAsync(RegressionCase @case, IReadOnlyList<Reference> references, List<Finding> findings, CancellationToken cancellationToken)
    {
        var operation = @case.Operation;
        if (RequestJson(@case.Request, operation) is not JsonObject sent)
        {
            return;
        }

        foreach (var reference in references.Where(r => !r.Field.Contains('.', StringComparison.Ordinal)))
        {
            var read = suite.Spec.Operations.FirstOrDefault(o => o.Method == "GET" && o.Path == reference.Target);
            var missing = await MissingAsync(sent[reference.Field], reference, read, cancellationToken).ConfigureAwait(false);
            if (missing is not null)
            {
                var dangling = (JsonObject)sent.DeepClone();
                dangling[reference.Field] = missing;
                var expected = Traits.Status(operation, Traits.ReferenceStatuses, reference.Declared);
                var answer = await SendAsync(@case.Request with { Body = RequestText(@case.Request, dangling), Headers = FreshKey(@case.Request.Headers) }, cancellationToken)
                    .ConfigureAwait(false);
                var subject = $"{@case.Name}, references {reference.Field} to nothing";
                if (answer.Status is >= 200 and < 300)
                {
                    Add(findings, Checks.ReferenceHolds, subject, answer,
                        $"a create whose {reference.Field} refers to {missing.ToJsonString()}, which doesn't exist, was accepted with {answer.Status}; it must be refused ({expected})");
                }
                else if (answer.Status != expected)
                {
                    Add(findings, Checks.ReferenceHolds, subject, answer, $"a reference to nothing was refused with {answer.Status}; the spec documents {expected} for it", Severity.Warning);
                }
            }

            var delete = suite.Spec.Operations.FirstOrDefault(o => o.Method == "DELETE" && o.Path == reference.Target);
            if (reference.OnDelete is "cascade" or "clear" && delete is not null)
            {
                await AfterDeleteAsync(@case, sent, reference, delete, findings, cancellationToken).ConfigureAwait(false);
            }

            if (reference.OnDelete == "restrict" && delete is not null
                && await ReferredAsync(@case, sent, reference, cancellationToken).ConfigureAwait(false) is { } target)
            {
                var parameter = System.Text.RegularExpressions.Regex.Match(reference.Target, @"\{(?<p>[^}]+)\}$").Groups["p"].Value;
                var path = reference.Target.Replace("{" + parameter + "}", Uri.EscapeDataString(target), StringComparison.Ordinal).TrimStart('/');
                var answer = await SendAsync(new RegressionRequest("DELETE", path, [], [], null, null), cancellationToken).ConfigureAwait(false);
                var expected = Traits.Status(delete, Traits.RestrictStatuses);
                if (answer.Status is >= 200 and < 300)
                {
                    Add(findings, Checks.ReferenceHolds, $"{@case.Name}, references {reference.Field} still referring", answer,
                        $"deleting {path}, which a resource's {reference.Field} still refers to, was accepted with {answer.Status}; the reference restricts it ({expected})");
                }
            }
        }
    }

    /// <summary>
    /// A fresh resource of the reference's target, and a fresh resource referring to it: the target's id, or null when
    /// either can't be created. The restrict check then needs nothing earlier cases left behind.
    /// </summary>
    private async Task<string?> ReferredAsync(RegressionCase @case, JsonObject sent, Reference reference, CancellationToken cancellationToken)
        => (await ReferringAsync(@case, sent, reference, cancellationToken).ConfigureAwait(false))?.Target;

    /// <summary>A fresh target, and a fresh resource referring to it: the target's id and the referrer as created; null when either couldn't be made.</summary>
    private async Task<(string Target, JsonObject Referrer)?> ReferringAsync(RegressionCase @case, JsonObject sent, Reference reference, CancellationToken cancellationToken)
    {
        var create = suite.Spec.Operations.FirstOrDefault(o => o.Method == "POST" && o.Path == reference.Collection && o.Body is not null);
        if (create?.Body is not { } body || !suite.Samples.TrySample(body.Schema, false, out var sample) || sample is null)
        {
            return null;
        }

        var fresh = suite.Samples.Unique(sample, body.Schema, 70_000 + _behaviour++) ?? sample;
        var made = await SendAsync(new RegressionRequest("POST", create.Path.TrimStart('/'), [], [], fresh.ToJsonString(), body.MediaType), cancellationToken).ConfigureAwait(false);
        var parameter = System.Text.RegularExpressions.Regex.Match(reference.Target, @"\{(?<p>[^}]+)\}$").Groups["p"].Value;
        if (made.Status is < 200 or >= 300 || made.Json() is not JsonObject target || (target[parameter] ?? target["id"]) is not { } id)
        {
            return null;
        }

        var referring = (JsonObject)sent.DeepClone();
        referring[reference.Field] = id.DeepClone();
        if (suite.Samples.Unique(referring, @case.Operation.Body!.Schema, 70_000 + _behaviour++) is JsonObject unique)
        {
            unique[reference.Field] = id.DeepClone();
            referring = unique;
        }

        var referred = await SendAsync(@case.Request with { Body = RequestText(@case.Request, referring), Headers = FreshKey(@case.Request.Headers) }, cancellationToken)
            .ConfigureAwait(false);
        return referred.Status is >= 200 and < 300 && referred.Json() is JsonObject referrer
            ? (id.GetValueKind() == JsonValueKind.String ? id.GetValue<string>() : id.ToJsonString(), referrer)
            : null;
    }

    /// <summary>
    /// A delete of what's referred to, for cascade and clear: a fresh target and referrer, the target deleted, then the
    /// referrer read back, which must be gone (cascade) or have the field cleared (clear).
    /// </summary>
    private async Task AfterDeleteAsync(RegressionCase @case, JsonObject sent, Reference reference, SpecOperation delete, List<Finding> findings, CancellationToken cancellationToken)
    {
        var operation = @case.Operation;
        var read = suite.Spec.Operations.Select(o => (Operation: o, Match: System.Text.RegularExpressions.Regex.Match(o.Path,
                "^" + System.Text.RegularExpressions.Regex.Escape(operation.Path.TrimEnd('/')) + @"/\{(?<p>[^}]+)\}$")))
            .FirstOrDefault(m => m.Operation.Method == "GET" && m.Match.Success);
        if (read.Operation is null || await ReferringAsync(@case, sent, reference, cancellationToken).ConfigureAwait(false) is not { } made
            || Id(made.Referrer, read.Match.Groups["p"].Value) is not { } referrer)
        {
            return;
        }

        var parameter = System.Text.RegularExpressions.Regex.Match(reference.Target, @"\{(?<p>[^}]+)\}$").Groups["p"].Value;
        var target = reference.Target.Replace("{" + parameter + "}", Uri.EscapeDataString(made.Target), StringComparison.Ordinal).TrimStart('/');
        var deleted = await SendAsync(new RegressionRequest("DELETE", target, [], [], null, null), cancellationToken).ConfigureAwait(false);
        if (deleted.Status is < 200 or >= 300)
        {
            return;
        }

        var path = $"{@case.Request.Path.Split('?')[0].Trim('/')}/{Uri.EscapeDataString(referrer)}";
        var after = await SendAsync(new RegressionRequest("GET", path, [], [], null, null), cancellationToken).ConfigureAwait(false);
        var subject = $"{@case.Name}, references {reference.Field} {reference.OnDelete}";
        if (reference.OnDelete == "cascade" && after.Status != 404)
        {
            Add(findings, Checks.ReferenceHolds, subject, after, $"after deleting {target}, {path}, which referred to it, is still there ({after.Status}); the reference cascades, so it must be gone (404)");
        }
        else if (reference.OnDelete == "clear" && after.Status is >= 200 and < 300 && after.Json() is JsonObject resource && Traits.Text(resource, reference.Field) is { } still)
        {
            Add(findings, Checks.ReferenceHolds, subject, after, $"after deleting {target}, {path} still has {reference.Field} {still}; the reference clears it, so it must be null or absent");
        }
    }

    /// <summary>An id of the reference's type that names no resource: checked with a read when the spec has one.</summary>
    private async Task<JsonNode?> MissingAsync(JsonNode? sample, Reference reference, SpecOperation? read, CancellationToken cancellationToken)
    {
        JsonNode candidate = sample is JsonValue value && value.GetValueKind() == JsonValueKind.Number ? JsonValue.Create(987654321L)
            : sample is JsonValue text && text.GetValueKind() == JsonValueKind.String && Guid.TryParse(text.GetValue<string>(), out _) ? JsonValue.Create(Guid.NewGuid().ToString())
            : JsonValue.Create("missing-" + Guid.NewGuid().ToString("N")[..8]);
        if (read is null)
        {
            return candidate;
        }

        var parameter = System.Text.RegularExpressions.Regex.Match(reference.Target, @"\{(?<p>[^}]+)\}$").Groups["p"].Value;
        var id = candidate.GetValueKind() == JsonValueKind.String ? candidate.GetValue<string>() : candidate.ToJsonString();
        var answer = await SendAsync(new RegressionRequest("GET", reference.Target.Replace("{" + parameter + "}", Uri.EscapeDataString(id), StringComparison.Ordinal).TrimStart('/'),
            [], [], null, null), cancellationToken).ConfigureAwait(false);
        return answer.Status == 404 ? candidate : null;
    }

    /// <summary>
    /// Unique keys (x-matchmark-unique): after a create succeeds, the same key again is refused, in another case too when
    /// the key ignores case, and so is renaming a second resource into it.
    /// </summary>
    private async Task UniqueAsync(RegressionCase @case, IReadOnlyList<UniqueKey> keys, List<Finding> findings, CancellationToken cancellationToken)
    {
        var operation = @case.Operation;
        if (operation.Body is not { } body || RequestJson(@case.Request, operation) is not JsonObject sent)
        {
            return;
        }

        foreach (var key in keys)
        {
            var expected = Traits.Status(operation, Traits.UniqueStatuses, key.Declared);
            if (Traits.Value(key, sent) is null)
            {
                continue;
            }

            await RefusedAsync(@case.Request with { Headers = FreshKey(@case.Request.Headers) }, $"{@case.Name}, unique {key} taken",
                "a second create with the same", key, expected, findings, cancellationToken).ConfigureAwait(false);

            if (key.IgnoreCase && Recased(sent, key) is { } variant)
            {
                await RefusedAsync(@case.Request with { Body = RequestText(@case.Request, variant), Headers = FreshKey(@case.Request.Headers) },
                    $"{@case.Name}, unique {key} taken, other case", "a create with the same, in another case,", key, expected, findings, cancellationToken)
                    .ConfigureAwait(false);
            }

            await RenameIntoAsync(@case, sent, body, key, findings, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>A second resource, then a PUT or PATCH that gives it the first's key: refused.</summary>
    private async Task RenameIntoAsync(RegressionCase @case, JsonObject first, SpecBody body, UniqueKey key, List<Finding> findings, CancellationToken cancellationToken)
    {
        var operation = @case.Operation;
        if (operation.Method != "POST")
        {
            return;
        }

        var change = suite.Spec.Operations.FirstOrDefault(o => o.Method is "PUT" or "PATCH" && o.Body is not null
            && System.Text.RegularExpressions.Regex.IsMatch(o.Path, "^" + System.Text.RegularExpressions.Regex.Escape(operation.Path.TrimEnd('/')) + @"/\{[^}]+\}$"));
        if (change is null || suite.Samples.Unique(first, body.Schema, 60_000 + _behaviour++) is not JsonObject second
            || Traits.Value(key, second) == Traits.Value(key, first))
        {
            return;
        }

        var created = await SendAsync(@case.Request with { Body = RequestText(@case.Request, second), Headers = FreshKey(@case.Request.Headers) }, cancellationToken)
            .ConfigureAwait(false);
        var parameter = System.Text.RegularExpressions.Regex.Match(change.Path, @"\{(?<p>[^}]+)\}$").Groups["p"].Value;
        if (created.Status is < 200 or >= 300 || created.Json() is not JsonObject resource
            || (resource[parameter] ?? resource["id"]) is not { } id)
        {
            return;
        }

        JsonObject renamed;
        if (change.Method == "PATCH")
        {
            renamed = [];
            foreach (var field in key.Fields.Where(f => !f.Contains('.', StringComparison.Ordinal)))
            {
                renamed[field] = first[field]?.DeepClone();
            }
        }
        else
        {
            renamed = (JsonObject)second.DeepClone();
            foreach (var field in key.Fields.Where(f => !f.Contains('.', StringComparison.Ordinal)))
            {
                renamed[field] = first[field]?.DeepClone();
            }
        }

        var idText = id.GetValueKind() == JsonValueKind.String ? id.GetValue<string>() : id.ToJsonString();
        var request = new RegressionRequest(change.Method, change.Path.Replace("{" + parameter + "}", Uri.EscapeDataString(idText), StringComparison.Ordinal).TrimStart('/'),
            [], FreshKey(@case.Request.Headers.Where(h => !string.Equals(h.Key, "If-Match", StringComparison.OrdinalIgnoreCase)).ToList()),
            renamed.ToJsonString(), change.Body!.MediaType);
        await RefusedAsync(request, $"{@case.Name}, unique {key} renamed into", $"a {change.Method} of another resource to the same", key,
            Traits.Status(change, Traits.UniqueStatuses, key.Declared), findings, cancellationToken).ConfigureAwait(false);
    }

    private async Task RefusedAsync(RegressionRequest request, string subject, string what, UniqueKey key, int expected, List<Finding> findings,
        CancellationToken cancellationToken)
    {
        var answer = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (answer.Status is >= 200 and < 300)
        {
            Add(findings, Checks.UniqueEnforced, subject, answer, $"{what} {key} was accepted with {answer.Status}; the spec declares it unique, so it must be refused ({expected})");
        }
        else if (answer.Status != expected)
        {
            Add(findings, Checks.UniqueEnforced, subject, answer, $"{what} {key} was refused with {answer.Status}; the spec documents {expected} for it", Severity.Warning);
        }

    }

    /// <summary>The request's headers with a fresh Idempotency-Key, if it sends one, so a repeat isn't a replay.</summary>
    private static List<KeyValuePair<string, string>> FreshKey(IReadOnlyList<KeyValuePair<string, string>> headers)
        => headers.Select(h => string.Equals(h.Key, "Idempotency-Key", StringComparison.OrdinalIgnoreCase) ? new KeyValuePair<string, string>(h.Key, Guid.NewGuid().ToString()) : h)
            .ToList();

    /// <summary>The body with the key's first string field in another case, or null when changing case changes nothing.</summary>
    private static JsonObject? Recased(JsonObject sent, UniqueKey key)
    {
        foreach (var field in key.Fields.Where(f => !f.Contains('.', StringComparison.Ordinal)))
        {
            if (sent[field] is JsonValue value && value.GetValueKind() == JsonValueKind.String && value.GetValue<string>() is { } text)
            {
                var upper = text.ToUpperInvariant();
                var other = string.Equals(upper, text, StringComparison.Ordinal) ? text.ToLowerInvariant() : upper;
                if (!string.Equals(other, text, StringComparison.Ordinal))
                {
                    var copy = (JsonObject)sent.DeepClone();
                    copy[field] = other;
                    return copy;
                }
            }
        }

        return null;
    }

    /// <summary>The same key and body again gives the same answer; the same key with another body is refused.</summary>
    private async Task IdempotencyAsync(RegressionCase @case, Exchange first, List<Finding> findings, CancellationToken cancellationToken)
    {
        if (!options.Skip.Contains(Checks.IdempotentReplay))
        {
            var replay = await SendAsync(@case.Request, cancellationToken).ConfigureAwait(false);
            if (replay.Status != first.Status || !JsonNode.DeepEquals(Volatile(first.Json()), Volatile(replay.Json())))
            {
                Add(findings, Checks.IdempotentReplay, @case.Name, replay,
                    $"the same Idempotency-Key and body again gave {replay.Status} {RegressionReport.Excerpt(replay.Body)}, not the first answer, "
                    + $"{first.Status} {RegressionReport.Excerpt(first.Body)}: a retry after a lost response would do the work twice");
            }
        }

        if (!options.Skip.Contains(Checks.IdempotencyKeyReuse) && @case.Operation.Body is { } body
            && RequestJson(@case.Request, @case.Operation) is { } json && suite.Samples.Unique(json, body.Schema, 50_000 + _behaviour++) is { } other
            && !JsonNode.DeepEquals(json, other))
        {
            var reuse = await SendAsync(@case.Request with { Body = RequestText(@case.Request, other) }, cancellationToken).ConfigureAwait(false);
            Response(@case.Operation, reuse, requestValid: false, $"{@case.Name}, key reused with another body", findings);
            if (reuse.Status is >= 200 and < 300)
            {
                Add(findings, Checks.IdempotencyKeyReuse, @case.Name, reuse,
                    $"an Idempotency-Key reused with a different body was accepted with {reuse.Status}; it must be refused (the draft says 422)");
            }
            else if (reuse.Status != 422)
            {
                Add(findings, Checks.IdempotencyKeyReuse, @case.Name, reuse, $"a reused Idempotency-Key with a different body was refused with {reuse.Status}; the draft says 422", Severity.Warning);
            }
        }
    }

    /// <summary>After a conditional change: a new ETag, and the old one now refused with 412.</summary>
    private async Task ConditionalAsync(SpecOperation operation, Exchange change, string subject, List<Finding> findings, List<Exchange> exchanges,
        CancellationToken cancellationToken)
    {
        if (change.Status is < 200 or >= 300)
        {
            return;
        }

        var sent = change.Request.Headers.FirstOrDefault(h => string.Equals(h.Key, "If-Match", StringComparison.OrdinalIgnoreCase)).Value;
        change.Headers.TryGetValue("ETag", out var etag);
        if (!options.Skip.Contains(Checks.ETagChanges) && (etag is null || etag == sent))
        {
            Add(findings, Checks.ETagChanges, subject, change, etag is null
                ? "the change succeeded but gave no ETag, so the next change can't be made safely"
                : $"the change succeeded but the ETag is still {etag}, so a stale copy still looks current");
        }

        if (sent is not null && !options.Skip.Contains(Checks.StaleIfMatchRefused))
        {
            var stale = await SendAsync(change.Request, cancellationToken).ConfigureAwait(false);
            exchanges.Add(stale);
            Response(operation, stale, requestValid: false, $"{subject}, with the old ETag", findings);
            if (stale.Status != 412)
            {
                Add(findings, Checks.StaleIfMatchRefused, subject, stale,
                    $"the same change with the now-stale If-Match {sent} gave {stale.Status}, not 412: a concurrent change would be lost");
            }
        }
    }

    /// <summary>Polls work accepted with 202 (its Location, or the first link to a GET) until it reaches a final state.</summary>
    private async Task PollAsync(Exchange accepted, SpecOperation operation, string subject, List<Finding> findings, List<Exchange>? exchanges,
        CancellationToken cancellationToken)
    {
        if (options.Skip.Contains(Checks.OperationCompletes))
        {
            return;
        }

        RegressionRequest? poll = null;
        if (accepted.Headers.TryGetValue("Location", out var location) && Uri.TryCreate(client.BaseAddress ?? new Uri("http://localhost/"), location, out var target))
        {
            var path = "/" + (client.BaseAddress ?? new Uri("http://localhost/")).MakeRelativeUri(target).OriginalString.Split('?')[0];
            poll = Template(path, "GET") is null ? null : new RegressionRequest("GET", path, [], [], null, null);
        }

        if (poll is null && operation.Response(202)?.Links.Select(l => (Link: l, Target: suite.Spec.Operations.FirstOrDefault(o => o.Id == l.OperationId)))
                .FirstOrDefault(l => l.Target?.Method == "GET") is { Target: { } read } link)
        {
            poll = Request(link.Link, read, accepted, 60_000 + _behaviour++);
        }

        if (poll is null)
        {
            return; // nothing to poll: the spec says no more about it
        }

        for (var attempt = 1; attempt <= options.MaxPolls; attempt++)
        {
            var status = await SendAsync(poll, cancellationToken).ConfigureAwait(false);
            exchanges?.Add(status);
            if (status.Status is < 200 or >= 300)
            {
                Add(findings, Checks.OperationCompletes, subject, status, $"polling the accepted work gave {status.Status}: {RegressionReport.Excerpt(status.Body)}");
                return;
            }

            var state = (status.Json() as JsonObject)?["status"] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;
            if (state is null || !Running.Contains(state.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant()))
            {
                return;
            }

            var wait = RetryAfter(status) ?? TimeSpan.Zero;
            await Task.Delay(wait < options.MaxRetryWait ? wait : options.MaxRetryWait, cancellationToken).ConfigureAwait(false);
        }

        Add(findings, Checks.OperationCompletes, subject, accepted, $"the accepted work was still running after {options.MaxPolls} polls");
    }

    internal static void Deprecation(Exchange exchange, string subject, List<Finding> findings)
    {
        if (!exchange.Headers.TryGetValue("Deprecation", out var deprecation))
        {
            findings.Add(new Finding(Checks.DeprecationAnnounced, Severity.Failure, subject,
                "the operation is deprecated, but the response has no Deprecation header (RFC 9745), so callers aren't told", exchange.Request.Curl(null)));
            return;
        }

        // RFC 9745: a structured-field date, @<seconds since the epoch>. RFC 8594: Sunset is an HTTP-date, never earlier.
        if (!deprecation.StartsWith('@') || !long.TryParse(deprecation[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            findings.Add(new Finding(Checks.DeprecationAnnounced, Severity.Failure, subject,
                $"Deprecation is '{deprecation}'; RFC 9745 says a date such as @1688169599", exchange.Request.Curl(null)));
        }
        else if (exchange.Headers.TryGetValue("Sunset", out var sunset) && DateTimeOffset.TryParse(sunset, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var end)
                 && end < DateTimeOffset.FromUnixTimeSeconds(seconds))
        {
            findings.Add(new Finding(Checks.DeprecationAnnounced, Severity.Failure, subject,
                $"Sunset ({sunset}) is before Deprecation ({DateTimeOffset.FromUnixTimeSeconds(seconds):R}); RFC 9745 says it can't be", exchange.Request.Curl(null)));
        }
    }

    private static TimeSpan? RetryAfter(Exchange exchange)
    {
        if (!exchange.Headers.TryGetValue("Retry-After", out var value))
        {
            return null;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return TimeSpan.FromSeconds(Math.Max(0, seconds));
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at - DateTimeOffset.UtcNow : null;
    }

    // ---------------------------------------------------------------- paging

    /// <summary>A paged list: a GET with a cursor parameter whose response has an array and a cursor to the next page.</summary>
    private (SpecParameter Cursor, string Items, string Next, SpecParameter? Limit)? Paging(SpecOperation operation)
    {
        if (operation.Method != "GET" || operation.Parameters.FirstOrDefault(p => p.In == "query" && Cursors.Contains(p.Name.ToLowerInvariant())) is not { } cursor
            || operation.Response(200)?.Content.Values.FirstOrDefault(s => s is not null) is not { } schema)
        {
            return null;
        }

        var properties = suite.Spec.Resolve(schema)["properties"]?.AsObject();
        var next = properties?.Select(p => p.Key).FirstOrDefault(k => Nexts.Contains(k.ToLowerInvariant()));
        var items = properties?.Where(p => SchemaValidator.Types(suite.Spec.Resolve(p.Value!)).Contains("array")).Select(p => p.Key)
            .OrderBy(k => ItemsNames.Contains(k.ToLowerInvariant()) ? 0 : 1).FirstOrDefault();
        var limit = operation.Parameters.FirstOrDefault(p => p.In == "query" && p.Name.ToLowerInvariant() is "limit" or "pagesize" or "page_size" or "per_page" or "top");
        return next is null || items is null ? null : (cursor, items, next, limit);
    }

    /// <summary>Walks every page with a small limit, then checks it against one big page.</summary>
    private async Task<WorkflowResult> PagesAsync(SpecOperation operation, CancellationToken cancellationToken)
    {
        var (cursor, items, next, limit) = Paging(operation)!.Value;
        var name = $"paging {operation.Id}";
        var findings = new List<Finding>();
        var exchanges = new List<Exchange>();
        var limitSchema = limit is null ? null : suite.Spec.Resolve(limit.Schema);
        var small = Math.Max(SchemaValidator.Number(limitSchema ?? [], "minimum") ?? 1, 2);
        var big = SchemaValidator.Number(limitSchema ?? [], "maximum") ?? 1000;

        RegressionRequest Page(decimal size, string? at)
        {
            var query = new List<KeyValuePair<string, string>>();
            if (limit is not null)
            {
                query.Add(new(limit.Name, size.ToString(CultureInfo.InvariantCulture)));
            }

            if (at is not null)
            {
                query.Add(new(cursor.Name, at));
            }

            return new RegressionRequest("GET", operation.Path, query, [], null, null);
        }

        var seen = new List<string>();
        var walked = new List<JsonObject>();
        string? at = null;
        for (var page = 0; ; page++)
        {
            if (page == 100)
            {
                Add(findings, Checks.PagesComplete, name, exchanges[^1], "100 pages and still a next cursor: the pages don't end");
                return new WorkflowResult(name, exchanges, findings, null);
            }

            var exchange = await SendAsync(Page(small, at), cancellationToken).ConfigureAwait(false);
            exchanges.Add(exchange);
            Response(operation, exchange, requestValid: true, name, findings);
            if (exchange.Json() is not JsonObject body || body[items] is not JsonArray list)
            {
                return new WorkflowResult(name, exchanges, findings, $"page {page + 1} gave {exchange.Status}, with no {items} to walk");
            }

            walked.AddRange(list.OfType<JsonObject>());
            foreach (var item in list)
            {
                var key = Key(item);
                if (seen.Contains(key))
                {
                    Add(findings, Checks.PagesComplete, name, exchange, $"{Excerpt(key)} is on two pages: walking the cursors repeats items");
                    return new WorkflowResult(name, exchanges, findings, null);
                }

                seen.Add(key);
            }

            at = body[next] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;
            if (at is null)
            {
                break;
            }
        }

        // A declared order holds across pages too: each page continues the one before.
        if (Traits.Order(operation) is { Count: > 0 } order && !options.Skip.Contains(Checks.ListOrdered))
        {
            for (var i = 1; i < walked.Count; i++)
            {
                if (Traits.Compare(walked[i - 1], walked[i], order) > 0)
                {
                    Add(findings, Checks.ListOrdered, $"{name}, across pages", exchanges[^1], $"item {i} of the pages walked comes before item {i - 1} in the declared order: a page doesn't continue the one before");
                    break;
                }
            }
        }

        var all = await SendAsync(Page(big, null), cancellationToken).ConfigureAwait(false);
        exchanges.Add(all);
        if (all.Json() is JsonObject whole && whole[items] is JsonArray everything && Cursor(whole[next]) is null)
        {
            var expected = everything.Select(Key).ToList();
            var missing = expected.Except(seen).ToList();
            if (missing.Count > 0 || seen.Except(expected).Any())
            {
                Add(findings, Checks.PagesComplete, name, all,
                    $"walking the pages found {seen.Count} items; one page of up to {big} has {expected.Count}"
                    + (missing.Count > 0 ? $", including {Excerpt(missing[0])}, which no page had" : string.Empty));
            }
        }

        return new WorkflowResult(name, exchanges, Noticed(findings), null);
    }

    private static string? Cursor(JsonNode? next) => next is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    /// <summary>An item's identity: its id when it has one, else the whole item.</summary>
    private static string Key(JsonNode? item) => (item as JsonObject)?["id"]?.ToJsonString() ?? item?.ToJsonString() ?? "null";

    private static string Excerpt(string key) => key.Length <= 80 ? key : key[..80] + "…";
}
