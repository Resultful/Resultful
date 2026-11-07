using System.Collections.Immutable;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;

namespace Resultful.OpenApi.Generator.Tests;

/// <summary>
/// The Verify part: the build checks hand-written endpoints (controller actions, Minimal API Map calls) against the spec.
/// ASP.NET Core's attributes and Map methods are stood in for by same-named types, which is all the verifier reads.
/// </summary>
[TestFixture]
public sealed class CoverageTests
{
    private const string Spec = """
        openapi: 3.1.0
        info: { title: T, version: '1' }
        paths:
          /things/{thingId}:
            get:
              operationId: getThing
              parameters: [{ name: thingId, in: path, required: true, schema: { type: integer } }]
              responses:
                '200': { description: ok, content: { application/json: { schema: { type: string } } } }
                '404': { description: missing }
            delete:
              operationId: deleteThing
              parameters: [{ name: thingId, in: path, required: true, schema: { type: integer } }]
              responses:
                '204': { description: gone }
        """;

    private const string AspNet = """
        namespace Microsoft.AspNetCore.Mvc
        {
            using System;
            public sealed class ApiControllerAttribute : Attribute { }
            public sealed class RouteAttribute(string template) : Attribute { }
            public sealed class HttpGetAttribute(string template = "") : Attribute { public string? Name { get; set; } }
            public sealed class HttpDeleteAttribute(string template = "") : Attribute { public string? Name { get; set; } }
            [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
            public sealed class ProducesResponseTypeAttribute(int statusCode) : Attribute { }
            [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
            public sealed class ProducesResponseTypeAttribute<T>(int statusCode, string contentType) : Attribute { }
            public sealed class NonActionAttribute : Attribute { }
            public sealed class ApiExplorerSettingsAttribute : Attribute { public bool IgnoreApi { get; set; } }
            public class ControllerBase { }
        }

        namespace Microsoft.AspNetCore.Builder
        {
            using System;
            public sealed class App { }
            public sealed class Endpoint { }
            public static class EndpointRouteBuilderExtensions
            {
                public static Endpoint MapGet(this App app, string pattern, Delegate handler) => new();
                public static Endpoint MapDelete(this App app, string pattern, Delegate handler) => new();
                public static Endpoint MapMethods(this App app, string pattern, string[] methods, Delegate handler) => new();
                public static App MapGroup(this App app, string prefix) => app;
                public static Endpoint WithName(this Endpoint endpoint, string name) => endpoint;
                public static Endpoint ExcludeFromDescription(this Endpoint endpoint) => endpoint;
                public static App ExcludeFromDescription(this App app) => app;
            }
        }
        """;

    [Test]
    public void Controllers_that_cover_the_spec_exactly_pass()
    {
        Verify("""
            using Microsoft.AspNetCore.Mvc;

            [ApiController]
            [Route("things")]
            public sealed class ThingsController : ControllerBase
            {
                [HttpGet("{thingId:long}", Name = "getThing")]
                [ProducesResponseType<string>(200, "application/json")]
                [ProducesResponseType(404)]
                public object Get(long thingId) => thingId;

                [HttpDelete("/things/{thingId}", Name = "deleteThing")]
                [ProducesResponseType(204)]
                public void Delete(long thingId) { }

                [HttpGet("/health")]
                [ApiExplorerSettings(IgnoreApi = true)]
                public string Health() => "ok";

                [NonAction]
                public void Helper() { }
            }
            """).Should().BeEmpty();
    }

