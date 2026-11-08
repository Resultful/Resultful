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

        /// <summary>EF Core is referenced, so the Server part adds the helper that applies unique keys as indexes.</summary>
        public bool EntityFramework { get; set; }

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
            var keys = UniqueKeys();
            if (keys.Count == 0)
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

            var w = new CodeWriter(_namespace);
            w.Usings("System.Collections.Generic");
            w.Line($"/// <summary>A unique key the spec declares (x-matchmark-unique): the schema, its fields as JSON names, and whether strings compare ignoring case.</summary>");
            w.Line($"public sealed record {_apiName}UniqueKey(string Schema, IReadOnlyList<string> Fields, bool IgnoreCase);");
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
