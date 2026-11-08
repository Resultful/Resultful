using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Matchmark.Generator;
using NUnit.Framework;

namespace Matchmark.Testing.Tests;

/// <summary>
/// AsyncAPI 3: the Notifications sample's events. The consumer of redelivery requests is tested by the regression suite
/// through an HTTP bridge (as a broker adapter would hand it messages), correct and then lax; what the producer
/// publishes is checked message by message.
/// </summary>
[TestFixture]
public sealed class AsyncApiTests
{
    private static readonly AsyncApiDocument Document = AsyncApiDocument.Parse(SpecJson.AsyncApi(
        File.ReadAllText(Path.GetFullPath(Path.Combine(ThisDirectory(), "..", "samples", "notifications", "asyncapi.yaml"))), "asyncapi.yaml"));

    [Test]
    public void The_operations_and_their_messages_are_read_through_their_references()
    {
        Document.Operations.Select(o => (o.Id, o.Action, o.Channel, o.Messages.Count)).Should().Equal(
            ("publishNotification", "send", "notifications.{topic}", 1),
            ("receiveRedelivery", "receive", "notifications.redeliver", 1));
        Document.Operations[1].Messages[0].ContentType.Should().Be("application/json");
    }

    [Test]
    public async Task A_consumer_that_checks_what_it_receives_passes()
    {
        var report = await Run(lax: false);

        File.WriteAllText(Path.Combine(TestContext.CurrentContext.WorkDirectory, "asyncapi-consumer.txt"), report.ToString());
        report.Failures.Should().BeEmpty(report.ToString());
        report.Cases.Should().Contain(c => c.Case.Kind == CaseKind.Invalid && c.Case.Name.Contains("attempts", StringComparison.Ordinal), report.ToString());
        report.Cases.Should().Contain(c => c.Case.Kind == CaseKind.Invalid && c.Case.Name.Contains("x-correlation-id", StringComparison.Ordinal), report.ToString());
    }

    [Test]
    public async Task A_consumer_that_takes_anything_is_caught()
    {
        var report = await Run(lax: true);

        File.WriteAllText(Path.Combine(TestContext.CurrentContext.WorkDirectory, "asyncapi-lax.txt"), report.ToString());
        report.Failures.Should().Contain(f => f.Check == Checks.InvalidRejected && f.Subject.Contains("attempts", StringComparison.Ordinal), report.ToString());
        report.Failures.Should().Contain(f => f.Check == Checks.InvalidRejected && f.Subject.Contains("x-correlation-id", StringComparison.Ordinal), report.ToString());
    }

    [Test]
    public void What_the_producer_sends_is_checked_against_its_message()
    {
        var id = Guid.NewGuid().ToString();
        var headers = new JsonObject { ["x-correlation-id"] = Guid.NewGuid().ToString() };
        Document.Check("publishNotification", Notification(id, "orders"), headers).Should().BeEmpty();

        Document.Check("publishNotification", Notification(id, "billing"), headers).Should().ContainSingle(e => e.Contains("payload.topic", StringComparison.Ordinal));
        Document.Check("publishNotification", Notification("not-a-uuid", "orders"), headers).Should().ContainSingle(e => e.Contains("payload.id", StringComparison.Ordinal));
        Document.Check("publishNotification", Notification(id, "orders"), []).Should().ContainSingle(e => e.Contains("x-correlation-id", StringComparison.Ordinal));
        FluentActions.Invoking(() => Document.Check("nothing", null)).Should().Throw<KeyNotFoundException>();
    }

    private static JsonObject Notification(string id, string topic)
        => new() { ["id"] = id, ["topic"] = topic, ["sentAt"] = "2026-11-08T10:00:00Z", ["summary"] = "Order 7 shipped" };

    private static async Task<RegressionReport> Run(bool lax)
    {
        var suite = Document.ConsumerSuite() ?? throw new InvalidOperationException("The sample receives nothing.");
        using var client = new HttpClient(new Bridge(lax)) { BaseAddress = new Uri("http://bridge/") };
        return await suite.RunAsync(client);
    }

    /// <summary>
    /// A broker adapter in memory: each request is a message for the consumer, which takes a redelivery only when its
    /// correlation id and payload are what the document says. Lax, it takes anything that's JSON.
    /// </summary>
    private sealed class Bridge(bool lax) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Post || request.RequestUri?.AbsolutePath != "/receiveRedelivery")
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            JsonNode? payload;
            try
            {
                payload = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            }
            catch (JsonException)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            if (request.Content.Headers.ContentType?.MediaType != "application/json")
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            var correlation = request.Headers.TryGetValues("x-correlation-id", out var values) ? values.FirstOrDefault() : null;
            var accepted = lax || (Guid.TryParse(correlation, out _) && Valid(payload));
            return new HttpResponseMessage(accepted ? HttpStatusCode.Accepted : HttpStatusCode.BadRequest);
        }

        private static bool Valid(JsonNode? payload)
            => payload is JsonObject message
               && message.All(p => p.Key is "notificationId" or "attempts" or "reason")
               && message["notificationId"] is JsonValue id && id.GetValueKind() == JsonValueKind.String && Guid.TryParse(id.GetValue<string>(), out _)
               && message["attempts"] is JsonValue attempts && attempts.GetValueKind() == JsonValueKind.Number
               && attempts.GetValue<decimal>() is var n && n == decimal.Truncate(n) && n is >= 1 and <= 5
               && (message["reason"] is null && !message.ContainsKey("reason")
                   || message["reason"] is JsonValue reason && reason.GetValueKind() == JsonValueKind.String && reason.GetValue<string>().Length is >= 1 and <= 200);
    }

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
