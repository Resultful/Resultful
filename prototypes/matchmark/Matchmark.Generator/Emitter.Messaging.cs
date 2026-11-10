using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace Matchmark.Generator
{
    /// <summary>
    /// An AsyncAPI document's operations on a broker, generated when the project references Matchmark.Messaging. The
    /// document's receive operations become <c>I{Api}Consumer</c>, which the application implements, and
    /// <c>Subscribe{Api}</c> subscribes it to their channels. Each message is checked as it arrives: its content type,
    /// its headers and its payload against the document. <c>{Api}Producer</c> sends each operation's message, checked the
    /// same way before it leaves, to its channel's address. The send operations are the application's own; the receive
    /// operations' methods are for its counterpart, such as a test. Run on the webhooks' emitter, whose operations these are.
    /// </summary>
    internal sealed partial class Emitter
    {
        private static readonly string[] MessagingUsings = { "System", "System.Collections.Generic", "System.Linq", "System.Text.Json", "System.Threading", "System.Threading.Tasks", "Matchmark.Messaging" };

        /// <summary>Whether Matchmark.Messaging is referenced, so a document's operations get a consumer and a producer.</summary>
        public bool Messaging { get; set; }

        private sealed class Channel
        {
            public Channel(string action, string address, string contentType, List<(string Name, string Identifier, List<string>? Enum)> parameters)
            {
                Action = action;
                Address = address;
                ContentType = contentType;
                Parameters = parameters;
            }

            public string Action { get; }

            public string Address { get; }

            public string ContentType { get; }

            public List<(string Name, string Identifier, List<string>? Enum)> Parameters { get; }
        }

        internal string? EmitMessaging(string api, IDictionary<string, IOpenApiPathItem> webhooks)
        {
            CollectSchemas();
            var channels = new Dictionary<string, Channel>(StringComparer.Ordinal);
            foreach (var webhook in webhooks)
            {
                var spec = webhook.Value.Operations?.Values.FirstOrDefault();
                if (spec?.OperationId is not { } id || Extension(spec.Extensions, "x-asyncapi-action")?.GetValue<string>() is not { } action
                    || Extension(spec.Extensions, "x-asyncapi-channel")?.GetValue<string>() is not { } address)
                {
                    continue;
                }

                var parameters = (Extension(spec.Extensions, "x-asyncapi-parameters") as JsonArray ?? new JsonArray()).OfType<JsonObject>()
                    .Select(p => (Name: p["name"]!.GetValue<string>(), Enum: (p["enum"] as JsonArray)?.Select(v => v!.ToString()).ToList())).ToList();
                var contentType = spec.RequestBody?.Content?.Keys.FirstOrDefault() ?? "application/json";
                channels[id] = new Channel(action, address, contentType, parameters.Select(p => (p.Name, Naming.Camel(p.Name), p.Enum)).ToList());
            }

            var operations = CollectOperations().Where(o => channels.ContainsKey(o.Id) && o.Body is { Form: false, Text: false }).ToList();
            if (operations.Count == 0)
            {
                return null;
            }

            var w = new CodeWriter(_namespace);
            w.Usings(MessagingUsings);
            var receives = operations.Where(o => channels[o.Id].Action == "receive").ToList();

            w.Line($"/// <summary>The messages {api} receives (its AsyncAPI receive operations), each already checked against the document.</summary>");
            w.Open($"public interface I{api}Consumer");
            foreach (var operation in receives)
            {
                w.Line($"/// <summary>A message on <c>{channels[operation.Id].Address}</c>.</summary>");
                w.Line($"Task {operation.Name}({string.Join(", ", Arguments(operation, channels[operation.Id]).Select(a => $"{a.Type} {a.Identifier}").Append("CancellationToken cancellationToken"))});");
            }

            w.Close();
            w.Line();

            w.Line($"/// <summary>Sends {api}'s messages to their channels, each checked against the document first: a message that breaks it is refused with a <see cref=\"MessageRejectedException\"/>, and never sent.</summary>");
            w.Open($"public sealed class {api}Producer");
            w.Line("private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);");
            w.Line("private readonly IMessageTransport _transport;");
            w.Line();
            w.Line($"public {api}Producer(IMessageTransport transport) => _transport = transport;");
            foreach (var operation in operations)
            {
                var channel = channels[operation.Id];
                var arguments = Arguments(operation, channel);
                w.Line();
                w.Line(channel.Action == "send"
                    ? $"/// <summary>Sends to <c>{channel.Address}</c> ({operation.Id}, which {api} sends).</summary>"
                    : $"/// <summary>Sends to <c>{channel.Address}</c> ({operation.Id}, which {api} receives: for its counterpart, such as a test).</summary>");
                w.Open($"public async Task {operation.Name}({string.Join(", ", arguments.Select(a => $"{a.Type} {a.Identifier}").Append("CancellationToken cancellationToken = default"))})");
                w.Line("ArgumentNullException.ThrowIfNull(message);");
                foreach (var parameter in channel.Parameters.Where(p => p.Enum is not null))
                {
                    w.Line($"if (!new[] {{ {string.Join(", ", parameter.Enum!.Select(Naming.Literal))} }}.Contains({parameter.Identifier})) throw new MessageRejectedException({Naming.Literal(operation.Id)}, new Dictionary<string, string[]> {{ [{Naming.Literal(parameter.Name)}] = new[] {{ {Naming.Literal($"Must be one of: {string.Join(", ", parameter.Enum!)}.")} }} }});");
                }

                if (operation.Body!.Validated)
                {
                    w.Line($"if ({ValidationClass}.Validate(message) is {{ }} __errors) throw new MessageRejectedException({Naming.Literal(operation.Id)}, __errors);");
                }

                var values = string.Join(", ", channel.Parameters.Select(p => $"[{Naming.Literal(p.Name)}] = {p.Identifier}"));
                w.Line($"var __address = ChannelAddress.Format({Naming.Literal(channel.Address)}, new Dictionary<string, string> {{ {values} }});");
                w.Line("var __headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);");
                foreach (var header in Headers(operation))
                {
                    w.Line(header.Type.EndsWith("?", StringComparison.Ordinal)
                        ? $"if ({header.Identifier} is {{ }} __{header.Identifier.TrimStart('@')}) __headers[{Naming.Literal(header.Name)}] = MessageHeaders.Write(__{header.Identifier.TrimStart('@')});"
                        : $"__headers[{Naming.Literal(header.Name)}] = MessageHeaders.Write({header.Identifier});");
                }

                w.Line($"await _transport.PublishAsync(new MessageEnvelope(__address, {Naming.Literal(channel.ContentType)}, __headers, JsonSerializer.SerializeToUtf8Bytes(message, Json)), cancellationToken).ConfigureAwait(false);");
                w.Close();
            }

            w.Close();
            w.Line();

            w.Line($"/// <summary>Subscribes an <see cref=\"I{api}Consumer\"/> to the channels of {api}'s receive operations.</summary>");
            w.Open($"public static class {api}Consumers");
            w.Line("private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);");
            w.Line();
            w.Line("/// <summary>");
            w.Line("/// Each receive operation's channel, subscribed: a message reaches the consumer only when its content type, headers and");
            w.Line("/// payload match the document. One that doesn't goes to <paramref name=\"rejected\"/> (a dead letter), if given. Dispose the");
            w.Line("/// result to stop.");
            w.Line("/// </summary>");
            w.Open($"public static async Task<IAsyncDisposable> Subscribe{api}(this IMessageTransport transport, I{api}Consumer consumer, Func<RejectedMessage, CancellationToken, Task>? rejected = null, CancellationToken cancellationToken = default)");
            w.Line("ArgumentNullException.ThrowIfNull(transport);");
            w.Line("ArgumentNullException.ThrowIfNull(consumer);");
            w.Line("var __subscriptions = new List<IAsyncDisposable>();");
            foreach (var operation in receives)
            {
                var channel = channels[operation.Id];
                w.Open($"__subscriptions.Add(await transport.SubscribeAsync({Naming.Literal(channel.Address)}, async (__message, __cancellation) =>");
                w.Line("var __errors = new Dictionary<string, string[]>();");
                w.Line($"var __parameters = ChannelAddress.Match({Naming.Literal(channel.Address)}, __message.Address) ?? new Dictionary<string, string>();");
                w.Line($"if (!__message.ContentType.StartsWith({Naming.Literal(channel.ContentType)}, StringComparison.OrdinalIgnoreCase)) __errors[\"content-type\"] = new[] {{ {Naming.Literal($"Must be {channel.ContentType}.")} }};");
                foreach (var parameter in channel.Parameters)
                {
                    w.Line($"var {parameter.Identifier} = __parameters.TryGetValue({Naming.Literal(parameter.Name)}, out var __{parameter.Identifier}) ? __{parameter.Identifier} : string.Empty;");
                    if (parameter.Enum is not null)
                    {
                        // Channels' addresses can overlap (notifications.{topic} and notifications.redeliver): a value the
                        // channel doesn't have means the message is another channel's, so it's left alone, not rejected.
                        w.Line($"if (!new[] {{ {string.Join(", ", parameter.Enum.Select(Naming.Literal))} }}.Contains({parameter.Identifier})) return;");
                    }
                }

                foreach (var header in Headers(operation))
                {
                    var type = header.Type.TrimEnd('?');
                    var required = !header.Type.EndsWith("?", StringComparison.Ordinal);
                    w.Line($"{header.Type} {header.Identifier} = default;");
                    w.Line($"if (MessageHeaders.TryRead<{type}>(__message.Headers, {Naming.Literal(header.Name)}, out var __{header.Identifier.TrimStart('@')})) {header.Identifier} = __{header.Identifier.TrimStart('@')};");
                    w.Line(required
                        ? $"else __errors[{Naming.Literal(header.Name)}] = new[] {{ {Naming.Literal($"A {header.Name} header is required.")} }};"
                        : $"else if (__message.Headers.ContainsKey({Naming.Literal(header.Name)})) __errors[{Naming.Literal(header.Name)}] = new[] {{ {Naming.Literal($"The {header.Name} header isn't a {type}.")} }};");
                }

                w.Line($"{operation.Body!.Type}? message = default;");
                w.Open("try");
                w.Line($"message = JsonSerializer.Deserialize<{operation.Body.Type}>(__message.Body.Span, Json);");
                w.Close();
                w.Open("catch (JsonException e)");
                w.Line("__errors[string.Empty] = new[] { e.Message };");
                w.Close();
                w.Line();
                w.Line("if (message is null && __errors.Count == 0) __errors[string.Empty] = new[] { \"The message has no payload.\" };");
                if (operation.Body.Validated)
                {
                    w.Line($"if (message is not null && {ValidationClass}.Validate(message) is {{ }} __invalid) foreach (var __error in __invalid) __errors[__error.Key] = __error.Value;");
                }

                w.Open("if (__errors.Count > 0)");
                w.Line($"if (rejected is not null) await rejected(new RejectedMessage({Naming.Literal(operation.Id)}, __message, __errors), __cancellation).ConfigureAwait(false);");
                w.Line("return;");
                w.Close();
                w.Line();
                var call = channel.Parameters.Select(p => p.Identifier).Concat(Headers(operation).Select(h => h.Identifier)).Append("message!").Append("__cancellation");
                w.Line($"await consumer.{operation.Name}({string.Join(", ", call)}).ConfigureAwait(false);");
                w.Close(braces: true, suffix: ", cancellationToken).ConfigureAwait(false));");
            }

            w.Line("return new Subscriptions(__subscriptions);");
            w.Close();
            w.Line();
            w.Open("private sealed class Subscriptions : IAsyncDisposable");
            w.Line("private readonly List<IAsyncDisposable> _subscriptions;");
            w.Line();
            w.Line("public Subscriptions(List<IAsyncDisposable> subscriptions) => _subscriptions = subscriptions;");
            w.Line();
            w.Open("public async ValueTask DisposeAsync()");
            w.Line("foreach (var subscription in _subscriptions) await subscription.DisposeAsync().ConfigureAwait(false);");
            w.Close();
            w.Close();
            w.Close();
            return w.ToString();
        }

        private static IEnumerable<Param> Headers(Operation operation) => operation.Parameters.Where(p => p.Attribute == "FromHeader");

        /// <summary>An operation's arguments: its channel's parameters, its headers, and the message.</summary>
        private static List<(string Type, string Identifier)> Arguments(Operation operation, Channel channel)
            => channel.Parameters.Select(p => ("string", p.Identifier))
                .Concat(Headers(operation).Select(h => (h.Type, h.Identifier)))
                .Append((operation.Body!.Type, "message")).ToList();
    }
}
