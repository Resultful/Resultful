using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Matchmark.Testing;
using Microsoft.AspNetCore.Builder;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// AsyncAPI 3, generated: the Notifications sample's events read as webhooks (<see cref="AsyncApi"/>), so each message's
/// payload is a typed model, a consumer implements the generated receiver behind its bridge, the suite holds it to the
/// document, and the generated sender publishes to it.
/// </summary>
[TestFixture]
public sealed class AsyncApiGenerationTests
{
    private static readonly string Spec = Path.GetFullPath(Path.Combine(ThisDirectory(), "..", "samples", "notifications", "asyncapi.yaml"));

    private const string Consumer = """
        using System;
        using System.Collections.Concurrent;
        using System.Net.Http;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Hosting;
        using Microsoft.Extensions.DependencyInjection;

        namespace Events.Api;

        /// <summary>The consumer, behind its bridge: what it takes is typed, and already checked against the document.</summary>
        public sealed class Consumer : IEventsWebhooksHandlers
        {
            public static readonly ConcurrentQueue<(Guid Correlation, Redelivery Message)> Redeliveries = new();
            public static readonly ConcurrentQueue<Notification> Notifications = new();

            public Task<Resultful.VoidResult<PublishNotificationError>> PublishNotification(Guid xCorrelationId, Notification body, CancellationToken cancellationToken)
            {
                Notifications.Enqueue(body);
                return Task.FromResult(Resultful.Result.Ok<PublishNotificationError>());
            }

            public Task<Resultful.VoidResult<ReceiveRedeliveryError>> ReceiveRedelivery(Guid xCorrelationId, Redelivery body, CancellationToken cancellationToken)
            {
                Redeliveries.Enqueue((xCorrelationId, body));
                return Task.FromResult(Resultful.Result.Ok<ReceiveRedeliveryError>());
            }
        }

        public static class Bridge
        {
            public static async Task<WebApplication> StartAsync()
            {
                var builder = WebApplication.CreateBuilder();
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Services.AddEventsWebhooks();
                builder.Services.AddProblemDetails();
                builder.Services.AddSingleton<IEventsWebhooksHandlers, Consumer>();
                var app = builder.Build();
                app.UseEventsWebhooksProblems();
                app.MapGroup("/bridge").MapEventsWebhooks();
                await app.StartAsync();
                return app;
            }

            /// <summary>The producer's side: a typed message, published through the generated sender.</summary>
            public static async Task<string> PublishAsync(string bridge)
            {
                using var http = new HttpClient { BaseAddress = new Uri(bridge) };
                var id = Guid.NewGuid();
                await new EventsWebhooksClient(http).PublishNotification(Guid.NewGuid(), new Notification { Id = id, Topic = "orders", SentAt = DateTimeOffset.UtcNow, Summary = "Order 7 shipped" }, default);
                return id.ToString();
            }
        }
        """;

    [Test]
    public async Task Each_message_is_typed_and_a_generated_consumer_passes_the_suite_and_takes_what_the_sender_publishes()
    {
        var (assembly, diagnostics) = GeneratedServer.Build("Events", Spec, [], Consumer, withRules: false);
        diagnostics.Where(d => d.Severity >= Microsoft.CodeAnalysis.DiagnosticSeverity.Warning).Should().BeEmpty("every operation's message is generated");
        assembly.GetType("Events.Api.Redelivery")!.GetProperty("Attempts")!.PropertyType.Should().Be<int>();
        assembly.GetType("Events.Api.Notification")!.GetProperty("SentAt")!.PropertyType.Should().Be<DateTimeOffset>();

        var bridgeType = assembly.GetType("Events.Api.Bridge")!;
        var app = await (Task<WebApplication>)bridgeType.GetMethod("StartAsync")!.Invoke(null, null)!;
        try
        {
            var bridge = app.Urls.Single() + "/bridge/";
            var suite = RegressionSuite.ForWebhooks(SpecJson.Normalize(File.ReadAllText(Spec), Spec))!;
            using var client = new HttpClient { BaseAddress = new Uri(bridge) };

            var report = await suite.RunAsync(client, new RegressionOptions());

            report.Failures.Should().BeEmpty(report.ToString());
            report.Cases.Select(c => c.Case.Operation.Id).Distinct().Should().BeEquivalentTo("publishNotification", "receiveRedelivery");
            report.Cases.Should().Contain(c => c.Case.Kind == CaseKind.Invalid && c.Case.Name.Contains("x-correlation-id", StringComparison.Ordinal) && c.Exchange.Status == 400, report.ToString());
            report.Cases.Should().Contain(c => c.Case.Kind == CaseKind.Invalid && c.Case.Name.Contains("attempts", StringComparison.Ordinal) && c.Exchange.Status == 400, report.ToString());

            var sent = await (Task<string>)bridgeType.GetMethod("PublishAsync")!.Invoke(null, [bridge])!;
            var received = (System.Collections.IEnumerable)assembly.GetType("Events.Api.Consumer")!.GetField("Notifications")!.GetValue(null)!;
            received.Cast<object>().Select(n => n.GetType().GetProperty("Id")!.GetValue(n)!.ToString()).Should().Contain(sent);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Test]
    public void The_document_reads_as_webhooks_named_by_operation()
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(AsyncApi.ToOpenApi(File.ReadAllText(Spec), Spec))!;
        json["webhooks"]!.AsObject().Select(w => w.Key).Should().Equal("publishNotification", "receiveRedelivery");
        json["webhooks"]!["receiveRedelivery"]!["post"]!["x-asyncapi-channel"]!.GetValue<string>().Should().Be("notifications.redeliver");
        AsyncApi.IsAsyncApi("openapi: 3.1.0").Should().BeFalse();
    }

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
