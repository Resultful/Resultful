using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Matchmark.Testing;

/// <summary>
/// A stateful fake of an API, from its spec alone: a twin (GAPS.md, gap 1). Resources are inferred from the paths, so
/// <c>/pets</c> and <c>/pets/{petId}</c> are one collection keyed by <c>petId</c>: POST creates (assigning the id and
/// any required read-only field), GET reads and lists, PUT replaces or creates, PATCH merges, DELETE removes, and what
/// isn't there is 404. Requests are checked against the spec and a bad one is its 400. ETags and If-Match, and
/// Idempotency-Key, behave as the spec's headers say. Any other operation answers with its documented success and a
/// sample. Behaviour beyond that (a name that must be unique, say) is what a fidelity run against the real API finds
/// missing (<see cref="RegressionReport.Fidelity"/>).
/// </summary>
public sealed class SpecTwin : HttpMessageHandler
{
    private readonly OpenApiSpec _spec;
    private readonly SchemaValidator _validator;
    private readonly Samples _samples;
    private readonly Dictionary<string, Collection> _collections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Body, Reply Answer)> _keys = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly Dictionary<string, List<Func<TwinCall, TwinAnswer?>>> _behaviours = new(StringComparer.Ordinal);
    private readonly List<Func<TwinRejection, TwinAnswer?>> _rejections = [];
    private long _ids;

    public SpecTwin(string specJson)
    {
        _spec = OpenApiSpec.Parse(specJson);
        _validator = new SchemaValidator(_spec);
        _samples = new Samples(_spec, _validator);
    }

    /// <summary>The resources it holds, by collection path, for a test to inspect or seed.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<JsonObject>> Resources
    {
        get
        {
            lock (_gate)
            {
                return _collections.ToDictionary(c => c.Key, c => (IReadOnlyList<JsonObject>)c.Value.Items.Values.Select(i => (JsonObject)i.Resource.DeepClone()).ToList());
            }
        }
    }

    /// <summary>
    /// How the API rejects, where it differs from the twin's problem details (GAPS.md, gap 3): run on every 4xx the twin
    /// answers, its own checks' included, which <see cref="On"/> never sees. An answer replaces it; a null body is an
    /// empty one.
    /// </summary>
    public SpecTwin OnRejected(Func<TwinRejection, TwinAnswer?> behaviour)
    {
        lock (_gate)
        {
            _rejections.Add(behaviour);
            return this;
        }
    }

    /// <summary>
    /// Behaviour the spec doesn't state, for one operation (GAPS.md, gap 3): run after the request is checked, before
    /// what the twin infers. An answer replaces the inferred one; null lets it through. Owned code, measured by the
    /// fidelity score: a behaviour is right when the steps it touches match the real API.
    /// </summary>
    public SpecTwin On(string operationId, Func<TwinCall, TwinAnswer?> behaviour)
    {
        lock (_gate)
        {
            if (!_behaviours.TryGetValue(operationId, out var list))
            {
                _behaviours[operationId] = list = [];
            }

            list.Add(behaviour);
            return this;
        }
    }

    /// <summary>
    /// Seeds a collection (its path, as <c>/pets</c>) with resources, each keyed by its id property: the same starting
    /// data as the real API's, for a fidelity run.
    /// </summary>
    public void Seed(string collection, IEnumerable<JsonObject> resources)
    {
        lock (_gate)
        {
            var store = _collections.TryGetValue(collection, out var existing) ? existing : _collections[collection] = new Collection();
            var idName = _spec.Operations.Select(o => Regex.Match(o.Path, "^" + Regex.Escape(collection) + @"/\{(?<id>[^}]+)\}$")).FirstOrDefault(m => m.Success)?.Groups["id"].Value;
            foreach (var resource in resources)
            {
                var key = (resource[idName ?? "id"] ?? resource["id"]) is JsonValue id ? (id.GetValueKind() == JsonValueKind.String ? id.GetValue<string>() : id.ToJsonString()) : (++_ids).ToString(CultureInfo.InvariantCulture);
                store.Items[key] = ((JsonObject)resource.DeepClone(), 1);
                if (long.TryParse(key, out var number) && number > _ids)
                {
                    _ids = number;
                }
            }
        }
    }

    private sealed record Reply(int Status, string? ContentType, string? Body, Dictionary<string, string> Headers);

    private sealed class Collection
    {
        public Dictionary<string, (JsonObject Resource, int Version)> Items { get; } = new(StringComparer.Ordinal);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var query = Query(request.RequestUri!.Query);
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase);
        Reply answer;
        lock (_gate)
        {
            answer = Answer(request.Method.Method, request.RequestUri.AbsolutePath, query, headers, body, request.Content?.Headers.ContentType?.MediaType);
        }

        var response = new HttpResponseMessage((HttpStatusCode)answer.Status);
        if (answer.Body is not null)
        {
            response.Content = new StringContent(answer.Body, Encoding.UTF8, answer.ContentType ?? "application/json");
        }

        foreach (var (name, value) in answer.Headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }

    /// <summary>One request, answered: for hosting the twin anywhere (Matchmark.Conformance's UseMatchmarkTwin).</summary>
    public (int Status, string? ContentType, string? Body, IReadOnlyDictionary<string, string> Headers) Answer(
        string method, string path, string queryString, IReadOnlyDictionary<string, string> headers, string? body, string? contentType)
    {
        lock (_gate)
        {
            var answer = Answer(method, path, Query(queryString), headers, body, contentType);
            return (answer.Status, answer.ContentType, answer.Body, answer.Headers);
        }
    }

    private Reply Answer(string method, string path, List<KeyValuePair<string, string>> query, IReadOnlyDictionary<string, string> headers, string? body, string? contentType)
    {
        var reply = Inferred(method, path, query, headers, body, contentType);
        if (reply.Status is >= 400 and < 500)
        {
            foreach (var rejection in _rejections)
            {
                if (rejection(new TwinRejection(reply.Status, method, path, reply.Body is { Length: > 0 } text ? JsonNode.Parse(text) : null)) is { } answer)
                {
                    return new Reply(answer.Status, answer.Body is null ? null : answer.ContentType ?? reply.ContentType, answer.Body?.ToJsonString(), []);
                }
            }
        }

        return reply;
    }

    private Reply Inferred(string method, string path, List<KeyValuePair<string, string>> query, IReadOnlyDictionary<string, string> headers, string? body, string? contentType)
    {
        var (operation, values) = Match(method, path);
        if (operation is null)
        {
            return Problem(404, $"No operation for {method} {path}", null);
        }

        // The request, checked as the spec says: parameters, then the body.
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var parameter in operation.Parameters)
        {
            if (parameter.In == "querystring")
            {
                // The whole query, as the object it describes.
                var whole = Forms.Decode(string.Join("&", query.Select(q => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(q.Value)}")), parameter.Schema, _spec);
                foreach (var problem in query.Count == 0 && !parameter.Required ? [] : _validator.Validate(whole, parameter.Schema, direction: Direction.Request))
                {
                    Error(errors, problem);
                }

                continue;
            }

            var text = parameter.In switch
            {
                "path" => values.GetValueOrDefault(parameter.Name),
                "query" => query.FirstOrDefault(q => q.Key == parameter.Name).Value,
                "header" => headers.TryGetValue(parameter.Name, out var h) ? h : null,
                _ => null,
            };
            if (text is null)
            {
                if (parameter.Required)
                {
                    Error(errors, parameter.Name, $"{parameter.Name} is required");
                }

                continue;
            }

            foreach (var problem in _validator.Validate(Typed(text, parameter.Schema), parameter.Schema, parameter.Name, Direction.Request))
            {
                Error(errors, problem);
            }
        }

        JsonNode? json = null;
        if (operation.Body is { } schema)
        {
            if (string.IsNullOrEmpty(body))
            {
                if (schema.Required)
                {
                    Error(errors, "body", "A request body is required.");
                }
            }
            else if (!string.Equals(contentType, schema.MediaType, StringComparison.OrdinalIgnoreCase)
                     && !(OpenApiSpec.IsJson(schema.MediaType) && contentType is not null && OpenApiSpec.IsJson(contentType)))
            {
                return Problem(415, $"The body must be {schema.MediaType}", operation);
            }
            else
            {
                try
                {
                    json = Forms.IsForm(schema.MediaType) ? Forms.Decode(body, schema.Schema, _spec)
                        : OpenApiSpec.IsText(schema.MediaType) ? JsonValue.Create(body)
                        : JsonNode.Parse(body);
                }
                catch (JsonException)
                {
                    Error(errors, "body", "The body isn't JSON.");
                }

                foreach (var problem in errors.Count == 0 ? _validator.Validate(json, schema.Schema, direction: Direction.Request) : [])
                {
                    Error(errors, problem);
                }

                // The rules the spec declares (x-matchmark-rules), as a generated server enforces them.
                foreach (var (at, _, message, error) in errors.Count == 0 ? SchemaRules.Broken(_spec, json, schema.Schema) : [])
                {
                    if (error is null)
                    {
                        Error(errors, at == "$" ? "$" : at.TrimStart('$', '.'), message ?? "The value breaks a rule.");
                    }
                }
            }
        }

        // And its parameters' dependencies (x-dependencies).
        var given = operation.Parameters.Where(p => p.In == "query").Select(p => (p.Name, Value: query.FirstOrDefault(q => q.Key == p.Name).Value))
            .Where(p => p.Value is not null).ToDictionary(p => p.Name, p => p.Value!, StringComparer.Ordinal);
        foreach (var dependency in errors.Count == 0 ? operation.Dependencies : [])
        {
            if (!dependency.Holds(given))
            {
                Error(errors, dependency.Parameters.Count > 0 ? dependency.Parameters[0] : "query", $"Must satisfy {dependency.Source}");
            }
        }

        if (errors.Count > 0)
        {
            return Invalid(errors, operation);
        }

        // Idempotency-Key: the same key and body give the same answer; another body, 422.
        var key = headers.TryGetValue("Idempotency-Key", out var k) && operation.Parameters.Any(p => p.In == "header" && string.Equals(p.Name, "Idempotency-Key", StringComparison.OrdinalIgnoreCase))
            ? $"{operation.Id}|{k}"
            : null;
        if (key is not null && _keys.TryGetValue(key, out var seen))
        {
            return seen.Body == (body ?? string.Empty) ? seen.Answer : Problem(422, "That key was used with another body", operation);
        }

        foreach (var behaviour in _behaviours.GetValueOrDefault(operation.Id) ?? [])
        {
            var call = new TwinCall(operation.Id, values, query, json, _collections.ToDictionary(c => c.Key,
                c => (IReadOnlyList<JsonObject>)c.Value.Items.Values.Select(i => i.Resource).ToList(), StringComparer.Ordinal));
            if (behaviour(call) is { } owned)
            {
                return new Reply(owned.Status, owned.ContentType ?? operation.Response(owned.Status)?.Content.Keys.FirstOrDefault() ?? "application/json",
                    owned.Body?.ToJsonString(), []);
            }
        }

        var answer = Behave(operation, values, query, headers, json);
        if (key is not null && answer.Status is >= 200 and < 300)
        {
            _keys[key] = (body ?? string.Empty, answer);
        }

        return answer;
    }

    /// <summary>What the operation does to its collection, by its method and path.</summary>
    private Reply Behave(SpecOperation operation, Dictionary<string, string> values, List<KeyValuePair<string, string>> query, IReadOnlyDictionary<string, string> headers, JsonNode? body)
    {
        var (collection, idName) = Resource(operation);
        if (collection is null)
        {
            return Sampled(operation);
        }

        var store = _collections.TryGetValue(collection, out var existing) ? existing : _collections[collection] = new Collection();
        var item = operation.Path != collection;
        var id = idName is null ? null : values.GetValueOrDefault(idName);
        var found = id is not null && store.Items.TryGetValue(id, out var current) ? current : ((JsonObject Resource, int Version)?)null;

        // If-Match on a change: the resource as it was read, or 412.
        if (item && operation.Method is "PUT" or "PATCH" or "DELETE" && found is { } before && headers.TryGetValue("If-Match", out var match)
            && match != ETag(before.Version) && operation.Response(412) is not null)
        {
            return Problem(412, "The resource changed since that ETag", operation);
        }

        switch (operation.Method, item)
        {
            case ("POST", false):
                return Created(operation, store, body as JsonObject ?? [], NewId(operation, idName));
            case ("GET", false):
                return Listed(operation, store, query);
            case ("GET", true):
                return found is { } read ? View(operation, 200, read.Resource, read.Version) : NotFound(operation);
            case ("PUT", true):
                if (found is null)
                {
                    return operation.Response(201) is not null ? Created(operation, store, body as JsonObject ?? [], id!) : NotFound(operation);
                }

                var replaced = (JsonObject)(body as JsonObject ?? []).DeepClone();
                Keep(found.Value.Resource, replaced, operation, idName, id!);
                store.Items[id!] = (replaced, found.Value.Version + 1);
                return View(operation, 200, replaced, found.Value.Version + 1);
            case ("PATCH", true):
                if (found is null)
                {
                    return NotFound(operation);
                }

                var patched = (JsonObject)found.Value.Resource.DeepClone();
                Merge(patched, body as JsonObject ?? []);
                store.Items[id!] = (patched, found.Value.Version + 1);
                return View(operation, 200, patched, found.Value.Version + 1);
            case ("DELETE", true):
                if (found is null)
                {
                    return NotFound(operation);
                }

                store.Items.Remove(id!);
                return operation.Response(204) is not null ? new Reply(204, null, null, []) : View(operation, 200, found.Value.Resource, found.Value.Version);
            default:
                return Sampled(operation);
        }
    }

    /// <summary>The collection an operation is on, and its id parameter: /pets and /pets/{petId} are one, keyed by petId.</summary>
    private (string? Collection, string? Id) Resource(SpecOperation operation)
    {
        var item = Regex.Match(operation.Path, @"^(?<collection>.+)/\{(?<id>[^}]+)\}$");
        if (item.Success && _spec.Operations.Any(o => o.Path == item.Groups["collection"].Value))
        {
            return (item.Groups["collection"].Value, item.Groups["id"].Value);
        }

        var children = _spec.Operations.Select(o => Regex.Match(o.Path, "^" + Regex.Escape(operation.Path) + @"/\{(?<id>[^}]+)\}$")).FirstOrDefault(m => m.Success);
        return children is null ? (null, null) : (operation.Path, children.Groups["id"].Value);
    }

    private Reply Created(SpecOperation operation, Collection store, JsonObject body, string id)
    {
        var resource = (JsonObject)body.DeepClone();
        var (_, idName) = Resource(operation);
        Keep(null, resource, operation, idName, id);
        store.Items[id] = (resource, 1);
        var status = operation.Response(201) is not null ? 201 : 200;
        var answer = View(operation, status, resource, 1);
        if (status == 201 && operation.Response(201)?.Headers.Any(h => string.Equals(h.Name, "Location", StringComparison.OrdinalIgnoreCase)) == true)
        {
            answer.Headers["Location"] = $"{operation.Path.TrimEnd('/')}/{Uri.EscapeDataString(id)}";
        }

        return answer;
    }

    /// <summary>What the server sets on a resource it stores: its id, and any required read-only field, sampled.</summary>
    private void Keep(JsonObject? before, JsonObject resource, SpecOperation operation, string? idName, string id)
    {
        var schema = Success(operation) is { } s ? _spec.Resolve(s) : null;
        var idProperty = schema?["properties"] is JsonObject properties
            ? properties.Select(p => p.Key).FirstOrDefault(n => n == idName) ?? (properties.ContainsKey("id") ? "id" : null)
            : "id";
        if (idProperty is not null)
        {
            var idSchema = schema?["properties"]?[idProperty];
            resource[idProperty] = idSchema is not null && SchemaValidator.Types(_spec.Resolve(idSchema)).Contains("integer") && long.TryParse(id, out var number)
                ? JsonValue.Create(number)
                : JsonValue.Create(id);
        }

        foreach (var (name, property) in schema?["properties"] as JsonObject ?? [])
        {
            if (property is null || resource.ContainsKey(name))
            {
                continue;
            }

            // What the server sets: a read-only value it set before is kept; anything the response requires and the
            // request didn't send (a pet's status) is sampled.
            if (before?[name] is { } kept && _validator.Flag(property, "readOnly"))
            {
                resource[name] = kept.DeepClone();
            }
            else if ((schema!["required"] as JsonArray ?? []).Any(r => r?.GetValue<string>() == name) && _samples.TrySample(property, false, out var sample))
            {
                resource[name] = sample;
            }
        }
    }

    private string NewId(SpecOperation operation, string? idName)
    {
        var parameter = _spec.Operations.SelectMany(o => o.Parameters).FirstOrDefault(p => p.In == "path" && p.Name == idName);
        var schema = parameter is null ? null : _spec.Resolve(parameter.Schema);
        _ids++;
        return schema?["format"]?.GetValue<string>() == "uuid" ? new Guid((int)_ids, 0, 0, new byte[8]).ToString()
            : schema is not null && SchemaValidator.Types(schema).Contains("string") && _samples.TrySample(parameter!.Schema, false, out var sample) && sample is JsonValue v
                && !_validator.IsValid(JsonValue.Create(_ids.ToString(CultureInfo.InvariantCulture)), parameter.Schema)
                ? $"{v.GetValue<string>()}{_ids}"
                : _ids.ToString(CultureInfo.InvariantCulture);
    }

    private Reply Listed(SpecOperation operation, Collection store, List<KeyValuePair<string, string>> query)
    {
        var limit = operation.Parameters.FirstOrDefault(p => p.In == "query" && p.Name.ToLowerInvariant() is "limit" or "pagesize" or "page_size" or "per_page" or "top") is { } l
                    && query.FirstOrDefault(q => q.Key == l.Name).Value is { } text && int.TryParse(text, out var n) ? n : int.MaxValue;
        var items = store.Items.Values.Take(limit).Select(i => (JsonNode?)Shaped(Item(operation), i.Resource)).ToArray();
        var schema = Success(operation) is { } s ? _spec.Resolve(s) : [];
        JsonNode list = SchemaValidator.Types(schema).Contains("array")
            ? new JsonArray(items)
            : new JsonObject { [(schema["properties"] as JsonObject ?? []).FirstOrDefault(p => p.Value is not null && SchemaValidator.Types(_spec.Resolve(p.Value)).Contains("array")).Key ?? "items"] = new JsonArray(items) };
        return Json(operation, 200, list, []);
    }

    private Reply View(SpecOperation operation, int status, JsonObject resource, int version)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (operation.Response(status)?.Headers.Any(h => string.Equals(h.Name, "ETag", StringComparison.OrdinalIgnoreCase)) == true)
        {
            headers["ETag"] = ETag(version);
        }

        return Json(operation, status, Shaped(operation.Response(status)?.Content.Values.FirstOrDefault(c => c is not null), resource), headers);
    }

    /// <summary>The resource as the response's schema has it: writeOnly properties left out.</summary>
    private JsonObject Shaped(JsonNode? schema, JsonObject resource)
    {
        var view = (JsonObject)resource.DeepClone();
        foreach (var (name, property) in schema is null ? [] : _spec.Resolve(schema)["properties"] as JsonObject ?? [])
        {
            if (property is not null && _validator.Flag(property, "writeOnly"))
            {
                view.Remove(name);
            }
        }

        return view;
    }

    private JsonNode? Item(SpecOperation operation)
        => Success(operation) is { } schema && _spec.Resolve(schema) is var resolved
            ? resolved["items"] ?? (resolved["properties"] as JsonObject ?? []).Select(p => p.Value is null ? null : _spec.Resolve(p.Value)["items"]).FirstOrDefault(i => i is not null)
            : null;

    private static JsonNode? Success(SpecOperation operation)
        => operation.Responses.Where(r => r.Key.StartsWith('2')).OrderBy(r => r.Key, StringComparer.Ordinal).SelectMany(r => r.Value.Content.Values).FirstOrDefault(c => c is not null);

    private Reply Sampled(SpecOperation operation)
    {
        var (status, response) = operation.Responses.Where(r => r.Key.StartsWith('2')).OrderBy(r => r.Key, StringComparer.Ordinal)
            .Select(r => (int.Parse(r.Key, CultureInfo.InvariantCulture), r.Value)).FirstOrDefault();
        if (response is null)
        {
            return new Reply(200, null, null, []);
        }

        var (media, schema) = response.Content.FirstOrDefault();
        return schema is not null && _samples.TrySample(schema, true, Direction.Response, out var sample)
            ? new Reply(status, media, sample?.ToJsonString() ?? "null", [])
            : new Reply(status, null, null, []);
    }

    private static Reply Json(SpecOperation operation, int status, JsonNode body, Dictionary<string, string> headers)
        => new(status, operation.Response(status)?.Content.Keys.FirstOrDefault() ?? "application/json", body.ToJsonString(), headers);

    private static Reply NotFound(SpecOperation operation) => Problem(404, "Not found", operation);

    /// <summary>A problem for a status; none when the spec documents that status without a body, as a 404 often is.</summary>
    private static Reply Problem(int status, string title, SpecOperation? operation)
        => operation?.Response(status) is { Content.Count: 0 }
            ? new Reply(status, null, null, [])
            : new(status, operation?.Response(status)?.Content.Keys.FirstOrDefault() ?? "application/problem+json",
                new JsonObject { ["title"] = title, ["status"] = status }.ToJsonString(), []);

    private static Reply Invalid(Dictionary<string, List<string>> errors, SpecOperation operation)
        => new(400, operation.Response(400)?.Content.Keys.FirstOrDefault() ?? "application/problem+json", new JsonObject
        {
            ["title"] = "One or more validation errors occurred.",
            ["status"] = 400,
            ["errors"] = new JsonObject(errors.Select(e => KeyValuePair.Create(e.Key, (JsonNode?)new JsonArray(e.Value.Select(m => (JsonNode?)m).ToArray())))),
        }.ToJsonString(), []);

    private static void Error(Dictionary<string, List<string>> errors, string problem)
    {
        var key = problem.Split(' ')[0];
        Error(errors, key.Length == 0 || key is "the" or "value" ? "body" : key, problem);
    }

    private static void Error(Dictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var list))
        {
            errors[key] = list = [];
        }

        list.Add(message);
    }

    private static string ETag(int version) => $"\"v{version}\"";

    /// <summary>A JSON merge patch (RFC 7396): null removes, objects merge, anything else replaces.</summary>
    private static void Merge(JsonObject target, JsonObject patch)
    {
        foreach (var (name, value) in patch)
        {
            if (value is null)
            {
                target.Remove(name);
            }
            else if (value is JsonObject inner && target[name] is JsonObject existing)
            {
                Merge(existing, inner);
            }
            else
            {
                target[name] = value.DeepClone();
            }
        }
    }

    /// <summary>A parameter's text as the type its schema says, so the validator can check it.</summary>
    private JsonValue Typed(string text, JsonNode schema)
    {
        var types = SchemaValidator.Types(_spec.Resolve(schema));
        if ((types.Contains("integer") || types.Contains("number")) && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return JsonValue.Create(number);
        }

        if (types.Contains("boolean") && text is "true" or "false")
        {
            return JsonValue.Create(text == "true");
        }

        return JsonValue.Create(text);
    }

    internal (SpecOperation? Operation, Dictionary<string, string> Values) Match(string method, string path)
    {
        foreach (var operation in _spec.Operations.Where(o => o.Method == method).OrderBy(o => o.Path.Count(c => c == '{')))
        {
            var pattern = "^" + Regex.Replace(Regex.Escape(operation.Path), @"\\\{([^}]+)}", "(?<$1>[^/]+)") + "/?$";
            if (Regex.Match(path, pattern) is { Success: true } match)
            {
                return (operation, match.Groups.Keys.Where(k => !int.TryParse(k, out _)).ToDictionary(k => k, k => Uri.UnescapeDataString(match.Groups[k].Value), StringComparer.Ordinal));
            }
        }

        return (null, []);
    }

    internal static List<KeyValuePair<string, string>> Query(string query)
        => query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Select(p => new KeyValuePair<string, string>(Uri.UnescapeDataString(p[0].Replace('+', ' ')), p.Length > 1 ? Uri.UnescapeDataString(p[1].Replace('+', ' ')) : string.Empty))
            .ToList();
}

/// <summary>
/// A request the twin received, for a behaviour of its own: the path values, query, body and what it holds. The body is
/// what the twin stores when the behaviour lets the request through, and the resources are the stored ones themselves:
/// a behaviour that changes either changes what the twin keeps (an agent's Ledger behaviour relies on both).
/// </summary>
public sealed record TwinCall(
    string OperationId,
    IReadOnlyDictionary<string, string> Path,
    IReadOnlyList<KeyValuePair<string, string>> Query,
    JsonNode? Body,
    IReadOnlyDictionary<string, IReadOnlyList<JsonObject>> Resources);

/// <summary>A rejection the twin is about to answer: its status, the request, and the problem it would send.</summary>
public sealed record TwinRejection(int Status, string Method, string Path, JsonNode? Problem);

/// <summary>A behaviour's answer: the status, and a body (the media type is the spec's for that status unless given).</summary>
public sealed record TwinAnswer(int Status, JsonNode? Body = null, string? ContentType = null);
