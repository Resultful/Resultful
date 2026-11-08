using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Matchmark.Generator
{
    /// <summary>A rule of the completeness audit: what the spec should say, and what saying it unlocks.</summary>
    public sealed class AuditRule
    {
        internal AuditRule(string id, string level, string title, string unlocks)
        {
            Id = id;
            Level = level;
            Title = title;
            Unlocks = unlocks;
        }

        public string Id { get; }

        /// <summary><c>warning</c> or <c>note</c>, as SARIF names them.</summary>
        public string Level { get; }

        public string Title { get; }

        public string Unlocks { get; }
    }

    /// <summary>One thing the spec leaves out, where, and (when it can be measured) the change that would say it.</summary>
    public sealed class AuditFinding
    {
        private readonly Action<JsonObject>? _change;

        internal AuditFinding(AuditRule rule, string subject, string pointer, string message, string? operationId, Action<JsonObject>? change)
        {
            Rule = rule;
            Subject = subject;
            Location = pointer;
            Message = message;
            OperationId = operationId;
            _change = change;
        }

        public AuditRule Rule { get; }

        /// <summary>The operation or schema it's about, as a person names it (<c>createPet</c>, <c>Pet.name</c>).</summary>
        public string Subject { get; }

        /// <summary>Where, as a JSON Pointer into the (normalized) spec.</summary>
        public string Location { get; }

        public string Message { get; }

        /// <summary>The operation it's about, when it's about one.</summary>
        public string? OperationId { get; }

        /// <summary>Whether <see cref="Fixed"/> can make the change, so that what it adds can be measured.</summary>
        public bool CanFix => _change is not null;

        /// <summary>A copy of the spec with this finding's change made (the least that says what's missing), or null.</summary>
        public JsonObject? Fixed(JsonObject spec)
        {
            if (_change is null)
            {
                return null;
            }

            var copy = (JsonObject)spec.DeepClone();
            if (Audit.At(copy, Location) is not JsonObject target)
            {
                return null;
            }

            _change(target);
            return copy;
        }

        public override string ToString() => $"{Subject}: {Message}";
    }

    /// <summary>
    /// The completeness audit (COMPLETENESS.md): what a spec leaves out that the generated suite, fake and server would
    /// use. Static rules over the normalized spec; the CLI measures each finding by making its change and generating the
    /// suite again.
    /// </summary>
    public static class Audit
    {
        public static readonly AuditRule OperationId = new("operation-id", "warning",
            "Every operation has an operationId", "the operation is in the contract at all, and links and workflows can name it");

        public static readonly AuditRule BoundedString = new("bounded-string", "warning",
            "Every string a request carries has maxLength, pattern, format or enum", "boundary and too-long cases, and a server that can't be fed unbounded input");

        public static readonly AuditRule BoundedNumber = new("bounded-number", "warning",
            "Every number a request carries has a minimum and a maximum", "boundary and out-of-range cases");

        public static readonly AuditRule RejectionDocumented = new("rejection-documented", "warning",
            "Every way a request can be rejected is documented (400 for input, 404 for one resource)", "rejections are checked, not warned about");

        public static readonly AuditRule RequestExample = new("request-example", "note",
            "Request bodies have examples", "an example case, and realistic scaffolded and fake answers");

        public static readonly AuditRule ReadOnlyDeclared = new("read-only", "warning",
            "Server-assigned fields (id, createdAt...) in request schemas are readOnly", "the read-only-sent case and its check");

        public static readonly AuditRule AdditionalProperties = new("additional-properties", "note",
            "Objects a request carries state additionalProperties", "an unknown-property case: the suite knows whether an extra field is a bug");

        public static readonly AuditRule CreateLinked = new("create-linked", "warning",
            "Every create has links, or an Arazzo workflow reaches it", "read-after-write, update, delete and deleted-is-gone checks");

        public static readonly AuditRule SecurityDocumented = new("security-documented", "warning",
            "A secured operation documents 401", "the unauthenticated answer is checked");

        public static readonly AuditRule IdempotencyKey = new("idempotency-key", "note",
            "An unsafe create takes an Idempotency-Key", "IdempotentReplay and IdempotencyKeyReuse");

        public static readonly AuditRule Concurrency = new("concurrency", "note",
            "An update takes If-Match", "ETagChanges and StaleIfMatchRefused");

        public static readonly AuditRule UniqueDeclared = new("unique-declared", "note",
            "A create that documents 409 says which fields must be unique", "UniqueEnforced: duplicate, case-variant and renamed-into cases");

        public static readonly AuditRule Invariants = new("invariants", "note",
            "The spec states invariants the schema can't", "Postcondition, SchemaRule, TransitionRule and dependency cases");

        public static IReadOnlyList<AuditRule> Rules { get; } =
        [
            OperationId, BoundedString, BoundedNumber, RejectionDocumented, RequestExample, ReadOnlyDeclared, AdditionalProperties,
            CreateLinked, SecurityDocumented, IdempotencyKey, Concurrency, UniqueDeclared, Invariants,
        ];

        private static readonly string[] Methods = ["get", "put", "post", "delete", "options", "head", "patch", "trace", "query"];

        /// <summary>The component schema a response's body refers to, by name.</summary>
        private static string? Resource(JsonObject response)
            => (response["content"] as JsonObject)?.Select(c => c.Value?["schema"]?["$ref"]?.GetValue<string>()).FirstOrDefault(r => r != null) is string reference
                ? reference.Substring(reference.LastIndexOf('/') + 1)
                : null;

        private static readonly string[] ServerAssigned = ["id", "createdAt", "created_at", "updatedAt", "updated_at", "created", "modified"];

        /// <summary>Audits a normalized spec (JSON), with the Arazzo documents (JSON) that run against it.</summary>
        public static IReadOnlyList<AuditFinding> Run(string specJson, params string[] arazzoJson)
        {
            var spec = JsonNode.Parse(specJson) as JsonObject ?? throw new ArgumentException("The spec isn't a JSON object.", nameof(specJson));
            var findings = new List<AuditFinding>();
            var reached = new HashSet<string>(StringComparer.Ordinal);
            foreach (var arazzo in arazzoJson)
            {
                Strings(JsonNode.Parse(arazzo), "operationId", reached);
            }

            var schemas = spec["components"]?["schemas"] as JsonObject ?? new JsonObject();
            var discriminators = new HashSet<string>(StringComparer.Ordinal);
            Discriminators(spec, discriminators);
            var inputs = new HashSet<string>(StringComparer.Ordinal);
            var operations = Operations(spec).ToList();
            foreach (var (_, _, operation, _) in operations)
            {
                foreach (var parameter in Parameters(spec, operation))
                {
                    Refs(parameter["schema"], schemas, inputs);
                }

                foreach (var media in Content(Resolve(spec, operation["requestBody"])))
                {
                    Refs(media.Value?["schema"], schemas, inputs);
                }
            }

            foreach (var (path, method, operation, pointer) in operations)
            {
                var id = operation["operationId"]?.GetValue<string>();
                var name = id ?? $"{method.ToUpperInvariant()} {path}";
                if (id is null)
                {
                    var made = method + string.Concat(path.Split('/', '{', '}', '-', '_').Where(p => p.Length > 0).Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
                    findings.Add(new AuditFinding(OperationId, name, pointer, "has no operationId", null, o => o["operationId"] = made));
                }

                var responses = operation["responses"] as JsonObject ?? new JsonObject();
                bool Documents(params string[] statuses) => statuses.Any(responses.ContainsKey) || responses.ContainsKey("default");
                var parameters = Parameters(spec, operation).ToList();
                var body = Resolve(spec, operation["requestBody"]);

                foreach (var (parameter, index) in parameters.Select((p, i) => (p, i)))
                {
                    if (parameter["schema"] is JsonObject schema && schema["$ref"] is null)
                    {
                        var at = operation["parameters"] is JsonArray own && own.Count > index && own[index] is JsonObject o && o["$ref"] is null
                            ? $"{pointer}/parameters/{index}/schema"
                            : null;
                        if (at is not null)
                        {
                            Bounds(schema, at, $"{name} {parameter["in"]}.{parameter["name"]}", id, findings);
                        }
                    }
                }

                foreach (var media in Content(body))
                {
                    var mediaPointer = $"{pointer}/requestBody/content/{Escape(media.Key)}";
                    if (media.Value?["schema"] is JsonObject schema)
                    {
                        if (schema["$ref"] is null)
                        {
                            Walk(schema, $"{mediaPointer}/schema", $"{name} body", id, findings, 0, discriminators);
                        }

                        if (media.Key.Contains("json") && media.Value["example"] is null && media.Value["examples"] is null
                            && Resolve(spec, schema)?["example"] is null && Resolve(spec, schema)?["examples"] is null)
                        {
                            findings.Add(new AuditFinding(RequestExample, name, mediaPointer, $"the {media.Key} body has no example", id, null));
                        }
                    }
                }

                if ((parameters.Any(p => (string?)p["in"] != "path") || body is not null) && !Documents("400", "4XX", "422"))
                {
                    findings.Add(new AuditFinding(RejectionDocumented, name, $"{pointer}/responses",
                        "documents no 400: how bad input is rejected is unchecked", id, r => r["400"] = Problem("The request is invalid")));
                }

                if (parameters.Any(p => (string?)p["in"] == "path") && !Documents("404", "4XX"))
                {
                    findings.Add(new AuditFinding(RejectionDocumented, name, $"{pointer}/responses",
                        "acts on one resource but documents no 404", id, r => r["404"] = Problem("No such resource")));
                }

                var security = operation["security"] as JsonArray ?? spec["security"] as JsonArray;
                if (security is { Count: > 0 } && security.All(s => s is JsonObject { Count: > 0 }) && !Documents("401", "4XX"))
                {
                    findings.Add(new AuditFinding(SecurityDocumented, name, $"{pointer}/responses",
                        "is secured but documents no 401", id, r => r["401"] = Problem("Not authenticated")));
                }

                var created = responses["201"] as JsonObject;
                if (method == "post" && created is not null)
                {
                    if (!parameters.Any(p => (string?)p["in"] == "header" && string.Equals((string?)p["name"], "Idempotency-Key", StringComparison.OrdinalIgnoreCase)))
                    {
                        findings.Add(new AuditFinding(IdempotencyKey, name, pointer, "creates without an Idempotency-Key header: a retried request can create twice", id, null));
                    }

                    // A documented 409 on a create is usually a unique key the twin and the suite can't know (invariants.md),
                    // unless the create takes an Idempotency-Key, whose 409 is the draft's "still being processed".
                    var keyed = parameters.Any(p => (string?)p["in"] == "header" && string.Equals((string?)p["name"], "Idempotency-Key", StringComparison.OrdinalIgnoreCase));
                    if (responses.ContainsKey("409") && !keyed && Resource(created) is { } resource && schemas[resource] is JsonObject resourceSchema
                        && resourceSchema["x-matchmark-unique"] is null)
                    {
                        findings.Add(new AuditFinding(UniqueDeclared, name, $"#/components/schemas/{resource}",
                            $"documents a 409 but {resource} declares no unique key: say which fields must be unique (x-matchmark-unique)", id, null));
                    }

                    if (created["links"] is not JsonObject { Count: > 0 } && (id is null || !reached.Contains(id)))
                    {
                        findings.Add(new AuditFinding(CreateLinked, name, $"{pointer}/responses/201",
                            "has no links, and no Arazzo workflow reaches it: nothing checks what it created can be read back",
                            id, Link(spec, path, created)));
                    }
                }

                if (method is "put" or "patch" && parameters.Any(p => (string?)p["in"] == "path")
                    && !parameters.Any(p => (string?)p["in"] == "header" && string.Equals((string?)p["name"], "If-Match", StringComparison.OrdinalIgnoreCase)))
                {
                    findings.Add(new AuditFinding(Concurrency, name, pointer, "changes a resource without If-Match: a concurrent change can be lost", id, null));
                }
            }

            foreach (var schema in schemas.Where(s => inputs.Contains(s.Key) && s.Value is JsonObject))
            {
                Walk((JsonObject)schema.Value!, $"/components/schemas/{Escape(schema.Key)}", schema.Key, null, findings, 0, discriminators);
            }

            var declared = new HashSet<string>(StringComparer.Ordinal);
            Keys(spec, declared);
            if (!declared.Overlaps(["x-matchmark-ensures", "x-matchmark-rules", "x-matchmark-transitions", "x-matchmark-relations", "x-dependencies"]))
            {
                findings.Add(new AuditFinding(Invariants, "the spec", string.Empty,
                    "states no invariants (x-matchmark-ensures, x-matchmark-rules, x-matchmark-transitions, x-dependencies); see COMPLETENESS.md", null, null));
            }

            return findings;
        }

        /// <summary>The rules a schema in a request breaks, then those of its properties and items (not following $ref).</summary>
        private static void Walk(JsonObject schema, string pointer, string subject, string? operationId, List<AuditFinding> findings, int depth,
            HashSet<string> discriminators)
        {
            if (depth > 16 || schema["$ref"] is not null)
            {
                return;
            }

            Bounds(schema, pointer, subject, operationId, findings);
            if (schema["properties"] is JsonObject properties)
            {
                if (schema["additionalProperties"] is null && schema["unevaluatedProperties"] is null)
                {
                    findings.Add(new AuditFinding(AdditionalProperties, subject, pointer,
                        "doesn't say whether other properties are allowed", operationId, s => s["additionalProperties"] = false));
                }

                foreach (var property in properties)
                {
                    if (property.Value is not JsonObject child)
                    {
                        continue;
                    }

                    var at = $"{pointer}/properties/{Escape(property.Key)}";
                    if (ServerAssigned.Contains(property.Key, StringComparer.Ordinal) && child["readOnly"] is null && child["$ref"] is null)
                    {
                        findings.Add(new AuditFinding(ReadOnlyDeclared, $"{subject}.{property.Key}", at,
                            "looks assigned by the server but isn't readOnly", operationId, s => s["readOnly"] = true));
                    }

                    // A discriminator's value is bounded by the mapping, whatever its schema says.
                    if (child["readOnly"]?.GetValueKind() != JsonValueKind.True && !discriminators.Contains(property.Key))
                    {
                        Walk(child, at, $"{subject}.{property.Key}", operationId, findings, depth + 1, discriminators);
                    }
                }
            }

            if (schema["items"] is JsonObject items)
            {
                Walk(items, $"{pointer}/items", $"{subject}[]", operationId, findings, depth + 1, discriminators);
            }

            foreach (var keyword in new[] { "allOf", "oneOf", "anyOf" })
            {
                foreach (var (member, index) in (schema[keyword] as JsonArray ?? new JsonArray()).Select((m, i) => (m, i)))
                {
                    if (member is JsonObject m)
                    {
                        Walk(m, $"{pointer}/{keyword}/{index}", subject, operationId, findings, depth + 1, discriminators);
                    }
                }
            }
        }

        private static void Bounds(JsonObject schema, string pointer, string subject, string? operationId, List<AuditFinding> findings)
        {
            var types = Types(schema);
            if (schema["enum"] is not null || schema["const"] is not null)
            {
                return;
            }

            if (types.Contains("string") && schema["maxLength"] is null && schema["pattern"] is null && schema["format"] is null)
            {
                findings.Add(new AuditFinding(BoundedString, subject, pointer, "is a string with no maxLength, pattern, format or enum", operationId,
                    s => s["maxLength"] = 256));
            }

            if (types.Contains("integer") || types.Contains("number"))
            {
                var low = schema["minimum"] is null && schema["exclusiveMinimum"] is null;
                var high = schema["maximum"] is null && schema["exclusiveMaximum"] is null;
                if (low || high)
                {
                    var missing = low && high ? "no minimum or maximum" : low ? "no minimum" : "no maximum";
                    findings.Add(new AuditFinding(BoundedNumber, subject, pointer, $"is a number with {missing}", operationId, s =>
                    {
                        if (low)
                        {
                            s["minimum"] = 0;
                        }

                        if (high)
                        {
                            s["maximum"] = 1_000_000;
                        }
                    }));
                }
            }
        }

        /// <summary>A link from a create to reading what it created: the GET on the path plus one parameter, by the body's id.</summary>
        private static Action<JsonObject>? Link(JsonObject spec, string path, JsonObject created)
        {
            var paths = spec["paths"] as JsonObject ?? new JsonObject();
            var read = paths.Select(p => (p.Key, Get: p.Value?["get"] as JsonObject))
                .FirstOrDefault(p => p.Get?["operationId"] is not null && p.Key.StartsWith(path.TrimEnd('/') + "/{", StringComparison.Ordinal)
                                     && p.Key.IndexOf('/', path.TrimEnd('/').Length + 1) < 0 && p.Key.EndsWith("}", StringComparison.Ordinal));
            var schema = Content(created).Select(c => Resolve(spec, c.Value?["schema"])).FirstOrDefault(s => s is not null);
            if (read.Get is null || schema?["properties"] is not JsonObject properties || !properties.ContainsKey("id"))
            {
                return null;
            }

            var parameter = read.Key.Substring(read.Key.LastIndexOf('{') + 1).TrimEnd('}');
            var target = read.Get["operationId"]!.GetValue<string>();
            return response => response["links"] = new JsonObject
            {
                [target] = new JsonObject { ["operationId"] = target, ["parameters"] = new JsonObject { [parameter] = "$response.body#/id" } },
            };
        }

        private static JsonObject Problem(string description) => new()
        {
            ["description"] = description,
            ["content"] = new JsonObject { ["application/problem+json"] = new JsonObject { ["schema"] = new JsonObject { ["type"] = "object" } } },
        };

        private static IEnumerable<(string Path, string Method, JsonObject Operation, string Pointer)> Operations(JsonObject spec)
        {
            foreach (var path in spec["paths"] as JsonObject ?? new JsonObject())
            {
                foreach (var method in Methods)
                {
                    if (path.Value?[method] is JsonObject operation)
                    {
                        yield return (path.Key, method, operation, $"/paths/{Escape(path.Key)}/{method}");
                    }
                }
            }
        }

        /// <summary>The operation's parameters and its path's, resolved; the operation's win.</summary>
        private static IEnumerable<JsonObject> Parameters(JsonObject spec, JsonObject operation)
        {
            var own = (operation["parameters"] as JsonArray ?? new JsonArray()).Select(p => Resolve(spec, p)).OfType<JsonObject>().ToList();
            var shared = (operation.Parent?["parameters"] as JsonArray ?? new JsonArray()).Select(p => Resolve(spec, p)).OfType<JsonObject>()
                .Where(p => !own.Any(o => (string?)o["name"] == (string?)p["name"] && (string?)o["in"] == (string?)p["in"]));
            return own.Concat(shared);
        }

        private static IEnumerable<KeyValuePair<string, JsonNode?>> Content(JsonNode? holder)
            => holder?["content"] as JsonObject ?? Enumerable.Empty<KeyValuePair<string, JsonNode?>>();

        private static JsonObject? Resolve(JsonObject spec, JsonNode? node)
        {
            for (var i = 0; i < 32 && node is JsonObject o && o["$ref"]?.GetValue<string>() is { } reference; i++)
            {
                node = reference.StartsWith("#", StringComparison.Ordinal) ? At(spec, reference.Substring(1)) : null;
            }

            return node as JsonObject;
        }

        /// <summary>The node at a JSON Pointer, or null.</summary>
        public static JsonNode? At(JsonNode root, string location)
        {
            JsonNode? node = root;
            foreach (var part in location.Split('/').Skip(1).Select(p => p.Replace("~1", "/").Replace("~0", "~")))
            {
                node = node switch
                {
                    JsonObject o => o[part],
                    JsonArray a when int.TryParse(part, out var i) && i < a.Count => a[i],
                    _ => null,
                };
            }

            return node;
        }

        private static string Escape(string key) => key.Replace("~", "~0").Replace("/", "~1");

        private static List<string> Types(JsonObject schema) => schema["type"] switch
        {
            JsonValue v when v.GetValueKind() == JsonValueKind.String => [v.GetValue<string>()],
            JsonArray a => a.Select(t => t?.GetValue<string>()).Where(t => t is not null).Select(t => t!).ToList(),
            _ => [],
        };

        /// <summary>Every component schema a schema refers to, transitively.</summary>
        private static void Refs(JsonNode? node, JsonObject schemas, HashSet<string> found)
        {
            switch (node)
            {
                case JsonObject o:
                    if (o["$ref"]?.GetValue<string>() is { } reference && reference.StartsWith("#/components/schemas/", StringComparison.Ordinal))
                    {
                        var name = reference.Substring("#/components/schemas/".Length).Replace("~1", "/").Replace("~0", "~");
                        if (found.Add(name))
                        {
                            Refs(schemas[name], schemas, found);
                        }
                    }

                    foreach (var child in o)
                    {
                        Refs(child.Value, schemas, found);
                    }

                    break;
                case JsonArray a:
                    foreach (var child in a)
                    {
                        Refs(child, schemas, found);
                    }

                    break;
            }
        }

        private static void Strings(JsonNode? node, string key, HashSet<string> found)
        {
            switch (node)
            {
                case JsonObject o:
                    foreach (var child in o)
                    {
                        if (child.Key == key && child.Value is JsonValue v && v.GetValueKind() == JsonValueKind.String)
                        {
                            found.Add(v.GetValue<string>());
                        }

                        Strings(child.Value, key, found);
                    }

                    break;
                case JsonArray a:
                    foreach (var child in a)
                    {
                        Strings(child, key, found);
                    }

                    break;
            }
        }

        private static void Discriminators(JsonNode? node, HashSet<string> found)
        {
            switch (node)
            {
                case JsonObject o:
                    if (o["discriminator"]?["propertyName"] is JsonValue v && v.GetValueKind() == JsonValueKind.String)
                    {
                        found.Add(v.GetValue<string>());
                    }

                    foreach (var child in o)
                    {
                        Discriminators(child.Value, found);
                    }

                    break;
                case JsonArray a:
                    foreach (var child in a)
                    {
                        Discriminators(child, found);
                    }

                    break;
            }
        }

        private static void Keys(JsonNode? node, HashSet<string> found)
        {
            switch (node)
            {
                case JsonObject o:
                    foreach (var child in o)
                    {
                        found.Add(child.Key);
                        Keys(child.Value, found);
                    }

                    break;
                case JsonArray a:
                    foreach (var child in a)
                    {
                        Keys(child, found);
                    }

                    break;
            }
        }
    }
}
