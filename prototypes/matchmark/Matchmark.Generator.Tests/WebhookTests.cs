using System.Reflection;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Matchmark.Testing;
using Microsoft.AspNetCore.Builder;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// Webhooks: the Orders spec's <c>orderShipped</c> event, generated as an API of its own. A consumer implements the
/// generated receiver interface and maps it; the webhook suite holds that receiver to the spec; the generated sender
/// delivers an event to it.
/// </summary>
[TestFixture]
public sealed class WebhookTests
{
    private static readonly string Samples = Path.GetFullPath(Path.Combine(ThisDirectory(), "..", "samples", "orders"));

    private const string Receiver = """
        using System;
        using System.Collections.Concurrent;
        using System.Net.Http;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Hosting;
        using Microsoft.AspNetCore.Routing;
        using Microsoft.Extensions.DependencyInjection;

        namespace Orders.Api;

        public sealed class Receiver : IOrdersWebhooksHandlers
        {
            public static readonly ConcurrentQueue<OrderEvent> Received = new();

            public Task<Resultful.VoidResult<OrderShippedError>> OrderShipped(OrderEvent body, CancellationToken cancellationToken)
            {
                Received.Enqueue(body);
                return Task.FromResult(Resultful.Result.Ok<OrderShippedError>());
            }
        }

        public static class Consumer
        {
            public static async Task<WebApplication> StartAsync()
            {
                var builder = WebApplication.CreateBuilder();
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Services.AddOrdersWebhooks();
                builder.Services.AddProblemDetails();
                builder.Services.AddSingleton<IOrdersWebhooksHandlers, Receiver>();
                var app = builder.Build();
                app.UseOrdersWebhooksProblems();
                app.MapGroup("/hooks").MapOrdersWebhooks();
                await app.StartAsync();
                return app;
            }

            /// <summary>What the API's side does: sends an event through the generated sender.</summary>
            public static async Task<string> SendAsync(string receiver)
            {
                using var http = new HttpClient { BaseAddress = new Uri(receiver) };
                var id = Guid.NewGuid();
                await new OrdersWebhooksClient(http).OrderShipped(new OrderEvent { OrderId = id, Status = OrderStatus.Shipped, At = DateTimeOffset.UtcNow }, default);
                return id.ToString();
            }
        }
        """;

    [Test]
    public async Task A_generated_receiver_passes_the_webhook_suite_and_receives_what_the_sender_sends()
    {
        var (assembly, diagnostics) = GeneratedServer.Build("Orders", Path.Combine(Samples, "openapi.yaml"), [], Receiver, withRules: false);
        diagnostics.Where(d => d.Severity >= Microsoft.CodeAnalysis.DiagnosticSeverity.Warning).Should().BeEmpty("webhooks are generated, not skipped, and this one documents its 400");
        var consumer = assembly.GetType("Orders.Api.Consumer")!;
        var app = await (Task<WebApplication>)consumer.GetMethod("StartAsync")!.Invoke(null, null)!;
        try
        {
            var receiver = app.Urls.Single() + "/hooks/";
            var suite = RegressionSuite.ForWebhooks(SpecJson.Normalize(File.ReadAllText(Path.Combine(Samples, "openapi.yaml")), "openapi.yaml"))!;
            using var client = new HttpClient { BaseAddress = new Uri(receiver) };

            var report = await suite.RunAsync(client, new RegressionOptions());

            report.Failures.Should().BeEmpty(report.ToString());
            report.Cases.Select(c => c.Case.Operation.Id).Distinct().Should().Equal("orderShipped");
            report.Cases.Should().Contain(c => c.Case.Kind == CaseKind.Invalid && c.Exchange.Status == 400);

            var sent = await (Task<string>)consumer.GetMethod("SendAsync")!.Invoke(null, [receiver])!;
            var received = (System.Collections.IEnumerable)assembly.GetType("Orders.Api.Receiver")!.GetField("Received")!.GetValue(null)!;
            received.Cast<object>().Select(e => e.GetType().GetProperty("OrderId")!.GetValue(e)!.ToString()).Should().Contain(sent);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Test]
    public void A_spec_without_webhooks_has_no_webhook_suite()
        => RegressionSuite.ForWebhooks("""{"openapi":"3.1.0","info":{"title":"t","version":"1"},"paths":{}}""").Should().BeNull();

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
