using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Matchmark.Testing;

/// <summary>
/// Conserved totals (x-matchmark-conserved, docs/reference/invariants.md#conserved): an operation that isn't excepted leaves
/// the total over its collection as it was. The collection is read through its list when it has an unpaged one, else
/// resource by resource: the ones the run has seen created, and the ones the request refers to.
/// </summary>
internal sealed partial class RegressionRunner
{
    private readonly Dictionary<string, HashSet<string>> _conservedIds = new(StringComparer.Ordinal);

    /// <summary>The totals a case must keep, read before it's sent; null when there are none to check.</summary>
    private async Task<List<(Conserved Total, IReadOnlyList<string> Ids, decimal Before)>?> TotalsBeforeAsync(RegressionCase @case, CancellationToken cancellationToken)
    {
        var operation = @case.Operation;
        // Reads too: a service may do deferred work when it's next looked at (the Ledger settles a transfer then).
        if (!options.Workflows || options.Skip.Contains(Checks.TotalConserved)
            || @case.Kind is not (CaseKind.Valid or CaseKind.Example or CaseKind.Boundary))
        {
            return null;
        }

        var totals = Traits.ConservedTotals(suite.Spec).Where(c => !c.Except.Contains(operation.Id)).ToList();
        if (totals.Count == 0)
        {
            return null;
        }

        var read = new List<(Conserved, IReadOnlyList<string>, decimal)>();
        foreach (var total in totals)
        {
            var ids = Ids(@case, total);
            if (await TotalAsync(total, ids, cancellationToken).ConfigureAwait(false) is { } before)
            {
                read.Add((total, ids, before));
            }
        }

        return read;
    }

    /// <summary>
    /// A links workflow, with each conserved total read before its first step and after its last: work deferred to a
    /// later step (a transfer that settles when it's read) is inside it. Not when any step's operation is excepted.
    /// </summary>
    private async Task<WorkflowResult> ConservedAsync(RegressionWorkflow workflow, Func<Task<WorkflowResult>> run, CancellationToken cancellationToken)
    {
        var excepted = Traits.ConservedTotals(suite.Spec).SelectMany(c => c.Except).ToHashSet(StringComparer.Ordinal);
        var before = workflow.Links.Any(l => excepted.Contains(l.OperationId)) ? null : await TotalsBeforeAsync(workflow.Start, cancellationToken).ConfigureAwait(false);
        var result = await run().ConfigureAwait(false);
        if (before is null || result.Skipped is not null || result.Exchanges.Count == 0 || result.Exchanges.Any(e => e.Status is < 200 or >= 300))
        {
            return result;
        }

        var findings = result.Findings.ToList();
        await TotalsAfterAsync(workflow.Start with { Name = workflow.Name }, result.Exchanges[^1], before, findings, cancellationToken, "the workflow").ConfigureAwait(false);
        return result with { Findings = findings };
    }

    /// <summary>After a successful call: each total read again over the same resources, and a finding when one moved.</summary>
    private async Task TotalsAfterAsync(RegressionCase @case, Exchange exchange, List<(Conserved Total, IReadOnlyList<string> Ids, decimal Before)> before,
        List<Finding> findings, CancellationToken cancellationToken, string? by = null)
    {
        foreach (var (total, ids, was) in before)
        {
            if (await TotalAsync(total, ids, cancellationToken).ConfigureAwait(false) is { } now && now != was)
            {
                Add(findings, Checks.TotalConserved, $"{@case.Name}, {total} conserved", exchange,
                    $"{by ?? @case.Operation.Id} changed {total} from {was.ToString(CultureInfo.InvariantCulture)} to {now.ToString(CultureInfo.InvariantCulture)}; the spec declares it conserved"
                    + (total.Except.Count > 0 ? $" by everything but {string.Join(", ", total.Except)}" : string.Empty));
            }
        }
    }

    /// <summary>
    /// Remembers the resources of a conserved collection a successful call created or referred to, so later totals
    /// include them: work deferred to a later call (a transfer settled when it's read) moves money between them.
    /// </summary>
    private void RememberConserved(RegressionCase @case, Exchange exchange)
    {
        if (exchange.Status is < 200 or >= 300 || options.Skip.Contains(Checks.TotalConserved))
        {
            return;
        }

        foreach (var total in Traits.ConservedTotals(suite.Spec))
        {
            var ids = _conservedIds.TryGetValue(total.Collection, out var known) ? known : _conservedIds[total.Collection] = new(StringComparer.Ordinal);
            ids.UnionWith(Ids(@case, total));
            if (@case.Operation.Method == "POST" && @case.Operation.Path == total.Collection && exchange.Json() is JsonObject created
                && ItemParameter(total.Collection) is { } parameter && IdText(created[parameter] ?? created["id"]) is { } id)
            {
                ids.Add(id);
            }
        }
    }

