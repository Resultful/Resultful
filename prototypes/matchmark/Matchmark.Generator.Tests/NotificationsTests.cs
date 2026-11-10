using System.Reflection;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Matchmark.Testing;
using Microsoft.AspNetCore.Builder;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// Subscribe, then receive, with the Notifications sample (OpenAPI 3.2): a callback generated as a typed receiver and a
/// sender, signed; and a server-sent event stream whose events' data are typed by their contentSchema. Each side is
/// held to its suite, and a notification goes all the way round.
/// </summary>
[TestFixture]
public sealed class NotificationsTests
{
    private static readonly string Samples = Path.GetFullPath(Path.Combine(ThisDirectory(), "..", "samples", "notifications"));

    private const string Source = """
        using System;
        using System.Collections.Concurrent;
        using System.Collections.Generic;
        using System.Linq;
        using System.Net.Http;
        using System.Security.Cryptography;
        using System.Text;
        using System.Text.Json;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Hosting;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using Microsoft.Extensions.DependencyInjection;
        using Resultful;

        namespace Notifications.Api;

        public static class Signature
        {
            public static string Of(Notification notification)
                => "sha256=" + Convert.ToHexString(HMACSHA256.HashData("secret"u8.ToArray(), Encoding.UTF8.GetBytes(JsonSerializer.Serialize(notification, JsonSerializerOptions.Web)))).ToLowerInvariant();
        }

        public sealed class Handlers : INotificationsHandlers
        {
            private readonly ConcurrentDictionary<long, Subscription> _subscriptions = new();
            private readonly ConcurrentQueue<Notification> _notifications = new();
            private long _next;

            public Task<Result<Subscription, SubscribeError>> Subscribe(NewSubscription body, CancellationToken cancellationToken)
            {
                var subscription = new Subscription { Id = Interlocked.Increment(ref _next), CallbackUrl = body.CallbackUrl, Topics = body.Topics };
                _subscriptions[subscription.Id] = subscription;
                return Task.FromResult(Result.Ok<Subscription, SubscribeError>(subscription));
            }

            public async Task<Result<Notification, PublishError>> Publish(NewNotification body, CancellationToken cancellationToken)
            {
                var notification = new Notification { Id = Interlocked.Increment(ref _next), Topic = body.Topic, Message = body.Message, At = DateTimeOffset.UtcNow };
                _notifications.Enqueue(notification);
                foreach (var subscription in _subscriptions.Values.Where(s => s.Topics.Contains(body.Topic)))
                {
                    try
                    {
                        using var http = new HttpClient { BaseAddress = new Uri(subscription.CallbackUrl), Timeout = TimeSpan.FromSeconds(2) };
                        await new NotificationsCallbacksClient(http).Notify(Signature.Of(notification), notification, cancellationToken);
                    }
                    catch (Exception e) when (e is HttpRequestException or TaskCanceledException or ContractViolationException)
                    {
                        // Delivery is the subscriber's to get right; the notification is published either way.
                    }
                }

                return Result.Ok<Notification, PublishError>(notification);
            }

            public Task<Result<IAsyncEnumerable<Notification>, StreamNotificationsError>> StreamNotifications(Topic? topic, CancellationToken cancellationToken)
                => Task.FromResult(Result.Ok<IAsyncEnumerable<Notification>, StreamNotificationsError>(
                    _notifications.Where(n => topic is null || n.Topic == topic).ToList().ToAsyncEnumerable()));
        }

        public sealed class Receiver : INotificationsCallbacksHandlers
        {
            public static bool Strict;
            public static readonly ConcurrentQueue<(string Signature, Notification Notification)> Received = new();

            public Task<VoidResult<NotifyError>> Notify(string xSignature, Notification body, CancellationToken cancellationToken)
            {
                if (Strict && xSignature != Signature.Of(body))
                {
                    var problem = new Microsoft.AspNetCore.Http.HttpValidationProblemDetails(new Dictionary<string, string[]> { ["X-Signature"] = ["The signature doesn't match the body."] }) { Status = 400 };
                    return Task.FromResult(Result.Fail<NotifyError>(NotifyError.BadRequest(problem)));
                }

                Received.Enqueue((xSignature, body));
                return Task.FromResult(Result.Ok<NotifyError>());
            }
        }

        public static class Hosts
        {
            public static async Task<WebApplication> ApiAsync()
            {
                var builder = WebApplication.CreateBuilder();
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Services.AddNotifications();
                builder.Services.AddProblemDetails();
                builder.Services.AddSingleton<INotificationsHandlers, Handlers>();
                var app = builder.Build();
                app.MapNotifications();
                await app.StartAsync();
                return app;
            }

            public static async Task<WebApplication> ReceiverAsync()
            {
                var builder = WebApplication.CreateBuilder();
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Services.AddNotificationsCallbacks();
                builder.Services.AddProblemDetails();
                builder.Services.AddSingleton<INotificationsCallbacksHandlers, Receiver>();
                var app = builder.Build();
                app.UseNotificationsCallbacksProblems();
                app.MapGroup("/hooks").MapNotificationsCallbacks();
                await app.StartAsync();
                return app;
            }

            /// <summary>Subscribe the receiver, publish, then read the stream: the notification received and streamed.</summary>
            public static async Task<string> RoundTripAsync(string api, string receiver)
            {
                Receiver.Strict = true;
                using var http = new HttpClient { BaseAddress = new Uri(api) };
                var client = new NotificationsClient(http);
                await client.Subscribe(new NewSubscription { CallbackUrl = receiver, Topics = new[] { Topic.Payments } }, default);
                var published = (Notification)(await client.Publish(new NewNotification { Topic = Topic.Payments, Message = "Payment 9 settled" }, default)).ToOneOf().Value;
                var streamed = new List<Notification>();
                await foreach (var notification in (IAsyncEnumerable<Notification>)(await client.StreamNotifications(Topic.Payments, default)).ToOneOf().Value)
                {
                    streamed.Add(notification);
                }

                var received = Receiver.Received.Single(r => r.Notification.Id == published.Id);
                return $"{received.Notification.Message} | signed {received.Signature == Signature.Of(published)} | streamed {streamed.Count(n => n.Id == published.Id)}";
            }
        }
        """;

