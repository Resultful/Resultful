using NATS.Client.Core;

namespace Matchmark.Messaging.Nats;

/// <summary>
/// NATS as the transport: an AsyncAPI address is a subject, and a parameter is one token, subscribed to as <c>*</c>.
/// Headers travel as NATS headers, and the content type as the <c>content-type</c> header. The connection is the
/// caller's, so it's shared and disposed by them.
/// </summary>
public sealed class NatsTransport(INatsConnection connection) : IMessageTransport
{
    private const string ContentType = "content-type";

    public async Task PublishAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        var headers = new NatsHeaders { [ContentType] = message.ContentType };
        foreach (var (name, value) in message.Headers)
        {
            headers[name] = value;
        }

        await connection.PublishAsync(Subject(message.Address), message.Body.ToArray(), headers, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<IAsyncDisposable> SubscribeAsync(string addressTemplate, Func<MessageEnvelope, CancellationToken, Task> handler, CancellationToken cancellationToken = default)
    {
        var subject = Subject(string.Join("*", System.Text.RegularExpressions.Regex.Split(addressTemplate, @"\{[^{}]+\}")));
        var subscription = await connection.SubscribeCoreAsync<byte[]>(subject, cancellationToken: cancellationToken).ConfigureAwait(false);
        var stopping = new CancellationTokenSource();
        var loop = Task.Run(async () =>
        {
            await foreach (var message in subscription.Msgs.ReadAllAsync(stopping.Token).ConfigureAwait(false))
            {
                var headers = (message.Headers ?? new NatsHeaders())
                    .Where(h => !string.Equals(h.Key, ContentType, StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
                var contentType = message.Headers is { } all && all.TryGetValue(ContentType, out var type) ? type.ToString() : "application/json";
                await handler(new MessageEnvelope(Address(message.Subject), contentType, headers, message.Data ?? []), stopping.Token).ConfigureAwait(false);
            }
        }, CancellationToken.None);
        await connection.PingAsync(cancellationToken).ConfigureAwait(false); // the subscription is registered on the server
        return new Subscription(subscription, stopping, loop);
    }

    /// <summary>A NATS subject separates tokens with dots, so an address's slashes become dots.</summary>
    private static string Subject(string address) => address.Replace('/', '.');

    private static string Address(string subject) => subject;

    private sealed class Subscription(INatsSub<byte[]> subscription, CancellationTokenSource stopping, Task loop) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await subscription.UnsubscribeAsync().ConfigureAwait(false);
            await stopping.CancelAsync().ConfigureAwait(false);
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            stopping.Dispose();
            await subscription.DisposeAsync().ConfigureAwait(false);
        }
    }
}
