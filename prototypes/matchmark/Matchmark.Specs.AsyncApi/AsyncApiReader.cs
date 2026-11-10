using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Matchmark.Specs.AsyncApi
{
    /// <summary>An AsyncAPI document, read: its version, title, and channels and operations by key.</summary>
    public sealed class AsyncApiDocument
    {
        public AsyncApiDocument(string version, string title, IReadOnlyList<string> channels, IReadOnlyList<AsyncApiOperation> operations, JsonObject source)
        {
            Version = version;
            Title = title;
            Channels = channels;
            Operations = operations;
            Source = source;
        }

        /// <summary>The <c>asyncapi</c> field: 3.0.0 or 3.1.0.</summary>
        public string Version { get; }

        public string Title { get; }

        /// <summary>The channels' keys.</summary>
        public IReadOnlyList<string> Channels { get; }

        public IReadOnlyList<AsyncApiOperation> Operations { get; }

        /// <summary>The document as read, for what the model doesn't hold.</summary>
        public JsonObject Source { get; }
    }

    public sealed class AsyncApiOperation
    {
        public AsyncApiOperation(string key, string action, string channel)
        {
            Key = key;
            Action = action;
            Channel = channel;
        }

        public string Key { get; }

        /// <summary><c>send</c> or <c>receive</c>.</summary>
        public string Action { get; }

        /// <summary>The <c>$ref</c> of its channel.</summary>
        public string Channel { get; }
    }

    /// <summary>
    /// Reads an AsyncAPI 3.0 or 3.1 document as the specification's text and schemas define it (specs/asyncapi), with
    /// every problem at its JSON pointer. Checked: the whole document structure, every object closed to its fields and
    /// <c>x-</c> extensions; references; security schemes by type; schemas as AsyncAPI's draft-07 Schema Object (or a
    /// multi-format schema). Not checked: what a protocol binding holds (each binding's own schema versions it), schemas
    /// in other formats (OpenAPI, Avro, RAML), and the info extensions <c>x-x</c> and <c>x-linkedin</c>. A 2.x document is
    /// read after <see cref="AsyncApiUpgrade"/> forward-ports it.
    /// </summary>
    public static class AsyncApiReader
    {
        private static readonly Regex Location = new Regex(@"^\$message\.(header|payload)#(\/(([^\/~])|(~[01]))*)*", RegexOptions.CultureInvariant);
        private static readonly Regex ComponentName = new Regex(@"^[\w\d\.\-_]+$", RegexOptions.CultureInvariant);
        private static readonly string[] ServerProtocols = { "http", "ws", "amqp", "amqp1", "mqtt", "kafka", "anypointmq", "nats", "jms", "sns", "solace", "sqs", "stomp", "redis", "ibmmq", "googlepubsub", "pulsar" };
        private static readonly string[] MessageProtocols = ServerProtocols.Where(p => p != "pulsar").ToArray();
        private static readonly string[] JsonSchemaFormats =
            new[] { "2.0.0", "2.1.0", "2.2.0", "2.3.0", "2.4.0", "2.5.0", "2.6.0", "3.0.0", "3.1.0" }
                .SelectMany(v => new[] { $"application/vnd.aai.asyncapi;version={v}", $"application/vnd.aai.asyncapi+json;version={v}", $"application/vnd.aai.asyncapi+yaml;version={v}" })
                .Concat(new[] { "application/schema+json;version=draft-07", "application/schema+yaml;version=draft-07" }).ToArray();

        public static ReadResult<AsyncApiDocument> Read(string text, string path) => Read(Documents.Load(text, path));

        public static ReadResult<AsyncApiDocument> Read(JsonNode? node)
        {
            var problems = new List<Problem>();
            var shape = new Shape(problems);
            var c = new Checks(shape);
            if (shape.Object(node, string.Empty, "An AsyncAPI document") is not { } root)
            {
                return new ReadResult<AsyncApiDocument>(null, problems);
            }

            var version = shape.String(root, "asyncapi", string.Empty, required: true);
            if (version is not null && version is not ("3.0.0" or "3.1.0"))
            {
                shape.Add("/asyncapi", version.StartsWith("2.", StringComparison.Ordinal)
                    ? $"'{version}' is read by forward-porting it to {AsyncApiUpgrade.Latest} first (AsyncApiUpgrade)."
                    : $"'{version}' isn't an AsyncAPI version this reader knows: 3.0.0 or 3.1.0.");
            }

            shape.Only(root, string.Empty, "An AsyncAPI document", "asyncapi", "id", "info", "servers", "defaultContentType", "channels", "operations", "components");
            shape.String(root, "id", string.Empty);
            shape.String(root, "defaultContentType", string.Empty);
            var title = c.Info(root);
            c.Map(root, "servers", string.Empty, (n, p) => c.RefOr(n, p, "A Server Object", c.Server));
            var channels = c.Map(root, "channels", string.Empty, (n, p) => c.RefOr(n, p, "A Channel Object", c.Channel));
            var operations = new List<AsyncApiOperation>();
            c.Map(root, "operations", string.Empty, (n, p) => c.RefOr(n, p, "An Operation Object", (o, at) =>
            {
                if (c.Operation(o, at) is { } read)
                {
                    operations.Add(new AsyncApiOperation(Pointer.Unescape(at.Substring(at.LastIndexOf('/') + 1)), read.Action, read.Channel));
                }
            }));
            if (root.TryGetPropertyValue("components", out var components))
            {
                c.Components(components, "/components");
            }

            var document = version is null || title is null ? null : new AsyncApiDocument(version, title, channels, operations, root);
            return new ReadResult<AsyncApiDocument>(document, problems);
        }

        private sealed class Checks
        {
            private readonly Shape _shape;

            public Checks(Shape shape) => _shape = shape;

            /// <summary>Each entry of a map field, checked; its keys. Only keys matching <paramref name="names"/> are checked when given.</summary>
            public List<string> Map(JsonObject owner, string field, string pointer, Action<JsonNode?, string> entry, Regex? names = null)
            {
                var keys = new List<string>();
                if (!owner.TryGetPropertyValue(field, out var node))
                {
                    return keys;
                }

                var at = Pointer.Join(pointer, field);
                if (_shape.Object(node, at, $"'{field}'") is not { } map)
                {
                    return keys;
                }

                foreach (var pair in map.Where(p => names is null || names.IsMatch(p.Key)))
                {
                    keys.Add(pair.Key);
                    entry(pair.Value, Pointer.Join(at, pair.Key));
                }

                return keys;
            }

            /// <summary>A Reference Object (an object with <c>$ref</c>), or the object <paramref name="check"/> checks.</summary>
            public void RefOr(JsonNode? node, string pointer, string what, Action<JsonObject, string> check, bool objectsOnly = true)
            {
                if (!objectsOnly && node is not JsonObject)
                {
                    return; // The schema's definition doesn't say 'type: object', so anything else passes it (the Parameter Object).
                }

                if (_shape.Object(node, pointer, what) is not { } obj)
                {
                    return;
                }

                if (obj.ContainsKey("$ref"))
                {
                    _shape.String(obj, "$ref", pointer);
                    return;
                }

                check(obj, pointer);
            }

            /// <summary>A Reference Object, and only one.</summary>
            private string? Reference(JsonNode? node, string pointer, string what)
            {
                if (_shape.Object(node, pointer, what) is not { } obj)
                {
                    return null;
                }

                return _shape.String(obj, "$ref", pointer, required: true);
            }

            private void Strings(JsonObject owner, string pointer, params string[] fields)
            {
                foreach (var field in fields)
                {
                    _shape.String(owner, field, pointer);
                }
            }

            private void List(JsonObject owner, string field, string pointer, bool unique, Action<JsonNode?, string> item)
            {
                if (_shape.Array(owner, field, pointer) is not { } array)
                {
                    return;
                }

                var at = Pointer.Join(pointer, field);
                if (unique)
                {
                    _shape.Unique(array, at, field);
                }

                for (var i = 0; i < array.Count; i++)
                {
                    item(array[i], Pointer.Join(at, i));
                }
            }

            private void StringList(JsonObject owner, string field, string pointer, bool unique)
                => List(owner, field, pointer, unique, (item, at) =>
                {
                    if (!(item is JsonValue v && v.GetValueKind() == JsonValueKind.String))
                    {
                        _shape.Add(at, $"Each of '{field}' must be a string.");
                    }
                });

            private void Tags(JsonObject owner, string pointer)
                => List(owner, "tags", pointer, unique: true, (item, at) => RefOr(item, at, "A Tag Object", (tag, p) =>
                {
                    _shape.Only(tag, p, "A Tag Object", "name", "description", "externalDocs");
                    _shape.String(tag, "name", p, required: true);
                    _shape.String(tag, "description", p);
                    ExternalDocs(tag, p);
                }));

            private void ExternalDocs(JsonObject owner, string pointer)
            {
                if (owner.TryGetPropertyValue("externalDocs", out var node))
                {
                    RefOr(node, Pointer.Join(pointer, "externalDocs"), "An External Documentation Object", (docs, p) =>
                    {
                        _shape.Only(docs, p, "An External Documentation Object", "url", "description");
                        _shape.String(docs, "url", p, required: true);
                        _shape.String(docs, "description", p);
                    });
                }
            }

            private void Bindings(JsonObject owner, string pointer, string[] protocols)
            {
                if (owner.TryGetPropertyValue("bindings", out var node))
                {
                    RefOr(node, Pointer.Join(pointer, "bindings"), "A Bindings Object", (bindings, p) => BindingsObject(bindings, p, protocols));
                }
            }

            /// <summary>A Bindings Object: the protocols it names; what each holds is versioned by that binding's own schema.</summary>
            private void BindingsObject(JsonObject bindings, string pointer, string[] protocols) => _shape.Only(bindings, pointer, "A Bindings Object", protocols);

            private void Location(JsonObject owner, string pointer, bool required)
            {
                if (_shape.String(owner, "location", pointer, required) is { } location && !AsyncApiReader.Location.IsMatch(location))
                {
                    _shape.Add(Pointer.Join(pointer, "location"), $"'{location}' isn't a runtime expression into a message: $message.header#... or $message.payload#...");
                }
            }

            public string? Info(JsonObject root)
            {
                if (!root.TryGetPropertyValue("info", out var node))
                {
                    _shape.Add(string.Empty, "'info' is required.");
                    return null;
                }

                if (_shape.Object(node, "/info", "'info'") is not { } info)
                {
                    return null;
                }

                const string at = "/info";
                _shape.Only(info, at, "The Info Object", "title", "version", "description", "termsOfService", "contact", "license", "tags", "externalDocs");
                var title = _shape.String(info, "title", at, required: true);
                var version = _shape.String(info, "version", at, required: true);
                Strings(info, at, "description", "termsOfService");
                if (info.TryGetPropertyValue("contact", out var contactNode) && _shape.Object(contactNode, "/info/contact", "'contact'") is { } contact)
                {
                    _shape.Only(contact, "/info/contact", "The Contact Object", "name", "url", "email");
                    Strings(contact, "/info/contact", "name", "url", "email");
                }

                if (info.TryGetPropertyValue("license", out var licenseNode) && _shape.Object(licenseNode, "/info/license", "'license'") is { } license)
                {
                    _shape.Only(license, "/info/license", "The License Object", "name", "url");
                    _shape.String(license, "name", "/info/license", required: true);
                    _shape.String(license, "url", "/info/license");
                }

                Tags(info, at);
                ExternalDocs(info, at);
                return version is null ? null : title;
            }

            public void Server(JsonObject server, string pointer)
            {
                _shape.Only(server, pointer, "A Server Object", "host", "protocol", "protocolVersion", "pathname", "description", "title", "summary", "variables", "security", "tags", "externalDocs", "bindings");
                _shape.String(server, "host", pointer, required: true);
                _shape.String(server, "protocol", pointer, required: true);
                Strings(server, pointer, "protocolVersion", "pathname", "description", "title", "summary");
                Map(server, "variables", pointer, (n, p) => RefOr(n, p, "A Server Variable Object", ServerVariable));
                Security(server, pointer);
                Tags(server, pointer);
                ExternalDocs(server, pointer);
                Bindings(server, pointer, ServerProtocols);
            }

            private void ServerVariable(JsonObject variable, string pointer)
            {
                _shape.Only(variable, pointer, "A Server Variable Object", "enum", "default", "description", "examples");
                StringList(variable, "enum", pointer, unique: true);
                Strings(variable, pointer, "default", "description");
                StringList(variable, "examples", pointer, unique: false);
            }

            private void Security(JsonObject owner, string pointer)
                => List(owner, "security", pointer, unique: false, (item, at) => RefOr(item, at, "A Security Scheme Object", SecurityScheme));

            /// <summary>A Security Scheme Object, by its type.</summary>
            private void SecurityScheme(JsonObject scheme, string pointer)
            {
                var type = _shape.String(scheme, "type", pointer, required: true);
                _shape.String(scheme, "description", pointer);
                switch (type)
                {
                    case "userPassword" or "X509" or "symmetricEncryption" or "asymmetricEncryption" or "plain" or "scramSha256" or "scramSha512" or "gssapi":
                        _shape.Only(scheme, pointer, $"A {type} Security Scheme", "type", "description");
                        break;
                    case "apiKey":
                        _shape.Only(scheme, pointer, "An apiKey Security Scheme", "type", "description", "in");
                        Enum(scheme, "in", pointer, required: true, "user", "password");
                        break;
                    case "httpApiKey":
                        _shape.Only(scheme, pointer, "An httpApiKey Security Scheme", "type", "description", "name", "in");
                        _shape.String(scheme, "name", pointer, required: true);
                        Enum(scheme, "in", pointer, required: true, "header", "query", "cookie");
                        break;
                    case "http":
                        var schemeName = _shape.String(scheme, "scheme", pointer, required: true);
                        _shape.Only(scheme, pointer, "An http Security Scheme", schemeName == "bearer" ? ["type", "description", "scheme", "bearerFormat"] : ["type", "description", "scheme"]);
                        _shape.String(scheme, "bearerFormat", pointer);
                        break;
                    case "oauth2":
                        // An oauth2 scheme isn't closed: the schema allows other fields beside these.
                        StringList(scheme, "scopes", pointer, unique: false);
                        Flows(scheme, pointer);
                        break;
                    case "openIdConnect":
                        _shape.Only(scheme, pointer, "An openIdConnect Security Scheme", "type", "description", "openIdConnectUrl", "scopes");
                        _shape.String(scheme, "openIdConnectUrl", pointer, required: true);
                        StringList(scheme, "scopes", pointer, unique: false);
                        break;
                    case null:
                        break;
                    default:
                        _shape.Add(Pointer.Join(pointer, "type"), $"'{type}' isn't a security scheme type AsyncAPI defines.");
                        break;
                }
            }

            private void Flows(JsonObject scheme, string pointer)
            {
                if (!scheme.TryGetPropertyValue("flows", out var node))
                {
                    _shape.Add(pointer, "'flows' is required.");
                    return;
                }

                var at = Pointer.Join(pointer, "flows");
                if (_shape.Object(node, at, "'flows'") is not { } flows)
                {
                    return;
                }

                _shape.Exactly(flows, at, "The OAuth Flows Object", "implicit", "password", "clientCredentials", "authorizationCode");
                foreach (var (name, needs, forbids) in new[]
                {
                    ("implicit", new[] { "authorizationUrl", "availableScopes" }, "tokenUrl"),
                    ("password", new[] { "tokenUrl", "availableScopes" }, "authorizationUrl"),
                    ("clientCredentials", new[] { "tokenUrl", "availableScopes" }, "authorizationUrl"),
                    ("authorizationCode", new[] { "authorizationUrl", "tokenUrl", "availableScopes" }, (string?)null),
                })
                {
                    if (!flows.TryGetPropertyValue(name, out var flowNode))
                    {
                        continue;
                    }

                    var flowAt = Pointer.Join(at, name);
                    if (_shape.Object(flowNode, flowAt, $"The {name} flow") is not { } flow)
                    {
                        continue;
                    }

                    _shape.Only(flow, flowAt, "An OAuth Flow Object", "authorizationUrl", "tokenUrl", "refreshUrl", "availableScopes");
                    Strings(flow, flowAt, "authorizationUrl", "tokenUrl", "refreshUrl");
                    foreach (var field in needs.Where(f => !flow.ContainsKey(f)))
                    {
                        _shape.Add(flowAt, $"The {name} flow needs '{field}'.");
                    }

                    if (forbids is not null && flow.ContainsKey(forbids))
                    {
                        _shape.Add(Pointer.Join(flowAt, forbids), $"The {name} flow has no '{forbids}'.");
                    }

                    if (flow.TryGetPropertyValue("availableScopes", out var scopesNode) && _shape.Object(scopesNode, Pointer.Join(flowAt, "availableScopes"), "'availableScopes'") is { } scopes)
                    {
                        foreach (var scope in scopes.Where(s => !(s.Value is JsonValue v && v.GetValueKind() == JsonValueKind.String)))
                        {
                            _shape.Add(Pointer.Join(Pointer.Join(flowAt, "availableScopes"), scope.Key), "A scope's description must be a string.");
                        }
                    }
                }
            }

            private void Enum(JsonObject owner, string field, string pointer, bool required, params string[] values)
            {
                if (!owner.TryGetPropertyValue(field, out var node))
                {
                    if (required)
                    {
                        _shape.Add(pointer, $"'{field}' is required.");
                    }

                    return;
                }

                if (!(node is JsonValue v && v.GetValueKind() == JsonValueKind.String && Array.IndexOf(values, v.GetValue<string>()) >= 0))
                {
                    _shape.Add(Pointer.Join(pointer, field), $"'{field}' must be one of {string.Join(", ", values)}.");
                }
            }

            public void Channel(JsonObject channel, string pointer)
            {
                _shape.Only(channel, pointer, "A Channel Object", "address", "messages", "parameters", "title", "summary", "description", "servers", "tags", "externalDocs", "bindings");
                if (channel.TryGetPropertyValue("address", out var address) && address is not null && !(address is JsonValue v && v.GetValueKind() == JsonValueKind.String))
                {
                    _shape.Add(Pointer.Join(pointer, "address"), "'address' must be a string or null.");
                }

                Map(channel, "messages", pointer, (n, p) => RefOr(n, p, "A Message Object", Message));
                Map(channel, "parameters", pointer, (n, p) => RefOr(n, p, "A Parameter Object", Parameter, objectsOnly: false));
                Strings(channel, pointer, "title", "summary", "description");
                List(channel, "servers", pointer, unique: true, (item, at) => Reference(item, at, "A server's Reference Object"));
                Tags(channel, pointer);
                ExternalDocs(channel, pointer);
                Bindings(channel, pointer, ServerProtocols);
            }

            private void Parameter(JsonObject parameter, string pointer)
            {
                _shape.Only(parameter, pointer, "A Parameter Object", "enum", "default", "description", "examples", "location");
                StringList(parameter, "enum", pointer, unique: false);
                Strings(parameter, pointer, "default", "description");
                StringList(parameter, "examples", pointer, unique: false);
                Location(parameter, pointer, required: false);
            }

            private void Message(JsonObject message, string pointer) => MessageFields(message, pointer, trait: false);

            private void MessageFields(JsonObject message, string pointer, bool trait)
            {
                var fields = new List<string> { "contentType", "headers", "correlationId", "tags", "summary", "name", "title", "description", "externalDocs", "deprecated", "bindings", "examples" };
                if (!trait)
                {
                    fields.AddRange(["payload", "traits"]);
                }

                var what = trait ? "A Message Trait Object" : "A Message Object";
                _shape.Only(message, pointer, what, [.. fields]);
                Strings(message, pointer, "contentType", "summary", "name", "title", "description");
                if (message.TryGetPropertyValue("headers", out var headers))
                {
                    AnySchema(headers, Pointer.Join(pointer, "headers"));
                }

                if (!trait && message.TryGetPropertyValue("payload", out var payload))
                {
                    AnySchema(payload, Pointer.Join(pointer, "payload"));
                }

                if (message.TryGetPropertyValue("correlationId", out var correlation))
                {
                    RefOr(correlation, Pointer.Join(pointer, "correlationId"), "A Correlation ID Object", CorrelationId);
                }

                Tags(message, pointer);
                ExternalDocs(message, pointer);
                _shape.Boolean(message, "deprecated", pointer);
                Bindings(message, pointer, MessageProtocols);
                List(message, "examples", pointer, unique: false, (item, at) => Example(item, at));
                if (!trait)
                {
                    List(message, "traits", pointer, unique: false, (item, at) =>
                    {
                        // A trait, a Reference to one, or the pair [trait or Reference, object].
                        if (item is JsonArray pair)
                        {
                            if (pair.Count > 0)
                            {
                                RefOr(pair[0], Pointer.Join(at, 0), "A Message Trait Object", (o, p) => MessageFields(o, p, trait: true));
                            }

                            if (pair.Count > 1 && pair[1] is not JsonObject)
                            {
                                _shape.Add(Pointer.Join(at, 1), "A trait's second item must be an object.");
                            }

                            return;
                        }

                        RefOr(item, at, "A Message Trait Object", (o, p) => MessageFields(o, p, trait: true));
                    });
                }
            }

            private void CorrelationId(JsonObject correlation, string pointer)
            {
                _shape.Only(correlation, pointer, "A Correlation ID Object", "location", "description");
                _shape.String(correlation, "description", pointer);
                Location(correlation, pointer, required: true);
            }

            /// <summary>A Message Example Object: a payload, headers (an object), or both; closed, extensions included.</summary>
            private void Example(JsonNode? node, string pointer)
            {
                if (_shape.Object(node, pointer, "A Message Example Object") is not { } example)
                {
                    return;
                }

                _shape.Exactly(example, pointer, "A Message Example Object", "name", "summary", "headers", "payload");
                Strings(example, pointer, "name", "summary");
                if (!example.ContainsKey("payload") && !example.ContainsKey("headers"))
                {
                    _shape.Add(pointer, "An example has a 'payload', 'headers', or both.");
                }

                if (example.TryGetPropertyValue("headers", out var headers) && headers is not JsonObject)
                {
                    _shape.Add(Pointer.Join(pointer, "headers"), "'headers' must be an object.");
                }
            }

            /// <summary>An AsyncAPI Schema Object, or a Multi Format Schema Object (it has <c>schema</c>).</summary>
            private void AnySchema(JsonNode? node, string pointer)
            {
                if (node is JsonObject multi && multi.ContainsKey("schema"))
                {
                    var format = _shape.String(multi, "schemaFormat", pointer);
                    var schemaAt = Pointer.Join(pointer, "schema");
                    if (!multi.ContainsKey("schemaFormat") || format is not null && Array.IndexOf(JsonSchemaFormats, format) >= 0)
                    {
                        JsonSchemaShape.Check(_shape, multi["schema"], schemaAt, SchemaDialect.AsyncApi);
                    }
                    else if (multi["schema"] is not JsonObject other)
                    {
                        // Another format's schema (OpenAPI, Avro, RAML) isn't checked here, but it is an object, or a Reference to one.
                        _shape.Add(schemaAt, "A schema in another format must be an object, or a Reference Object.");
                    }
                    else if (other.ContainsKey("$ref"))
                    {
                        _shape.String(other, "$ref", schemaAt);
                    }

                    return;
                }

                JsonSchemaShape.Check(_shape, node, pointer, SchemaDialect.AsyncApi);
            }

            public (string Action, string Channel)? Operation(JsonObject operation, string pointer) => OperationFields(operation, pointer, trait: false);

            private (string Action, string Channel)? OperationFields(JsonObject operation, string pointer, bool trait)
            {
                var fields = new List<string> { "title", "summary", "description", "security", "tags", "externalDocs", "bindings" };
                if (!trait)
                {
                    fields.AddRange(["action", "channel", "messages", "reply", "traits"]);
                }

                _shape.Only(operation, pointer, trait ? "An Operation Trait Object" : "An Operation Object", [.. fields]);
                Strings(operation, pointer, "title", "summary", "description");
                Security(operation, pointer);
                Tags(operation, pointer);
                ExternalDocs(operation, pointer);
                Bindings(operation, pointer, MessageProtocols);
                if (trait)
                {
                    return null;
                }

                Enum(operation, "action", pointer, required: true, "send", "receive");
                string? channel = null;
                if (!operation.TryGetPropertyValue("channel", out var channelNode))
                {
                    _shape.Add(pointer, "'channel' is required.");
                }
                else
                {
                    channel = Reference(channelNode, Pointer.Join(pointer, "channel"), "'channel'");
                }

                List(operation, "messages", pointer, unique: false, (item, at) => Reference(item, at, "A message's Reference Object"));
                if (operation.TryGetPropertyValue("reply", out var reply))
                {
                    RefOr(reply, Pointer.Join(pointer, "reply"), "An Operation Reply Object", Reply);
                }

                List(operation, "traits", pointer, unique: false, (item, at) => RefOr(item, at, "An Operation Trait Object", (o, p) => OperationFields(o, p, trait: true)));
                var action = operation["action"] is JsonValue a && a.GetValueKind() == JsonValueKind.String ? a.GetValue<string>() : null;
                return action is "send" or "receive" && channel is not null ? (action, channel) : null;
            }

            private void Reply(JsonObject reply, string pointer)
            {
                _shape.Only(reply, pointer, "An Operation Reply Object", "address", "channel", "messages");
                if (reply.TryGetPropertyValue("address", out var address))
                {
                    RefOr(address, Pointer.Join(pointer, "address"), "An Operation Reply Address Object", ReplyAddress);
                }

                if (reply.TryGetPropertyValue("channel", out var channel))
                {
                    Reference(channel, Pointer.Join(pointer, "channel"), "'channel'");
                }

                List(reply, "messages", pointer, unique: false, (item, at) => Reference(item, at, "A message's Reference Object"));
            }

            private void ReplyAddress(JsonObject address, string pointer)
            {
                _shape.Only(address, pointer, "An Operation Reply Address Object", "location", "description");
                _shape.String(address, "description", pointer);
                Location(address, pointer, required: true);
            }

            public void Components(JsonNode? node, string pointer)
            {
                if (_shape.Object(node, pointer, "'components'") is not { } components)
                {
                    return;
                }

                _shape.Only(components, pointer, "The Components Object", "schemas", "servers", "channels", "serverVariables", "operations", "messages", "securitySchemes", "parameters", "correlationIds",
                    "operationTraits", "messageTraits", "replies", "replyAddresses", "serverBindings", "channelBindings", "operationBindings", "messageBindings", "tags", "externalDocs");
                Map(components, "schemas", pointer, AnySchema, ComponentName);
                Map(components, "servers", pointer, (n, p) => RefOr(n, p, "A Server Object", Server), ComponentName);
                Map(components, "channels", pointer, (n, p) => RefOr(n, p, "A Channel Object", Channel), ComponentName);
                Map(components, "serverVariables", pointer, (n, p) => RefOr(n, p, "A Server Variable Object", ServerVariable), ComponentName);
                Map(components, "operations", pointer, (n, p) => RefOr(n, p, "An Operation Object", (o, at) => Operation(o, at)), ComponentName);
                Map(components, "messages", pointer, (n, p) => RefOr(n, p, "A Message Object", Message), ComponentName);
                Map(components, "securitySchemes", pointer, (n, p) => RefOr(n, p, "A Security Scheme Object", SecurityScheme), ComponentName);
                Map(components, "parameters", pointer, (n, p) => RefOr(n, p, "A Parameter Object", Parameter, objectsOnly: false), ComponentName);
                Map(components, "correlationIds", pointer, (n, p) => RefOr(n, p, "A Correlation ID Object", CorrelationId), ComponentName);
                Map(components, "operationTraits", pointer, (n, p) => RefOr(n, p, "An Operation Trait Object", (o, at) => OperationFields(o, at, trait: true)), ComponentName);
                Map(components, "messageTraits", pointer, (n, p) => RefOr(n, p, "A Message Trait Object", (o, at) => MessageFields(o, at, trait: true)), ComponentName);
                Map(components, "replies", pointer, (n, p) => RefOr(n, p, "An Operation Reply Object", Reply), ComponentName);
                Map(components, "replyAddresses", pointer, (n, p) => RefOr(n, p, "An Operation Reply Address Object", ReplyAddress), ComponentName);
                Map(components, "serverBindings", pointer, (n, p) => RefOr(n, p, "A Bindings Object", (o, at) => BindingsObject(o, at, ServerProtocols)), ComponentName);
                Map(components, "channelBindings", pointer, (n, p) => RefOr(n, p, "A Bindings Object", (o, at) => BindingsObject(o, at, ServerProtocols)), ComponentName);
                Map(components, "operationBindings", pointer, (n, p) => RefOr(n, p, "A Bindings Object", (o, at) => BindingsObject(o, at, MessageProtocols)), ComponentName);
                Map(components, "messageBindings", pointer, (n, p) => RefOr(n, p, "A Bindings Object", (o, at) => BindingsObject(o, at, MessageProtocols)), ComponentName);
                Map(components, "tags", pointer, (n, p) => RefOr(n, p, "A Tag Object", (tag, at) =>
                {
                    _shape.Only(tag, at, "A Tag Object", "name", "description", "externalDocs");
                    _shape.String(tag, "name", at, required: true);
                    _shape.String(tag, "description", at);
                    ExternalDocs(tag, at);
                }), ComponentName);
                Map(components, "externalDocs", pointer, (n, p) => RefOr(n, p, "An External Documentation Object", (docs, at) =>
                {
                    _shape.Only(docs, at, "An External Documentation Object", "url", "description");
                    _shape.String(docs, "url", at, required: true);
                    _shape.String(docs, "description", at);
                }), ComponentName);
            }
        }
    }
}
