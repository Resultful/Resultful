using System.Reflection;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Matchmark.Messaging;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// The Notifications sample's AsyncAPI document on a broker: with Matchmark.Messaging referenced, its receive operation
/// becomes <c>IEventsConsumer</c> and its operations <c>EventsProducer</c>. One scenario runs on the in-memory transport
/// and, when a NATS server is given (MATCHMARK_NATS_URL, as CI's service container does), on NATS: a valid message
/// reaches the consumer typed; a message that breaks the document is refused by the producer before it's sent, and by
/// the consumer when it arrives anyway; and a notification goes to its channel's address.
/// </summary>
[TestFixture]
public sealed class MessagingTests
{
    private static readonly string Spec = Path.GetFullPath(Path.Combine(ThisDirectory(), "..", "samples", "notifications", "asyncapi.yaml"));

    private const string Scenario = """
        using System;
        using System.Collections.Concurrent;
        using System.Collections.Generic;
        using System.Linq;
        using System.Text;
        using System.Threading;
        using System.Threading.Tasks;
        using Matchmark.Messaging;

        namespace Events.Api;

        public sealed class Redeliveries : IEventsConsumer
        {
            public readonly ConcurrentQueue<(Guid Correlation, Redelivery Message)> Taken = new();

            public Task ReceiveRedelivery(Guid xCorrelationId, Redelivery message, CancellationToken cancellationToken)
            {
                Taken.Enqueue((xCorrelationId, message));
                return Task.CompletedTask;
            }
        }

        public static class Conversation
        {
            /// <summary>What happened, one line each, for the test to read.</summary>
            public static async Task<List<string>> RunAsync(IMessageTransport transport)
            {
                var lines = new List<string>();
                var consumer = new Redeliveries();
                var rejected = new ConcurrentQueue<RejectedMessage>();
                var notifications = new ConcurrentQueue<MessageEnvelope>();
                await using var subscription = await transport.SubscribeEvents(consumer, (r, _) => { rejected.Enqueue(r); return Task.CompletedTask; });
                await using var watch = await transport.SubscribeAsync("notifications.orders", (m, _) => { notifications.Enqueue(m); return Task.CompletedTask; });
                var producer = new EventsProducer(transport);

                // A valid redelivery, sent as the consumer's counterpart would: taken, typed.
                var correlation = Guid.NewGuid();
                var notification = Guid.NewGuid();
                await producer.ReceiveRedelivery(correlation, new Redelivery { NotificationId = notification, Attempts = 2, Reason = "lost" });
                await Until(() => consumer.Taken.Count == 1);
                var taken = consumer.Taken.Single();
                lines.Add($"taken {taken.Correlation == correlation} {taken.Message.NotificationId == notification} {taken.Message.Attempts}");

                // Breaking the document: refused before it's sent.
                try
                {
                    await producer.ReceiveRedelivery(Guid.NewGuid(), new Redelivery { NotificationId = notification, Attempts = 9 });
                    lines.Add("sent attempts 9");
                }
                catch (MessageRejectedException e)
                {
                    lines.Add($"refused {string.Join(",", e.Errors.Keys)}");
                }

                // Sent anyway, by something that isn't the generated producer: refused as it arrives, never reaching the consumer.
                await transport.PublishAsync(new MessageEnvelope("notifications.redeliver", "application/json", new Dictionary<string, string>(),
                    Encoding.UTF8.GetBytes($"{{\"notificationId\":\"{notification}\",\"attempts\":0}}")));
                await transport.PublishAsync(new MessageEnvelope("notifications.redeliver", "text/plain", new Dictionary<string, string> { ["x-correlation-id"] = Guid.NewGuid().ToString() },
                    Encoding.UTF8.GetBytes("redeliver it")));
                await Until(() => rejected.Count == 2);
                foreach (var r in rejected)
                {
                    lines.Add($"rejected {r.Operation} {string.Join(",", r.Errors.Keys.Order(StringComparer.Ordinal))}");
                }

                lines.Add($"taken count {consumer.Taken.Count}");

                // A notification, to its channel's address; a topic the channel doesn't have is refused.
                await producer.PublishNotification("orders", Guid.NewGuid(), new Notification { Id = Guid.NewGuid(), Topic = "orders", SentAt = DateTimeOffset.UtcNow, Summary = "Order 7 shipped" });
                await Until(() => notifications.Count == 1);
                var published = notifications.Single();
                lines.Add($"published {published.Address} {published.ContentType} {published.Headers.ContainsKey("x-correlation-id")}");
                try
                {
                    await producer.PublishNotification("shipping", Guid.NewGuid(), new Notification { Id = Guid.NewGuid(), Topic = "orders", SentAt = DateTimeOffset.UtcNow });
                    lines.Add("published shipping");
                }
                catch (MessageRejectedException e)
                {
                    lines.Add($"refused {string.Join(",", e.Errors.Keys)}");
                }

                return lines;
            }

            private static async Task Until(Func<bool> condition)
            {
                for (var i = 0; i < 100 && !condition(); i++)
                {
                    await Task.Delay(50);
                }
            }
        }
        """;

