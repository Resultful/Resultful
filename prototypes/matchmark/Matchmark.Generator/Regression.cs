using System;
using System.Collections.Generic;
using System.IO;
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
        private string EmitRegression()
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
            w.Line("/// <summary>The suite, generated once from <see cref=\"Spec\"/>.</summary>");
            w.Line("public static global::Matchmark.Testing.RegressionSuite Suite { get; } = global::Matchmark.Testing.RegressionSuite.FromJson(Spec);");
            w.Close();
            return w.ToString();
        }
    }
}
