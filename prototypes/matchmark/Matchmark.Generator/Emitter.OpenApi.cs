using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.OpenApi;

namespace Matchmark.Generator
{
    /// <summary>The emitter's the served OpenAPI document (decision Q10: the model and its outputs, split by output).</summary>
    internal sealed partial class Emitter
    {
        // ---------------------------------------------------------------- the served OpenAPI document

        /// <summary>
        /// Configuration for ASP.NET Core's own OpenAPI document, so the server describes its schemas the way the spec
        /// does. It renames nothing the spec did not name and invents nothing: every rule below is driven by the spec's
        /// own unions and enums, and the round-trip test compares the result against the spec.
        /// </summary>
        private string EmitOpenApi(string? typesNamespace = null)
        {
            var w = new CodeWriter(_namespace);
            w.Usings("System.Collections.Generic", "System.Linq", "System.Threading.Tasks", "Microsoft.AspNetCore.OpenApi", "Microsoft.OpenApi");

            w.Open($"public static class {_apiName}OpenApi");

            w.Line("/// <summary>The spec's oneOf unions: base schema, discriminator property, and each member with its discriminator value.</summary>");
            w.Line("private static readonly (string Union, string Discriminator, (string Member, string Value)[] Members)[] Unions =");
            w.Line("[");
            foreach (var union in _unionMembers.GroupBy(m => m.Value.Base))
            {
                var members = string.Join(", ", union.Select(m => $"({Naming.Literal(Naming.Pascal(m.Key))}, {Naming.Literal(m.Value.Value)})"));
                w.Line($"    ({Naming.Literal(Naming.Pascal(union.Key))}, {Naming.Literal(union.First().Value.Property)}, [{members}]),");
            }

            w.Line("];");
            w.Line();
            var enums = _schemas.Where(s => IsEnum(s.Value)).Select(s => Naming.Literal(Naming.Pascal(s.Key)));
            w.Line($"private static readonly string[] StringEnums = [{string.Join(", ", enums)}];");
            w.Line();

            w.Line("/// <summary>");
            w.Line("/// Describes the generated types as the spec does: schemas named after their types, and oneOf unions, their members and");
            w.Line("/// string enums written in the spec's form rather than ASP.NET Core's.");
            w.Line("/// </summary>");
            w.Open($"public static OpenApiOptions Add{_apiName}Schemas(this OpenApiOptions options)");
            w.Line($"options.CreateSchemaReferenceId = type => type.Type.Namespace == {Naming.Literal(typesNamespace ?? _namespace)}");
            w.Line("    ? type.Type.Name");
            w.Line("    : OpenApiOptions.CreateDefaultSchemaReferenceId(type);");
            w.Line("options.AddDocumentTransformer((document, _, _) =>");
            w.Line("{");
            w.Line("    Describe(document);");
            w.Line("    return Task.CompletedTask;");
            w.Line("});");
            w.Line("return options;");
            w.Close();
            w.Line();

            w.Open("private static void Describe(OpenApiDocument document)");
            w.Line("var schemas = document.Components?.Schemas;");
            w.Open("if (schemas is null)");
            w.Line("return;");
            w.Close();
            w.Line();

            w.Open("foreach (var (union, discriminator, members) in Unions)");
            w.Open("if (!schemas.TryGetValue(union, out var found) || found is not OpenApiSchema schema || schema.Discriminator?.Mapping is not { } mapping)");
            w.Line("continue;");
            w.Close();
            w.Line();
            w.Line("// ASP.NET Core writes the union as anyOf over members it names '{Union}{Member}'; the spec writes oneOf over the members' own names.");
            w.Open("foreach (var (member, value) in members)");
            w.Open("if (mapping.TryGetValue(value, out var served) && served.Reference.Id is { } servedId && servedId != member && schemas.Remove(servedId, out var memberSchema))");
            w.Line("schemas[member] = memberSchema;");
            w.Close();
            w.Line();
            w.Line("mapping[value] = new OpenApiSchemaReference(member, document);");
            w.Line();
            w.Line("// The discriminator is an ordinary required string property of each member, not a single-value enum.");
            w.Open("if (schemas.TryGetValue(member, out var m) && m is OpenApiSchema memberObject)");
            w.Line("memberObject.Type ??= JsonSchemaType.Object;");
            w.Line("memberObject.Properties ??= new System.Collections.Generic.Dictionary<string, IOpenApiSchema>();");
            w.Line("memberObject.Properties[discriminator] = new OpenApiSchema { Type = JsonSchemaType.String };");
            w.Line("memberObject.Required ??= new System.Collections.Generic.HashSet<string>();");
            w.Line("memberObject.Required.Add(discriminator);");
            w.Close();
            w.Close();
            w.Line();
            w.Line("schema.OneOf = members.Select(m => (IOpenApiSchema)new OpenApiSchemaReference(m.Member, document)).ToList();");
            w.Line("schema.AnyOf = null;");
            w.Line("schema.Type = null;");
            w.Line("schema.Required = null;");
            w.Line("schema.Properties = null;");
            w.Close();
            w.Line();

            w.Open("foreach (var name in StringEnums)");
            w.Open("if (schemas.TryGetValue(name, out var e) && e is OpenApiSchema enumSchema)");
            w.Line("enumSchema.Type = JsonSchemaType.String;");
            w.Close();
            w.Close();
            w.Close();

            w.Close();
            return w.ToString();
        }
    }
}