    private static readonly string[] Expected =
    [
        "taken True True 2",
        "refused attempts",
        "rejected receiveRedelivery attempts,x-correlation-id",
        "rejected receiveRedelivery ,content-type",
        "taken count 1",
        "published notifications.orders application/json True",
        "refused topic",
    ];

    private static Assembly Build()
    {
        var (assembly, diagnostics) = GeneratedServer.Build("Events", Spec, [], Scenario, withRules: false, withMessaging: true);
        diagnostics.Where(d => d.Severity >= Microsoft.CodeAnalysis.DiagnosticSeverity.Warning).Should().BeEmpty();
        assembly.GetType("Events.Api.IEventsConsumer")!.GetMethods().Select(m => m.Name).Should().Equal("ReceiveRedelivery");
        assembly.GetType("Events.Api.EventsProducer")!.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name).Should().BeEquivalentTo("PublishNotification", "ReceiveRedelivery");
        return assembly;
    }

    private static Task<List<string>> Run(Assembly assembly, IMessageTransport transport)
        => (Task<List<string>>)assembly.GetType("Events.Api.Conversation")!.GetMethod("RunAsync")!.Invoke(null, [transport])!;

    [Test]
    public async Task In_memory_the_consumer_takes_what_the_document_allows_and_nothing_else()
    {
        var transport = new InMemoryTransport();
        var lines = await Run(Build(), transport);

        lines.Should().Equal(Expected);
        transport.Published.Should().HaveCount(4, "the producer sent two, the test two by hand; the refused ones were never sent");
    }

    [Test]
    public async Task On_NATS_the_same()
    {
        var url = Environment.GetEnvironmentVariable("MATCHMARK_NATS_URL");
        if (string.IsNullOrEmpty(url))
        {
            Assert.Ignore("Set MATCHMARK_NATS_URL to a NATS server (nats://localhost:4222) to run on a broker.");
        }

        await using var connection = new NATS.Client.Core.NatsConnection(new NATS.Client.Core.NatsOpts { Url = url });
        await connection.ConnectAsync();
        var lines = await Run(Build(), new Matchmark.Messaging.Nats.NatsTransport(connection));

        lines.Should().Equal(Expected);
    }

    [Test]
    public void Without_the_runtime_nothing_is_generated_for_a_broker()
    {
        var (assembly, _) = GeneratedServer.Build("Events", Spec, [], "namespace Events.Api;", withRules: false);
        assembly.GetType("Events.Api.EventsProducer").Should().BeNull();
        assembly.GetType("Events.Api.IEventsWebhooksHandlers").Should().NotBeNull("the webhooks are generated as before");
    }

    [Test]
    public void Channel_addresses_take_one_segment_per_parameter()
    {
        ChannelAddress.Format("notifications.{topic}", new Dictionary<string, string> { ["topic"] = "orders" }).Should().Be("notifications.orders");
        ChannelAddress.Match("notifications.{topic}", "notifications.orders").Should().Contain("topic", "orders");
        ChannelAddress.Match("notifications.{topic}", "notifications.orders.late").Should().BeNull();
        ChannelAddress.Match("users/{id}/signedup", "users/7/signedup").Should().Contain("id", "7");
        FluentActions.Invoking(() => ChannelAddress.Format("notifications.{topic}", new Dictionary<string, string> { ["topic"] = "a.b" })).Should().Throw<ArgumentException>();
    }

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
