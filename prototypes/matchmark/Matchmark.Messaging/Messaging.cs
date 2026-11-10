using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Matchmark.Messaging;

/// <summary>One message on a channel: the address it was sent to, its content type, its headers and its body.</summary>
public sealed record MessageEnvelope(string Address, string ContentType, IReadOnlyDictionary<string, string> Headers, ReadOnlyMemory<byte> Body);

/// <summary>
/// A broker, as the generated producers and consumers use it: publish to an address, and subscribe to an AsyncAPI
/// channel's address template (<c>notifications.{topic}</c>), each parameter matching one segment.
/// </summary>
public interface IMessageTransport
{
    Task PublishAsync(MessageEnvelope message, CancellationToken cancellationToken = default);

    Task<IAsyncDisposable> SubscribeAsync(string addressTemplate, Func<MessageEnvelope, CancellationToken, Task> handler, CancellationToken cancellationToken = default);
}

/// <summary>A message the producer refused to send, or the consumer refused to take, because it breaks its schema.</summary>
public sealed class MessageRejectedException(string operation, IReadOnlyDictionary<string, string[]> errors)
    : Exception($"{operation}: the message breaks its schema: {string.Join("; ", errors.Select(e => $"{(e.Key.Length == 0 ? "body" : e.Key)}: {string.Join(", ", e.Value)}"))}")
{
    public string Operation { get; } = operation;

    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>A message a generated consumer refused, and why: what a dead-letter handler is given.</summary>
public sealed record RejectedMessage(string Operation, MessageEnvelope Message, IReadOnlyDictionary<string, string[]> Errors);

/// <summary>
/// AsyncAPI channel addresses. A parameter is one segment, between the address's separators (<c>.</c> or <c>/</c>), so
/// <c>notifications.{topic}</c> takes <c>notifications.orders</c> and not <c>notifications.orders.late</c>.
/// </summary>
public static partial class ChannelAddress
{
    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex Parameter();

    /// <summary>The address with each parameter filled in. A value holding a separator or a wildcard is refused.</summary>
    public static string Format(string template, IReadOnlyDictionary<string, string> parameters)
        => Parameter().Replace(template, m =>
        {
            var value = parameters.TryGetValue(m.Groups[1].Value, out var v) ? v : throw new ArgumentException($"The address {template} needs '{m.Groups[1].Value}'.", nameof(parameters));
            if (value.Length == 0 || value.IndexOfAny(['.', '/', '*', '>', '+', '#', ' ']) >= 0)
            {
                throw new ArgumentException($"'{value}' can't be the {m.Groups[1].Value} segment of {template}.", nameof(parameters));
            }

            return value;
        });

    /// <summary>The parameters in <paramref name="address"/>, or null when it isn't one of the template's addresses.</summary>
    public static IReadOnlyDictionary<string, string>? Match(string template, string address)
    {
        var pattern = "^" + string.Concat(Parameter().Split(template).Select((part, i) => i % 2 == 0 ? Regex.Escape(part) : $"(?<{Name(part)}>[^./]+)")) + "$";
        var match = Regex.Match(address, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return match.Success ? Names(template).ToDictionary(n => n, n => match.Groups[Name(n)].Value, StringComparer.Ordinal) : null;
    }

    /// <summary>The template's parameter names, in order.</summary>
    public static IReadOnlyList<string> Names(string template) => Parameter().Matches(template).Select(m => m.Groups[1].Value).ToList();

    private static string Name(string parameter) => "p" + Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(parameter));
}

/// <summary>Header values as text, the way a broker carries them, and back to the type the message's headers schema gives.</summary>
public static class MessageHeaders
{
    public static string Write(object value) => value switch
    {
        string s => s,
        bool b => b ? "true" : "false",
        DateTimeOffset d => d.ToString("O", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>The header read as <typeparamref name="T"/>; false when it's missing or isn't one.</summary>
    public static bool TryRead<T>(IReadOnlyDictionary<string, string> headers, string name, out T value)
    {
        value = default!;
        var text = headers.FirstOrDefault(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
        if (text is null)
        {
            return false;
        }

        if (typeof(T) == typeof(string))
        {
            value = (T)(object)text;
            return true;
        }

        foreach (var json in new[] { text, JsonSerializer.Serialize(text) })
        {
            try
            {
                value = JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web)!;
                return value is not null;
            }
            catch (JsonException)
            {
            }
        }

        return false;
    }
}

/// <summary>
/// A transport in memory, for tests: what's published reaches every matching subscription before PublishAsync returns,
/// and every message is kept, in order, in <see cref="Published"/>.
/// </summary>
public sealed class InMemoryTransport : IMessageTransport
{
    private readonly ConcurrentDictionary<Guid, (string Template, Func<MessageEnvelope, CancellationToken, Task> Handler)> _subscriptions = new();
    private readonly ConcurrentQueue<MessageEnvelope> _published = new();

    public IReadOnlyCollection<MessageEnvelope> Published => _published;

    public async Task PublishAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        _published.Enqueue(message);
        foreach (var (template, handler) in _subscriptions.Values.ToList())
        {
            if (ChannelAddress.Match(template, message.Address) is not null)
            {
                await handler(message, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public Task<IAsyncDisposable> SubscribeAsync(string addressTemplate, Func<MessageEnvelope, CancellationToken, Task> handler, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        _subscriptions[id] = (addressTemplate, handler);
        return Task.FromResult<IAsyncDisposable>(new Subscription(() => _subscriptions.TryRemove(id, out _)));
    }

    private sealed class Subscription(Action dispose) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            dispose();
            return ValueTask.CompletedTask;
        }
    }
}
