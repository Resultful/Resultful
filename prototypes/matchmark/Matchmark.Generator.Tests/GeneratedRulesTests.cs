using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using AwesomeAssertions;
using Matchmark.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// The invariants an overlay declares, enforced by the generated server and checked by the engine: the Bookings sample's
/// generated server, compiled here with a small in-memory handler, run on a real port, and held to the suite generated
/// from the same spec and overlay. Without Matchmark.Rules the generator says the rules aren't enforced (MM025), and the
/// suite finds every request that breaks one accepted.
/// </summary>
[TestFixture]
public sealed class GeneratedRulesTests
{
    private static readonly string Samples = Path.GetFullPath(Path.Combine(ThisDirectory(), "..", "samples", "bookings"));

    private const string Handlers = """
        using System;
        using System.Collections.Concurrent;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Hosting;
        using Microsoft.AspNetCore.Mvc;
        using Microsoft.Extensions.DependencyInjection;
        using Resultful;

        namespace Bookings.Api;

        public sealed class Handlers : IBookingsHandlers
        {
            private readonly ConcurrentDictionary<long, Booking> _store = new();
            private long _next;

            public Task<Result<IReadOnlyList<Booking>, ListBookingsError>> ListBookings(int? roomId, int? deskId, int? minMinutes, int? maxMinutes, CancellationToken cancellationToken)
            {
                IReadOnlyList<Booking> found = _store.Values
                    .Where(b => (roomId is null || b.RoomId == roomId) && (deskId is null || b.DeskId == deskId)
                                && (minMinutes is null || (b.EndsAt - b.StartsAt).TotalMinutes >= minMinutes)
                                && (maxMinutes is null || (b.EndsAt - b.StartsAt).TotalMinutes <= maxMinutes))
                    .OrderBy(b => b.Id).ToList();
                return Task.FromResult(Result.Ok<IReadOnlyList<Booking>, ListBookingsError>(found));
            }

            public Task<Result<Booking, CreateBookingError>> CreateBooking(NewBooking body, CancellationToken cancellationToken)
            {
                var id = Interlocked.Increment(ref _next);
                var booking = new Booking { Id = id, StartsAt = body.StartsAt, EndsAt = body.EndsAt, RoomId = body.RoomId, DeskId = body.DeskId, Note = body.Note, Status = "booked" };
                _store[id] = booking;
                return Task.FromResult(Result.Ok<Booking, CreateBookingError>(booking));
            }

            public Task<Result<Booking, GetBookingError>> GetBooking(long bookingId, CancellationToken cancellationToken)
                => Task.FromResult(_store.TryGetValue(bookingId, out var booking)
                    ? Result.Ok<Booking, GetBookingError>(booking)
                    : Result.Fail<Booking, GetBookingError>(GetBookingError.NotFound(new ProblemDetails { Title = "No such booking" })));

            public Task<VoidResult<CancelBookingError>> CancelBooking(long bookingId, CancellationToken cancellationToken)
                => Task.FromResult(_store.TryRemove(bookingId, out _)
                    ? Result.Ok<CancelBookingError>()
                    : Result.Fail<CancelBookingError>(CancelBookingError.NotFound(new ProblemDetails { Title = "No such booking" })));
        }

        public static class Server
        {
            public static async Task<WebApplication> StartAsync()
            {
                var builder = WebApplication.CreateBuilder();
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Services.AddBookings();
                builder.Services.AddProblemDetails();
                builder.Services.AddSingleton<IBookingsHandlers, Handlers>();
                var app = builder.Build();
                app.MapBookings();
                await app.StartAsync();
                return app;
            }
        }
        """;

    [Test]
    public async Task The_generated_server_enforces_what_the_overlay_declares_and_the_suite_agrees()
    {
        var (assembly, diagnostics) = Build(withRules: true);
        diagnostics.Should().BeEmpty();

        var report = await Run(assembly);

        report.Failures.Should().BeEmpty(report.ToString());
        report.Cases.Where(c => c.Case.Kind == CaseKind.Invalid).Select(c => c.Case.Name).Should().Contain([
            "listBookings: breaks the dependency ZeroOrOne(roomId, deskId);",
            "listBookings: breaks the dependency minMinutes <= maxMinutes;",
        ]).And.Contain(n => n.Contains("breaking the rule !has(self.roomId) || !has(self.deskId)", StringComparison.Ordinal));
    }

