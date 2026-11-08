using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.OpenApi;

namespace Matchmark.Generator
{
    /// <summary>
    /// Invariants across a collection (docs/reference/invariants.md, decisions Q44 and Q46): the spec's unique keys as
    /// metadata in the Contract part (<c>{Api}Invariants</c>), and, when the project references EF Core, a helper that
    /// applies them to a service's entities as unique indexes. The generator enforces nothing itself: a check before a
    /// write races with another request, so enforcement is the service's storage, and the suite proves it.
    /// </summary>
    internal sealed partial class Emitter
    {
        private static readonly int[] UniqueStatuses = { 409, 422, 400 };

        private static readonly int[] ReferenceStatuses = { 422, 404, 400 };

        /// <summary>Schemas with a reference that names no status of its own, so a create must document one (MM028).</summary>
        private readonly HashSet<string> _undeclaredStatus = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>EF Core is referenced, so the Server part adds the helper that applies unique keys as indexes.</summary>
        public bool EntityFramework { get; set; }

        /// <summary>Whether the project references Microsoft.Extensions.Validation, so the contract gets a resolver for it (Q45).</summary>
        public bool NetValidation { get; set; }

        /// <summary>Each schema's declared unique keys: (schema, fields, ignoreCase), checked against the schema's properties.</summary>
        private List<(string Schema, List<string> Fields, bool IgnoreCase)> UniqueKeys()
        {
            var keys = new List<(string, List<string>, bool)>();
            foreach (var pair in _document.Components?.Schemas ?? new Dictionary<string, IOpenApiSchema>())
            {
                if (!(Extension(pair.Value.Extensions, "x-matchmark-unique") is JsonArray declared))
                {
                    continue;
                }

                var properties = AllProperties(pair.Value);
                foreach (var key in declared.OfType<JsonObject>())
                {
                    var fields = (key["fields"] as JsonArray ?? new JsonArray()).Select(f => f?.GetValue<string>()).OfType<string>().ToList();
                    var unknown = fields.Where(f => !properties.Contains(f.Split('.')[0])).ToList();
                    if (fields.Count == 0 || unknown.Count > 0)
                    {
                        Diagnostics.Add(Diagnostic.Create(Generator.Diagnostics.InvariantDeclaredWrongly, _location,
                            $"{pair.Key}'s x-matchmark-unique", fields.Count == 0 ? "a key with no fields" : $"unknown fields {string.Join(", ", unknown)}"));
                        continue;
                    }

                    keys.Add((pair.Key, fields, key["ignoreCase"]?.GetValue<bool>() == true));
                }
            }

            return keys;
        }

        /// <summary>Each schema's declared references: (schema, field, target, onDelete), checked against the schema and the spec's paths.</summary>
        private List<(string Schema, string Field, string Target, string OnDelete)> References()
        {
            var references = new List<(string, string, string, string)>();
            foreach (var pair in _document.Components?.Schemas ?? new Dictionary<string, IOpenApiSchema>())
            {
                if (!(Extension(pair.Value.Extensions, "x-matchmark-references") is JsonObject declared))
                {
                    continue;
                }

                var properties = AllProperties(pair.Value);
                foreach (var reference in declared)
                {
                    var target = reference.Value?["target"]?.GetValue<string>();
                    var onDelete = reference.Value?["onDelete"]?.GetValue<string>() ?? "restrict";
                    string? wrong = !properties.Contains(reference.Key) ? $"an unknown field {reference.Key}"
                        : target is null ? $"no target for {reference.Key}"
                        : _document.Paths?.ContainsKey(target) != true ? $"a target path the spec doesn't have, {target}"
                        : !(onDelete is "restrict" or "cascade" or "clear" or "none") ? $"onDelete '{onDelete}' for {reference.Key}, not restrict, cascade, clear or none"
                        : onDelete == "clear" && !Nullable(pair.Value, reference.Key) ? $"onDelete clear on {reference.Key}, which can't be null"
                        : null;
                    if (wrong is not null)
                    {
                        Diagnostics.Add(Diagnostic.Create(Generator.Diagnostics.InvariantDeclaredWrongly, _location, $"{pair.Key}'s x-matchmark-references", wrong));
                        continue;
                    }

                    references.Add((pair.Key, reference.Key, target!, onDelete));
                    if (reference.Value?["status"] is null)
                    {
                        _undeclaredStatus.Add(pair.Key);
                    }
                }
            }

            return references;
        }