    /// <summary>The resources a total is read over, when it's read one by one: those seen created, and those the request names.</summary>
    private List<string> Ids(RegressionCase @case, Conserved total)
    {
        var ids = new HashSet<string>(_conservedIds.GetValueOrDefault(total.Collection) ?? [], StringComparer.Ordinal);
        if (RequestJson(@case.Request, @case.Operation) is JsonObject sent)
        {
            var schemas = new[] { Traits.ResourceSchema(@case.Operation), @case.Operation.Body?.Schema };
            foreach (var reference in schemas.SelectMany(s => Traits.References(suite.Spec, s)).Where(r => r.Collection == total.Collection))
            {
                if (IdText(sent[reference.Field]) is { } id)
                {
                    ids.Add(id);
                }
            }
        }

        // A write to one of the collection's own resources.
        var item = System.Text.RegularExpressions.Regex.Match(@case.Operation.Path, "^" + System.Text.RegularExpressions.Regex.Escape(total.Collection) + @"/\{(?<p>[^}]+)\}$");
        if (item.Success && @case.Request.Path.Split('?')[0].Split('/').LastOrDefault() is { Length: > 0 } own)
        {
            ids.Add(Uri.UnescapeDataString(own));
        }

        return ids.Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>The total now: through the collection's list when it isn't paged, else over the given resources; null when it can't be read.</summary>
    private async Task<decimal?> TotalAsync(Conserved total, IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        var list = suite.Spec.Operations.FirstOrDefault(o => o.Method == "GET" && o.Path == total.Collection && !o.Parameters.Any(p => p.In == "path" || p.Required));
        if (list is not null && Paging(list) is null)
        {
            var listed = await SendAsync(new RegressionRequest("GET", total.Collection.TrimStart('/'), [], [], null, null), cancellationToken).ConfigureAwait(false);
            return listed.Status == 200 && listed.Json() is { } body ? Traits.Sum(Items(body), total.Total) : null;
        }

        // A paged list is walked whole, page by page.
        if (list is not null && Paging(list) is { } paging)
        {
            var paged = new List<JsonObject>();
            string? at = null;
            for (var page = 0; page < 100; page++)
            {
                var query = at is null ? new List<KeyValuePair<string, string>>() : [new(paging.Cursor.Name, at)];
                var listed = await SendAsync(new RegressionRequest("GET", total.Collection.TrimStart('/'), query, [], null, null), cancellationToken).ConfigureAwait(false);
                if (listed.Status != 200 || listed.Json() is not JsonObject body || body[paging.Items] is not JsonArray items)
                {
                    return null;
                }

                paged.AddRange(items.OfType<JsonObject>());
                at = body[paging.Next] is JsonValue next && next.GetValueKind() == JsonValueKind.String ? next.GetValue<string>() : null;
                if (at is null)
                {
                    return Traits.Sum(paged, total.Total);
                }
            }

            return null;
        }

        var read = suite.Spec.Operations.FirstOrDefault(o => o.Method == "GET" && o.Path.StartsWith(total.Collection + "/{", StringComparison.Ordinal));
        if (read is null || ItemParameter(total.Collection) is not { } parameter || ids.Count == 0)
        {
            return null;
        }

        var resources = new List<JsonObject>();
        foreach (var id in ids)
        {
            var answer = await SendAsync(new RegressionRequest("GET", read.Path.Replace("{" + parameter + "}", Uri.EscapeDataString(id), StringComparison.Ordinal).TrimStart('/'), [], [], null, null),
                cancellationToken).ConfigureAwait(false);
            if (answer.Status == 200 && answer.Json() is JsonObject resource)
            {
                resources.Add(resource);
            }
        }

        return Traits.Sum(resources, total.Total);
    }

    private string? ItemParameter(string collection)
        => suite.Spec.Operations.Select(o => System.Text.RegularExpressions.Regex.Match(o.Path, "^" + System.Text.RegularExpressions.Regex.Escape(collection) + @"/\{(?<p>[^}]+)\}$"))
            .FirstOrDefault(m => m.Success)?.Groups["p"].Value;

    private static string? IdText(JsonNode? id)
        => id is null ? null : id.GetValueKind() == JsonValueKind.String ? id.GetValue<string>() : id.ToJsonString();
}