    [Test]
    public async Task Without_Matchmark_Rules_the_rules_are_not_enforced_and_the_suite_says_so()
    {
        var (assembly, diagnostics) = Build(withRules: false);
        diagnostics.Select(d => d.Id).Should().Equal("MM025");

        var report = await Run(assembly);

        report.Failures.Where(f => f.Check == Checks.InvalidRejected).Select(f => f.Subject).Should().Contain([
            "listBookings: breaks the dependency ZeroOrOne(roomId, deskId);",
            "listBookings: breaks the dependency minMinutes <= maxMinutes;",
        ]).And.Contain(s => s.Contains("breaking the rule", StringComparison.Ordinal));
    }

    private static RegressionSuite Suite() => RegressionSuite.FromJson(SpecJson.Normalize(
        File.ReadAllText(Path.Combine(Samples, "openapi.yaml")), "openapi.yaml", out _,
        [(File.ReadAllText(Path.Combine(Samples, "bookings.overlay.yaml")), "bookings.overlay.yaml")]));

    private static async Task<RegressionReport> Run(Assembly assembly)
    {
        var start = assembly.GetType("Bookings.Api.Server")!.GetMethod("StartAsync")!;
        var app = await (Task<WebApplication>)start.Invoke(null, null)!;
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single() + "/") };
            var report = await Suite().RunAsync(client, new RegressionOptions());
            File.WriteAllText(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"bookings-{TestContext.CurrentContext.Test.MethodName}.txt"), report.ToString());
            return report;
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    /// <summary>The generated Contract and Server parts for Bookings and its overlay, with the handler above, compiled and loaded.</summary>
    private static (Assembly Assembly, IReadOnlyList<Diagnostic> Diagnostics) Build(bool withRules)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Append(typeof(Resultful.Unit).Assembly.Location)
            .Append(typeof(OneOf.OneOf<>).Assembly.Location)
            .Concat(withRules ? [typeof(Matchmark.Rules.Cel).Assembly.Location] : [])
            .Where(path => withRules || !path.EndsWith("Matchmark.Rules.dll", StringComparison.Ordinal))
            .Distinct()
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));
        var name = $"Bookings{(withRules ? "WithRules" : "WithoutRules")}{Guid.NewGuid():N}";
        var compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(Handlers, new CSharpParseOptions(LanguageVersion.Latest))], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var texts = new AdditionalText[]
        {
            new Text(Path.Combine(Samples, "openapi.yaml"), File.ReadAllText(Path.Combine(Samples, "openapi.yaml"))),
            new Text(Path.Combine(Samples, "bookings.overlay.yaml"), File.ReadAllText(Path.Combine(Samples, "bookings.overlay.yaml"))),
        };
        var driver = CSharpGeneratorDriver.Create([new OpenApiSourceGenerator().AsSourceGenerator()], texts, optionsProvider: new Options(),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest));

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var generated, out var generatorDiagnostics);

        using var stream = new MemoryStream();
        var emitted = generated.Emit(stream);
        emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()).Should().BeEmpty();
        stream.Position = 0;
        return (AssemblyLoadContext.Default.LoadFromStream(stream), generatorDiagnostics.ToList());
    }

    private sealed class Text(string path, string text) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }

    private sealed class Options : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _spec = new Values(new Dictionary<string, string>
        {
            ["build_metadata.AdditionalFiles.Matchmark"] = "true",
            ["build_metadata.AdditionalFiles.MatchmarkApiName"] = "Bookings",
            ["build_metadata.AdditionalFiles.MatchmarkNamespace"] = "Bookings.Api",
            ["build_metadata.AdditionalFiles.MatchmarkParts"] = "Contract,Server",
        });

        private readonly AnalyzerConfigOptions _overlay = new Values(new Dictionary<string, string>
        {
            ["build_metadata.AdditionalFiles.MatchmarkOverlay"] = "true",
            ["build_metadata.AdditionalFiles.MatchmarkApiName"] = "Bookings",
        });

        public override AnalyzerConfigOptions GlobalOptions { get; } = new Values([]);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
            => textFile.Path.EndsWith(".overlay.yaml", StringComparison.Ordinal) ? _overlay : _spec;

        private sealed class Values(Dictionary<string, string> values) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
        }
    }

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
