using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;

namespace Matchmark.Generator
{
    /// <summary>A spec (YAML or JSON, OpenAPI 3.0 or 3.1) written as OpenAPI 3.1 JSON: the form the regression engine reads.</summary>
    public static class SpecJson
    {
        public static string Normalize(string text, string path) => Normalize(text, path, out _);

        /// <summary>The spec as OpenAPI 3.1 JSON, with what the reader objected to (and left out).</summary>
        public static string Normalize(string text, string path, out IReadOnlyList<string> problems)
            => Write(SpecReader.Read(text, path, out problems)
                ?? throw new InvalidOperationException($"{path} could not be read: {string.Join("; ", problems)}"));

        /// <summary>An Arazzo document (YAML or JSON) as JSON, for the regression engine.</summary>
        public static string Arazzo(string text, string path)
        {
            if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                return text;
            }

            var stream = new SharpYaml.Serialization.YamlStream();
            stream.Load(new StringReader(text));
            if (stream.Documents.Count == 0)
            {
                throw new InvalidOperationException($"{path} is empty.");
            }

            var json = Microsoft.OpenApi.YamlReader.YamlConverter.ToJsonNode(stream.Documents[0]);
            return json["arazzo"] is null ? throw new InvalidOperationException($"{path} has no 'arazzo' version, so it isn't an Arazzo document.") : json.ToJsonString();
        }

        internal static string Write(OpenApiDocument document)
        {
            using var writer = new StringWriter();
            document.SerializeAsV31(new OpenApiJsonWriter(writer));
            return writer.ToString();
        }
    }

    internal sealed partial class Emitter
    {
        /// <summary>
        /// The Regression part: the spec as OpenAPI 3.1 JSON, written at build time, and the regression suite generated
        /// from it by Matchmark.Testing. The suite can't drift from the spec, because it is rebuilt with it.
        /// </summary>
        private string EmitRegression(IReadOnlyList<string> workflows)
        {
            var w = new CodeWriter(_namespace);
            w.Line("/// <summary>");
            w.Line("/// The regression suite generated from the spec: examples, valid requests, boundary values, one invalid request");
            w.Line("/// per constraint, and workflows from the spec's links. Run it against any server with");
            w.Line($"/// <c>{_apiName}Regression.Suite.RunAsync(httpClient)</c>.");
            w.Line("/// </summary>");
            w.Open($"public static class {_apiName}Regression");
            w.Line("/// <summary>The spec, as OpenAPI 3.1 JSON.</summary>");
            w.Line($"public const string Spec = @\"{SpecJson.Write(_document).Replace("\"", "\"\"")}\";");
            w.Line();
            w.Line("/// <summary>The Arazzo documents given with the spec (MatchmarkArazzo), as JSON.</summary>");
            w.Line($"public static readonly string[] Workflows = new[] {{ {string.Join(", ", workflows.Select(json => $"@\"{json.Replace("\"", "\"\"")}\""))} }};");
            w.Line();
            w.Line("/// <summary>The suite, generated once from <see cref=\"Spec\"/> and its <see cref=\"Workflows\"/>.</summary>");
            w.Line("public static global::Matchmark.Testing.RegressionSuite Suite { get; } = global::Matchmark.Testing.RegressionSuite.FromJson(Spec, Workflows);");
            w.Close();
            return w.ToString();
        }
    }
}