    [Test]
    public async Task Callbacks_and_an_event_stream_go_all_the_way_round()
    {
        var (assembly, diagnostics) = GeneratedServer.Build("Notifications", Path.Combine(Samples, "openapi.yaml"), [], Source, withRules: false, resultful: true);
        diagnostics.Where(d => d.Severity >= Microsoft.CodeAnalysis.DiagnosticSeverity.Warning).Should().BeEmpty("callbacks and server-sent events are generated");
        var hosts = assembly.GetType("Notifications.Api.Hosts")!;
        var api = await (Task<WebApplication>)hosts.GetMethod("ApiAsync")!.Invoke(null, null)!;
        var receiver = await (Task<WebApplication>)hosts.GetMethod("ReceiverAsync")!.Invoke(null, null)!;
        try
        {
            var spec = SpecJson.Normalize(File.ReadAllText(Path.Combine(Samples, "openapi.yaml")), "openapi.yaml");
            using var apiClient = new HttpClient { BaseAddress = new Uri(api.Urls.Single() + "/") };
            using var receiverClient = new HttpClient { BaseAddress = new Uri(receiver.Urls.Single() + "/hooks/") };

            var apiReport = await RegressionSuite.FromJson(spec).RunAsync(apiClient);
            var callbackReport = await RegressionSuite.ForCallbacks(spec)!.RunAsync(receiverClient);

            File.WriteAllText(Path.Combine(TestContext.CurrentContext.WorkDirectory, "notifications.txt"), apiReport.ToString() + "\n" + callbackReport.ToString());
            apiReport.Failures.Should().BeEmpty(apiReport.ToString());
            apiReport.Cases.Should().Contain(c => c.Case.Operation.Id == "streamNotifications" && c.Exchange.ContentType == "text/event-stream" && c.Exchange.Body.Contains("data: ", StringComparison.Ordinal));
            callbackReport.Failures.Should().BeEmpty(callbackReport.ToString());
            callbackReport.Cases.Should().Contain(c => c.Case.Kind == CaseKind.Invalid && c.Case.Name.Contains("X-Signature", StringComparison.Ordinal) && c.Exchange.Status == 400);

            (await (Task<string>)hosts.GetMethod("RoundTripAsync")!.Invoke(null, [api.Urls.Single() + "/", receiver.Urls.Single() + "/hooks/"])!)
                .Should().Be("Payment 9 settled | signed True | streamed 1");
        }
        finally
        {
            await api.StopAsync();
            await receiver.StopAsync();
            await api.DisposeAsync();
            await receiver.DisposeAsync();
        }
    }

    [Test]
    public void An_event_whose_data_breaks_its_contentSchema_is_caught()
    {
        var spec = SpecJson.Normalize(File.ReadAllText(Path.Combine(Samples, "openapi.yaml")), "openapi.yaml");
        var suite = RegressionSuite.FromJson(spec);
        var request = new RegressionRequest("GET", "/notifications", [], [], null, null);

        var findings = suite.Check(request, 200, "text/event-stream", "data: {\"id\":1,\"topic\":\"weather\",\"message\":\"x\",\"at\":\"2026-01-01T00:00:00Z\"}\n\n",
            new Dictionary<string, string>());

        findings.Select(f => f.Message).Should().Contain(m => m.StartsWith("event 1's data breaks its contentSchema", StringComparison.Ordinal));
    }

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