    [Test]
    public void Every_kind_of_drift_is_reported_where_it_is()
    {
        var diagnostics = Verify("""
            using Microsoft.AspNetCore.Mvc;

            [ApiController]
            public sealed class ThingsController : ControllerBase
            {
                [HttpGet("/[controller]/{id}", Name = "fetchThing")]
                [ProducesResponseType(200)]
                [ProducesResponseType(410)]
                public object Get(long id) => id;

                [HttpGet("/health")]
                public string Health() => "ok";
            }
            """);

        diagnostics.Should().Equal(
            "RAPI010: The spec's operation 'deleteThing' (DELETE /things/{thingId}) has no endpoint: add a controller action or Map call for it",
            "RAPI011: ThingsController.Health (GET /health) is not in the spec: document it, or leave it out of the OpenAPI description ([ApiExplorerSettings(IgnoreApi = true)] or ExcludeFromDescription())",
            "RAPI012: ThingsController.Get (GET /things/{thingId}): the spec documents a 404 response, but the action declares no [ProducesResponseType] for it",
            "RAPI013: ThingsController.Get (GET /things/{thingId}): the action declares a 410 response, which the spec does not document",
            "RAPI014: ThingsController.Get (GET /things/{thingId}) is named 'fetchThing'; name it 'getThing', the spec's operationId, which the OpenAPI description uses",
            "RAPI015: ThingsController.Get (GET /things/{thingId}): the route parameter is '{id}', the spec calls it '{thingId}'");
    }

    [Test]
    public void Minimal_API_endpoints_are_checked_too_including_group_prefixes()
    {
        Verify("""
            using Microsoft.AspNetCore.Builder;

            public static class Api
            {
                public static void Map(App app)
                {
                    var things = app.MapGroup("/things");
                    things.MapGet("/{thingId}", () => "x").WithName("getThing");
                    app.MapMethods("/things/{thingId}", new[] { "DELETE" }, () => { }).WithName("deleteThing");
                    app.MapGet("/health", () => "ok").ExcludeFromDescription();
                    var admin = app.MapGroup("/admin").ExcludeFromDescription();
                    admin.MapGet("/stats", () => 1);
                }
            }
            """).Should().BeEmpty();

        Verify("""
            using Microsoft.AspNetCore.Builder;

            public static class Api
            {
                public static void Map(App app)
                {
                    app.MapGet("/things/{thingId}", () => "x");
                    app.MapGet("/version", () => "1");
                }
            }
            """).Should().Equal(
                "RAPI010: The spec's operation 'deleteThing' (DELETE /things/{thingId}) has no endpoint: add a controller action or Map call for it",
                "RAPI011: MapGet(\"/version\") (GET /version) is not in the spec: document it, or leave it out of the OpenAPI description ([ApiExplorerSettings(IgnoreApi = true)] or ExcludeFromDescription())",
                "RAPI014: MapGet(\"/things/{thingId}\") (GET /things/{thingId}) is named 'none'; name it 'getThing', the spec's operationId, which the OpenAPI description uses");
    }

    [Test]
    public void Nothing_is_verified_unless_the_spec_asks_for_it()
    {
        Verify("public sealed class Empty { }", parts: "Contract").Should().NotContain(d => d.StartsWith("RAPI01"));
    }

    private static List<string> Verify(string code, string parts = "Verify")
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("Test",
            [CSharpSyntaxTree.ParseText(AspNet), CSharpSyntaxTree.ParseText(code)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty("the test code should compile");

        var driver = CSharpGeneratorDriver.Create(
            [new OpenApiSourceGenerator().AsSourceGenerator()],
            [new Text("/specs/t.yaml", Spec)],
            optionsProvider: new Options(parts));
        return driver.RunGenerators(compilation).GetRunResult().Diagnostics
            .Where(d => d.Id.StartsWith("RAPI01", StringComparison.Ordinal))
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private sealed class Text(string path, string text) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }

    private sealed class Options(string parts) : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _file = new Values(new Dictionary<string, string>
        {
            ["build_metadata.AdditionalFiles.ResultfulOpenApi"] = "true",
            ["build_metadata.AdditionalFiles.ResultfulApiName"] = "T",
            ["build_metadata.AdditionalFiles.ResultfulParts"] = parts,
        });

        public override AnalyzerConfigOptions GlobalOptions { get; } = new Values([]);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _file;

        private sealed class Values(Dictionary<string, string> values) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
        }
    }
}