        /// <summary>
        /// MM027 for derived fields and list orders that name what isn't there: a derived field that isn't a property, or one
        /// that a request can set (not readOnly, on a schema a request body uses); an order field the list's items don't have.
        /// </summary>
        private void CheckDerivedAndOrder(List<Operation> operations)
        {
            var requestSchemas = new HashSet<string>(operations.Where(o => o.Body is not null).Select(o => o.Body!.Type.TrimEnd('?')), StringComparer.Ordinal);
            foreach (var pair in _document.Components?.Schemas ?? new Dictionary<string, IOpenApiSchema>())
            {
                if (!(Extension(pair.Value.Extensions, "x-matchmark-derived") is JsonObject derived))
                {
                    continue;
                }

                var properties = AllProperties(pair.Value);
                foreach (var field in derived.Select(d => d.Key))
                {
                    var wrong = !properties.Contains(field) ? $"an unknown field {field}"
                        : requestSchemas.Contains(pair.Key) && pair.Value.Properties?.TryGetValue(field, out var property) == true && property.ReadOnly != true
                            ? $"{field}, which a request can set: derive it on a readOnly field"
                        : null;
                    if (wrong is not null)
                    {
                        Diagnostics.Add(Diagnostic.Create(Generator.Diagnostics.InvariantDeclaredWrongly, _location, $"{pair.Key}'s x-matchmark-derived", wrong));
                    }
                }
            }

            foreach (var path in _document.Paths ?? new OpenApiPaths())
            {
                foreach (var operation in path.Value.Operations ?? new Dictionary<HttpMethod, OpenApiOperation>())
                {
                    if (!(Extension(operation.Value.Extensions, "x-matchmark-order")?["by"] is JsonArray by))
                    {
                        continue;
                    }

                    var items = operation.Value.Responses?.Where(r => r.Key.StartsWith("2", StringComparison.Ordinal))
                        .Select(r => r.Value.Content?.Values.FirstOrDefault()?.Schema).FirstOrDefault(s => s is not null);
                    var item = items?.Items ?? items?.Properties?.Values.FirstOrDefault(p => p.Items is not null)?.Items;
                    if (item is null)
                    {
                        continue;
                    }

                    var properties = AllProperties(item);
                    foreach (var field in by.Select(b => b?.GetValue<string>()?.Split(' ')[0]).OfType<string>().Where(f => !properties.Contains(f.Split('.')[0])))
                    {
                        Diagnostics.Add(Diagnostic.Create(Generator.Diagnostics.InvariantDeclaredWrongly, _location, $"{operation.Value.OperationId}'s x-matchmark-order", $"an unknown field {field}"));
                    }
                }
            }
        }

