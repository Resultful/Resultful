using System.Reflection;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Matchmark.Testing;
using Microsoft.AspNetCore.Builder;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// Form bodies, as Stripe takes them, with the Payments sample: nested values in brackets, numbers and true or false as
/// text. The generated server reads them and the suite sends them; the generated client writes them.
/// </summary>
[TestFixture]
public sealed class FormTests
{
    private static readonly string Samples = Path.GetFullPath(Path.Combine(ThisDirectory(), "..", "samples", "payments"));

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

        namespace Payments.Api;

        public sealed class Handlers : IPaymentsHandlers
        {
            private readonly ConcurrentDictionary<string, Charge> _charges = new();
            private long _next;

            public Task<Result<Charge, CreateChargeError>> CreateCharge(NewCharge body, CancellationToken cancellationToken)
            {
                // Stripe's test card that is always declined: the spec's default response, with the status Stripe gives it.
                if (body.Card.Number == "4000000000000002")
                {
                    var declined = new Error { ErrorValue = new ApiError { Type = "card_error", Message = "Your card was declined.", Code = "card_declined" } };
                    return Task.FromResult(Result.Fail<Charge, CreateChargeError>(CreateChargeError.Default(402, declined)));
                }

                var charge = new Charge
                {
                    Id = $"ch_{Interlocked.Increment(ref _next)}", Amount = body.Amount, Currency = body.Currency, Captured = body.Capture ?? true,
                    Description = body.Description, Last4 = body.Card.Number[^4..], Metadata = body.Metadata,
                };
                _charges[charge.Id] = charge;
                return Task.FromResult(Result.Ok<Charge, CreateChargeError>(charge));
            }

            public Task<Result<Charge, GetChargeError>> GetCharge(string chargeId, CancellationToken cancellationToken)
                => Task.FromResult(_charges.TryGetValue(chargeId, out var charge)
                    ? Result.Ok<Charge, GetChargeError>(charge)
                    : Result.Fail<Charge, GetChargeError>(GetChargeError.NotFound(new ProblemDetails { Title = "No such charge" })));
        }

        public static class Server
        {
            public static async Task<WebApplication> StartAsync()
            {
                var builder = WebApplication.CreateBuilder();
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Services.AddPayments();
                builder.Services.AddProblemDetails();
                builder.Services.AddSingleton<IPaymentsHandlers, Handlers>();
                var app = builder.Build();
                app.MapPayments();
                await app.StartAsync();
                return app;
            }

            /// <summary>The generated client sends a charge as a form, nested values and all, and reads it back.</summary>
            public static async Task<string> ChargeAsync(string url)
            {
                using var http = new HttpClient { BaseAddress = new Uri(url) };
                var client = new PaymentsClient(http);
                var created = await client.CreateCharge(new NewCharge
                {
                    Amount = 1250, Currency = Currency.Gbp, Capture = false, Description = "Tea",
                    Card = new Card { Number = "4000056655665556", ExpMonth = 4, ExpYear = 2030 },
                    Metadata = new Dictionary<string, string> { ["order"] = "o-17" },
                    Expand = new[] { "customer" },
                }, default);
                var charge = (Charge)created.ToOneOf().Value;
                var read = (Charge)(await client.GetCharge(charge.Id, default)).ToOneOf().Value;
                return $"{read.Amount} {read.Currency} {read.Captured} {read.Last4} {read.Metadata!["order"]}";
            }

            /// <summary>A declined card: the client reads the default response, with its status.</summary>
            public static async Task<string> DeclineAsync(string url)
            {
                using var http = new HttpClient { BaseAddress = new Uri(url) };
                var result = await new PaymentsClient(http).CreateCharge(new NewCharge
                {
                    Amount = 500, Currency = Currency.Usd, Card = new Card { Number = "4000000000000002", ExpMonth = 1, ExpYear = 2031 },
                }, default);
                var declined = (CreateChargeDefault)((CreateChargeError)result.ToOneOf().Value).Value;
                return $"{declined.Status} {declined.Body.ErrorValue.Code}";
            }
        }
        """;

    [Test]
    public async Task The_generated_server_reads_forms_the_suite_sends_and_the_client_writes()
    {
        var (assembly, diagnostics) = GeneratedServer.Build("Payments", Path.Combine(Samples, "openapi.yaml"), [], Handlers, withRules: false);
        diagnostics.Where(d => d.Severity >= Microsoft.CodeAnalysis.DiagnosticSeverity.Warning).Should().BeEmpty("form bodies are generated");
        var server = assembly.GetType("Payments.Api.Server")!;
        var app = await (Task<WebApplication>)server.GetMethod("StartAsync")!.Invoke(null, null)!;
        try
        {
            var url = app.Urls.Single() + "/";
            using var client = new HttpClient { BaseAddress = new Uri(url) };
            var suite = RegressionSuite.FromJson(SpecJson.Normalize(File.ReadAllText(Path.Combine(Samples, "openapi.yaml")), "openapi.yaml"));

            var report = await suite.RunAsync(client, new RegressionOptions { Random = 10 });

            File.WriteAllText(Path.Combine(TestContext.CurrentContext.WorkDirectory, "payments.txt"), report.ToString());
            report.Failures.Should().BeEmpty(report.ToString());
            report.Cases.Should().Contain(c => c.Case.Request.ContentType == "application/x-www-form-urlencoded" && c.Exchange.Status == 200)
                .And.Contain(c => c.Case.Kind == CaseKind.Invalid && c.Case.Request.Body!.Contains("card%5Bexp_month%5D=13", StringComparison.Ordinal) && c.Exchange.Status == 400);
            (await (Task<string>)server.GetMethod("ChargeAsync")!.Invoke(null, [url])!).Should().Be("1250 Gbp False 5556 o-17");
            (await (Task<string>)server.GetMethod("DeclineAsync")!.Invoke(null, [url])!).Should().Be("402 card_declined");
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
