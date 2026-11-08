using System.Reflection;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Matchmark.Testing;
using Microsoft.AspNetCore.Builder;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// OpenAPI 3.2 end to end, with the Catalogue sample: a search sent as a QUERY, and an export streamed as JSON Lines.
/// The generated server, with a small in-memory handler, held to the suite from the same spec; the generated client
/// reading the stream; and a stream with one bad line caught at that line.
/// </summary>
[TestFixture]
public sealed class CatalogueTests
{
    private static readonly string Samples = Path.GetFullPath(Path.Combine(ThisDirectory(), "..", "samples", "catalogue"));

    private const string Handlers = """
        using System;
        using System.Collections.Concurrent;
        using System.Collections.Generic;
        using System.Linq;
        using System.Net.Http;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Hosting;
        using Microsoft.AspNetCore.Mvc;
        using Microsoft.Extensions.DependencyInjection;
        using Resultful;

        namespace Catalogue.Api;

        public sealed class Handlers : ICatalogueHandlers
        {
            public static bool Fault;
            private readonly ConcurrentDictionary<long, Product> _products = new();
            private long _next;

            private IEnumerable<Product> All => _products.Values.OrderBy(p => p.Id);

            public Task<Result<IReadOnlyList<Product>, ListProductsError>> ListProducts(int? limit, CancellationToken cancellationToken)
                => Task.FromResult(Result.Ok<IReadOnlyList<Product>, ListProductsError>(All.Take(limit ?? 100).ToList()));

            public Task<Result<IReadOnlyList<Product>, SearchProductsError>> SearchProducts(ProductSearch body, CancellationToken cancellationToken)
                => Task.FromResult(Result.Ok<IReadOnlyList<Product>, SearchProductsError>(All
                    .Where(p => p.Name.Contains(body.Text, StringComparison.OrdinalIgnoreCase) && (body.MaxPrice is null || p.Price <= body.MaxPrice)
                                && (body.Category is null || p.Category == body.Category))
                    .ToList()));

            public Task<Result<Product, CreateProductError>> CreateProduct(NewProduct body, CancellationToken cancellationToken)
            {
                var product = new Product { Id = Interlocked.Increment(ref _next), Name = body.Name, Price = body.Price, Category = body.Category };
                _products[product.Id] = product;
                return Task.FromResult(Result.Ok<Product, CreateProductError>(product));
            }

            public Task<Result<Product, GetProductError>> GetProduct(long productId, CancellationToken cancellationToken)
                => Task.FromResult(_products.TryGetValue(productId, out var product)
                    ? Result.Ok<Product, GetProductError>(product)
                    : Result.Fail<Product, GetProductError>(GetProductError.NotFound(new ProblemDetails { Title = "No such product" })));

            public Task<IAsyncEnumerable<Product>> ExportProducts(CancellationToken cancellationToken)
                => Task.FromResult(Export());

            private async IAsyncEnumerable<Product> Export()
            {
                foreach (var product in All)
                {
                    await Task.Yield();
                    yield return product;
                }

                if (Fault)
                {
                    yield return new Product { Id = 999, Name = "", Price = -1, Category = Category.Tea };
                }
            }
        }

        public static class Server
        {
            public static async Task<WebApplication> StartAsync()
            {
                var builder = WebApplication.CreateBuilder();
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Services.AddCatalogue();
                builder.Services.AddProblemDetails();
                builder.Services.AddSingleton<ICatalogueHandlers, Handlers>();
                var app = builder.Build();
                app.MapCatalogue();
                await app.StartAsync();
                return app;
            }

            /// <summary>The generated client: creates two products, then reads the export.</summary>
            public static async Task<int> ExportedAsync(string url)
            {
                using var http = new HttpClient { BaseAddress = new Uri(url) };
                var client = new CatalogueClient(http);
                await client.CreateProduct(new NewProduct { Name = "Gyokuro", Price = 30m, Category = Category.Tea }, default);
                await client.CreateProduct(new NewProduct { Name = "Mug", Price = 8m, Category = Category.Cups }, default);
                var count = 0;
                await foreach (var _ in await client.ExportProducts(default))
                {
                    count++;
                }

                return count;
            }
        }
        """;

    private static Assembly? _assembly;

    private static Assembly Compiled()
    {
        if (_assembly is null)
        {
            var (assembly, diagnostics) = GeneratedServer.Build("Catalogue", Path.Combine(Samples, "openapi.yaml"), [Path.Combine(Samples, "catalogue.overlay.yaml")], Handlers, withRules: true);
            diagnostics.Where(d => d.Severity >= Microsoft.CodeAnalysis.DiagnosticSeverity.Warning).Should().BeEmpty("QUERY and JSON Lines are generated");
            _assembly = assembly;
        }

        return _assembly;
    }

    private static RegressionSuite Suite() => RegressionSuite.FromJson(SpecJson.Normalize(
        File.ReadAllText(Path.Combine(Samples, "openapi.yaml")), "openapi.yaml", out _,
        [(File.ReadAllText(Path.Combine(Samples, "catalogue.overlay.yaml")), "catalogue.overlay.yaml")]));

    [Test]
    public async Task The_generated_server_answers_QUERY_and_streams_JSON_Lines_as_the_spec_says()
    {
        var (report, exported) = await Run(fault: false);

        report.Failures.Should().BeEmpty(report.ToString());
        report.Cases.Should().Contain(c => c.Case.Request.Method == "QUERY" && c.Exchange.Status == 200)
            .And.Contain(c => c.Case.Request.Method == "QUERY" && c.Case.Kind == CaseKind.Invalid && c.Exchange.Status == 400);
        report.Workflows.Select(w => w.Name).Should().Contain("prefix listProducts by limit");
        exported.Should().BeGreaterThanOrEqualTo(2, "the client reads every line of the export");
    }

    [Test]
    public async Task A_bad_line_in_the_stream_is_caught_at_that_line()
    {
        var (report, _) = await Run(fault: true);

        report.Failures.Where(f => f.Check == Checks.BodyConforms).Select(f => f.Message)
            .Should().Contain(m => m.Contains("of the application/jsonl stream breaks its itemSchema", StringComparison.Ordinal));
    }

    private static async Task<(RegressionReport Report, int Exported)> Run(bool fault)
    {
        var assembly = Compiled();
        assembly.GetType("Catalogue.Api.Handlers")!.GetField("Fault")!.SetValue(null, fault);
        var server = assembly.GetType("Catalogue.Api.Server")!;
        var app = await (Task<WebApplication>)server.GetMethod("StartAsync")!.Invoke(null, null)!;
        try
        {
            var url = app.Urls.Single() + "/";
            using var client = new HttpClient { BaseAddress = new Uri(url) };
            var report = await Suite().RunAsync(client, new RegressionOptions());
            File.WriteAllText(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"catalogue-{(fault ? "fault" : "right")}.txt"), report.ToString());
            var exported = fault ? 0 : await (Task<int>)server.GetMethod("ExportedAsync")!.Invoke(null, [url])!;
            return (report, exported);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