        /// <summary>
        /// MM027 for a limit declared wrongly (a max below 1, an unknown scope); MM028 for a create of a limited schema that
        /// documents no status to refuse with, unless the limit names its own.
        /// </summary>
        private void CheckLimits(List<Operation> operations)
        {
            foreach (var pair in _document.Components?.Schemas ?? new Dictionary<string, IOpenApiSchema>())
            {
                if (!(Extension(pair.Value.Extensions, "x-matchmark-limit") is JsonObject limit))
                {
                    continue;
                }

                // YAML may give the number as text.
                var max = limit["max"] is JsonValue m && int.TryParse(m.ToString(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
                var scope = limit["scope"]?.GetValue<string>() ?? "collection";
                var wrong = max < 1 ? "a max that isn't a whole number of at least 1"
                    : scope is not ("collection" or "parent" or "principal") ? $"an unknown scope {scope}"
                    : null;
                if (wrong is not null)
                {
                    Diagnostics.Add(Diagnostic.Create(Generator.Diagnostics.InvariantDeclaredWrongly, _location, $"{pair.Key}'s x-matchmark-limit", wrong));
                    continue;
                }

                if (limit["status"] is null)
                {
                    foreach (var operation in operations.Where(o => o.Method == "POST" && Creates(o, pair.Key) && !UniqueStatuses.Any(s => o.Errors.Any(e => e.Status == s))))
                    {
                        Diagnostics.Add(Diagnostic.Create(Generator.Diagnostics.InvariantStatusMissing, _location, operation.Id, $"{pair.Key}'s limit", "409, 422 or 400"));
                    }
                }
            }
        }

        /// <summary>
        /// MM027 for a conserved total declared wrongly: a field the schema doesn't have, one that isn't a number or a string
        /// (decimal strings count), or an excepted operation the spec doesn't have.
        /// </summary>
        private void CheckConserved(List<Operation> operations)
        {
            var ids = new HashSet<string>(operations.Select(o => o.Id), StringComparer.Ordinal);
            foreach (var pair in _document.Components?.Schemas ?? new Dictionary<string, IOpenApiSchema>())
            {
                if (!(Extension(pair.Value.Extensions, "x-matchmark-conserved") is JsonArray totals))
                {
                    continue;
                }

                foreach (var total in totals.OfType<JsonObject>())
                {
                    var field = total["total"]?.ToString();
                    var property = field is null ? null : Property(pair.Value, field);
                    var type = property?.Type ?? 0;
                    var wrong = field is null ? "no total field"
                        : property is null ? $"an unknown field {field}"
                        : !(type.HasFlag(JsonSchemaType.Number) || type.HasFlag(JsonSchemaType.Integer) || type.HasFlag(JsonSchemaType.String))
                            ? $"{field}, which isn't a number or a decimal string"
                        : (total["except"] as JsonArray ?? new JsonArray()).Select(e => e?.ToString()).FirstOrDefault(e => e is not null && !ids.Contains(e)) is { } unknown
                            ? $"an unknown operation {unknown} in except"
                        : null;
                    if (wrong is not null)
                    {
                        Diagnostics.Add(Diagnostic.Create(Generator.Diagnostics.InvariantDeclaredWrongly, _location, $"{pair.Key}'s x-matchmark-conserved", wrong));
                    }
                }
            }
        }

        /// <summary>
        /// MM027 for a scope declared wrongly (by anything but principal, visible to anyone but owner); MM028 for a read of a
        /// scoped resource that documents neither 404 nor 403, unless the scope names its status.
        /// </summary>
        private void CheckScoped(List<Operation> operations)
        {
            foreach (var pair in _document.Components?.Schemas ?? new Dictionary<string, IOpenApiSchema>())
            {
                if (!(Extension(pair.Value.Extensions, "x-matchmark-scoped") is JsonObject scoped))
                {
                    continue;
                }

                var by = scoped["by"]?.ToString() ?? "principal";
                var visible = scoped["visible"]?.ToString() ?? "owner";
                var wrong = by != "principal" ? $"an unknown by {by}" : visible != "owner" ? $"an unknown visible {visible}" : null;
                if (wrong is not null)
                {
                    Diagnostics.Add(Diagnostic.Create(Generator.Diagnostics.InvariantDeclaredWrongly, _location, $"{pair.Key}'s x-matchmark-scoped", wrong));
                    continue;
                }

                if (scoped["status"] is null)
                {
                    foreach (var operation in operations.Where(o => o.Method == "GET" && o.Path.EndsWith("}", StringComparison.Ordinal) && Creates(o, pair.Key)
                                 && !o.Errors.Any(e => e.Status is 404 or 403)))
                    {
                        Diagnostics.Add(Diagnostic.Create(Generator.Diagnostics.InvariantStatusMissing, _location, operation.Id, $"{pair.Key}'s scope", "404 or 403"));
                    }
                }
            }
        }

        private static IOpenApiSchema? Property(IOpenApiSchema schema, string name)
            => schema.Properties is not null && schema.Properties.TryGetValue(name, out var property) ? property
               : (schema.AllOf ?? new List<IOpenApiSchema>()).Select(part => Property(part, name)).FirstOrDefault(p => p is not null);

        /// <summary>Whether a property's type allows null, so a reference to a deleted resource can be cleared.</summary>
        private static bool Nullable(IOpenApiSchema schema, string property)
            => schema.Properties is not null && schema.Properties.TryGetValue(property, out var value) && (value.Type ?? 0).HasFlag(JsonSchemaType.Null)
               || (schema.AllOf ?? new List<IOpenApiSchema>()).Any(part => Nullable(part, property));

        private static HashSet<string> AllProperties(IOpenApiSchema schema)
        {
            var names = new HashSet<string>(schema.Properties?.Keys ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            foreach (var part in schema.AllOf ?? new List<IOpenApiSchema>())
            {
                names.UnionWith(AllProperties(part));
            }

            return names;
        }

        /// <summary>The Contract part's <c>{Api}Invariants</c>, or null when the spec declares none; MM028 for creates with no status to refuse with.</summary>
        private string? EmitInvariants(List<Operation> operations)
        {
            CheckDerivedAndOrder(operations);
            CheckLimits(operations);
            CheckConserved(operations);
            CheckScoped(operations);
            var keys = UniqueKeys();
            var references = References();
            if (keys.Count == 0 && references.Count == 0)
            {
                return null;
            }

            foreach (var schema in keys.Select(k => k.Schema).Distinct())
            {
                foreach (var operation in operations.Where(o => o.Method == "POST" && Creates(o, schema)))
                {
                    if (!UniqueStatuses.Any(s => operation.Errors.Any(e => e.Status == s)))
                    {
                        Diagnostics.Add(Diagnostic.Create(Generator.Diagnostics.InvariantStatusMissing, _location, operation.Id, $"{schema}'s unique keys", "409, 422 or 400"));
                    }
                }
            }

            foreach (var schema in references.Select(r => r.Schema).Distinct().Where(_undeclaredStatus.Contains))
            {
                foreach (var operation in operations.Where(o => o.Method == "POST" && Creates(o, schema)))
                {
                    if (!ReferenceStatuses.Any(s => operation.Errors.Any(e => e.Status == s)))
                    {
                        Diagnostics.Add(Diagnostic.Create(Generator.Diagnostics.InvariantStatusMissing, _location, operation.Id, $"{schema}'s references", "422, 404 or 400"));
                    }
                }
            }

            var w = new CodeWriter(_namespace);
            w.Usings("System.Collections.Generic");
            w.Line($"/// <summary>A unique key the spec declares (x-matchmark-unique): the schema, its fields as JSON names, and whether strings compare ignoring case.</summary>");
            w.Line($"public sealed record {_apiName}UniqueKey(string Schema, IReadOnlyList<string> Fields, bool IgnoreCase);");
            w.Line();
            w.Line($"/// <summary>A reference the spec declares (x-matchmark-references): the schema, its field, the item path it refers to, and what a delete does.</summary>");
            w.Line($"public sealed record {_apiName}Reference(string Schema, string Field, string Target, string OnDelete);");
            w.Line();
            w.Line("/// <summary>");
            w.Line("/// The invariants across a collection the spec declares, as data: for a service to apply to its storage (a unique index),");
            w.Line("/// since the generated server can't enforce them without it. The twin enforces them, and the suite checks the service does.");
            w.Line("/// </summary>");
            w.Open($"public static class {_apiName}Invariants");
            w.Line($"public static IReadOnlyList<{_apiName}UniqueKey> Unique {{ get; }} = new {_apiName}UniqueKey[]");
            w.Line("{");
            foreach (var (schema, fields, ignoreCase) in keys)
            {
                w.Line($"    new({Naming.Literal(schema)}, new[] {{ {string.Join(", ", fields.Select(Naming.Literal))} }}, {(ignoreCase ? "true" : "false")}),");
            }

            w.Line("};");
            w.Line();
            w.Line($"public static IReadOnlyList<{_apiName}Reference> References {{ get; }} = new {_apiName}Reference[]");
            w.Line("{");
            foreach (var (schema, field, target, onDelete) in references)
            {
                w.Line($"    new({Naming.Literal(schema)}, {Naming.Literal(field)}, {Naming.Literal(target)}, {Naming.Literal(onDelete)}),");
            }

            w.Line("};");
            w.Close();
            return w.ToString();
        }

        /// <summary>Whether an operation's success response holds this schema.</summary>
        private static bool Creates(Operation operation, string schema)
            => operation.Successes.Any(s => s.BodyType == schema);

        /// <summary>The Server part's EF Core helper, when EF Core is referenced and the spec declares unique keys.</summary>
        private string? EmitInvariantsForEntityFramework()
        {
            if (!EntityFramework || UniqueKeysQuietly().Count == 0)
            {
                return null;
            }

            var w = new CodeWriter(_namespace);
            w.Usings("System", "System.Collections.Generic", "System.Linq", "Microsoft.EntityFrameworkCore");
            w.Line("/// <summary>Applies the spec's unique keys to a service's entities (decision Q46).</summary>");
            w.Open($"public static class {_apiName}InvariantsModelBuilderExtensions");
            w.Line("/// <summary>");
            w.Line("/// A unique index on <typeparamref name=\"TEntity\"/> for each of <paramref name=\"schema\"/>'s unique keys. The entity isn't the");
            w.Line("/// API model, so <paramref name=\"map\"/> names the entity property for a JSON field; unmapped fields use the field's name in");
            w.Line("/// Pascal case. Comparing ignoring case is the store's (a NOCASE column in SQLite, a case-insensitive collation elsewhere).");
            w.Line("/// </summary>");
            w.Open($"public static ModelBuilder Apply{_apiName}Unique<TEntity>(this ModelBuilder builder, string schema, IReadOnlyDictionary<string, string>? map = null) where TEntity : class");
            w.Line($"foreach (var key in {_apiName}Invariants.Unique.Where(k => k.Schema == schema))");
            w.Line("{");
            w.Line("    var properties = key.Fields.Select(f => map is not null && map.TryGetValue(f, out var p) ? p : char.ToUpperInvariant(f[0]) + f.Substring(1)).ToArray();");
            w.Line("    builder.Entity<TEntity>().HasIndex(properties).IsUnique();");
            w.Line("}");
            w.Line();
            w.Line("return builder;");
            w.Close();
            w.Close();
            return w.ToString();
        }

        private List<(string Schema, List<string> Fields, bool IgnoreCase)> UniqueKeysQuietly()
        {
            var count = Diagnostics.Count;
            var keys = UniqueKeys();
            Diagnostics.RemoveRange(count, Diagnostics.Count - count);
            return keys;
        }
    }
}
