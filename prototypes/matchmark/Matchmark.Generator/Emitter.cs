using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.OpenApi;

namespace Matchmark.Generator
{
    /// <summary>
    /// Turns one OpenAPI document into C# source. Prototype scope: component schemas that are objects, string enums or
    /// discriminated oneOf unions; JSON bodies; path, query and header parameters.
    /// </summary>
    internal sealed partial class Emitter
    {
        /// <summary>Component schemas that map onto framework types instead of being generated.</summary>
        private static readonly Dictionary<string, string> WellKnownSchemas = new()
        {
            ["ProblemDetails"] = "global::Microsoft.AspNetCore.Mvc.ProblemDetails",
            ["HttpValidationProblemDetails"] = "global::Microsoft.AspNetCore.Http.HttpValidationProblemDetails",
        };

        private const string ValidationSchema = "HttpValidationProblemDetails";

        private const string FormMedia = "application/x-www-form-urlencoded";

        /// <summary>The framework types the schemas map onto: none when the contract owns its problem types.</summary>
        private Dictionary<string, string> Mapped => _ownProblems ? NoMapping : WellKnownSchemas;

        private static readonly Dictionary<string, string> NoMapping = new();

        /// <summary>
        /// MatchmarkProblems="Owned": the spec's problem schemas are generated like any other, so the contract needs no
        /// ASP.NET Core (a client-only package). The Server part writes ASP.NET Core's problem results, so it needs them.
        /// </summary>
        private readonly bool _ownProblems;

        private readonly OpenApiDocument _document;
        private readonly string _namespace;
        private readonly string _apiName;
        private readonly Location _location;

        /// <summary>Generated component schemas by name.</summary>
        private readonly Dictionary<string, IOpenApiSchema> _schemas = new(StringComparer.Ordinal);

        /// <summary>Component schemas that are a primitive or an array: a $ref to one is the type it stands for, not a class.</summary>
        private readonly Dictionary<string, IOpenApiSchema> _aliases = new(StringComparer.Ordinal);

        /// <summary>Inline objects (and allOfs) given generated types, by the schema instance, with the name chosen for each.</summary>
        private readonly Dictionary<IOpenApiSchema, string> _inline = new(Identity.Instance);

        private bool _collected;
        private readonly HashSet<IOpenApiSchema> _loose = new(Identity.Instance);

        /// <summary>Union member schema → (base type, discriminator property, discriminator value).</summary>
        private readonly Dictionary<string, (string Base, string Property, string Value)> _unionMembers = new(StringComparer.Ordinal);

        private readonly bool _describesItself;
        private readonly bool _hasFakes;

        /// <summary>Matchmark.Rules is referenced, so x-matchmark-rules and x-dependencies are enforced.</summary>
        private readonly bool _hasRules;

        /// <summary>The webhooks' API: its models are the main API's, so it doesn't write them again.</summary>
        private bool _shared;

        private bool _rulesReported;

        public Emitter(OpenApiDocument document, string ns, string apiName, Location location, bool describesItself = false, bool hasFakes = false,
            bool hasRules = false, bool ownProblems = false)
        {
            _ownProblems = ownProblems;
            _hasRules = hasRules;
            _describesItself = describesItself;
            _hasFakes = hasFakes;
            _document = document;
            _namespace = ns;
            _apiName = apiName;
            _location = location;
        }

        public List<Diagnostic> Diagnostics { get; } = new();

        private string ValidationClass => _apiName + "Validation";

        public Dictionary<string, string> Emit(bool contract = true, bool server = true, bool fakeServer = false, bool regression = false,
            IReadOnlyList<string>? workflows = null)
        {
            CollectSchemas();
            var operations = CollectOperations();
            var files = new Dictionary<string, string>();
            if (_ownProblems && (server || fakeServer))
            {
                Diagnostics.Add(Diagnostic.Create(Generator.Diagnostics.OwnedProblemsNeedNoServer, _location));
                server = fakeServer = false;
            }

            if (contract)
            {
                if (!_shared)
                {
                    files["Models"] = EmitModels();
                }

                files["Validation"] = EmitValidation(operations);
                if (EmitValidationResolver(operations) is { } resolver)
                {
                    files["ValidationResolver"] = resolver;
                }
                files["Responses"] = EmitResponses(operations);
                if (EmitOutcomes() is { } outcomes)
                {
                    files["Outcomes"] = outcomes;
                }
                files["Handlers"] = EmitHandlers(operations);
                files["Client"] = EmitClient(operations);
                if (EmitInvariants(operations) is { } invariants)
                {
                    files["Invariants"] = invariants;
                }
                if (_hasFakes)
                {
                    files["Fake"] = EmitFake(operations);
                }
            }

            if (server)
            {
                files["Endpoints"] = EmitEndpoints(operations);
                if (EmitInvariantsForEntityFramework() is { } entityFramework)
                {
                    files["Invariants.EntityFramework"] = entityFramework;
                }
                if (_describesItself)
                {
                    files["OpenApi"] = EmitOpenApi();
                }
            }

            if (fakeServer && _hasFakes)
            {
                files["FakeHosting"] = EmitFakeHosting(operations);
            }

            if (regression)
            {
                files["Regression"] = EmitRegression(workflows ?? Array.Empty<string>());
            }

            // Webhooks are operations the consumer implements: generated as an API of their own, {Api}Webhooks, from
            // the same components. Its handler interface and Map{Api}Webhooks are the typed receiver; its client sends
            // events (checking each against the spec); its fake records what a sender sent.
            if (_document.Webhooks is { Count: > 0 } webhooks && (contract || server))
            {
                var paths = new OpenApiPaths();
                foreach (var webhook in webhooks)
                {
                    paths["/" + webhook.Key] = webhook.Value;
                }

                var receiver = new Emitter(new OpenApiDocument { Info = _document.Info, Components = _document.Components, Paths = paths },
                    _namespace, _apiName + "Webhooks", _location, describesItself: false, _hasFakes, _hasRules, _ownProblems) { _shared = true, PlainOutcomes = PlainOutcomes };
                foreach (var file in receiver.Emit(contract, server))
                {
                    files["Webhooks." + file.Key] = file.Value;
                }

                // An AsyncAPI document's operations on a broker (with Matchmark.Messaging): a typed consumer and producer.
                if (Messaging && contract && receiver.EmitMessaging(_apiName, webhooks) is { } messaging)
                {
                    files["Messaging"] = messaging;
                }

                Diagnostics.AddRange(receiver.Diagnostics.Where(d => !Diagnostics.Any(e => e.Id == d.Id && e.GetMessage(CultureInfo.InvariantCulture) == d.GetMessage(CultureInfo.InvariantCulture))));
            }

            // Callbacks: an operation's requests back to a URL its caller gave (subscribe, then receive). Generated like
            // webhooks, as {Api}Callbacks, each callback at /{name}: a typed receiver for the caller, a sender for the API.
            var callbacks = new OpenApiPaths();
            foreach (var item in _document.Paths?.Values ?? Enumerable.Empty<IOpenApiPathItem>())
            {
                foreach (var operation in item.Operations?.Values ?? Enumerable.Empty<OpenApiOperation>())
                {
                    foreach (var callback in operation.Callbacks ?? new Dictionary<string, IOpenApiCallback>())
                    {
                        if (callback.Value.PathItems?.Values.FirstOrDefault() is { } target && !callbacks.ContainsKey("/" + callback.Key))
                        {
                            callbacks["/" + callback.Key] = target;
                        }
                    }
                }
            }

            if (callbacks.Count > 0 && (contract || server))
            {
                var receiver = new Emitter(new OpenApiDocument { Info = _document.Info, Components = _document.Components, Paths = callbacks },
                    _namespace, _apiName + "Callbacks", _location, describesItself: false, _hasFakes, _hasRules, _ownProblems) { _shared = true, PlainOutcomes = PlainOutcomes };
                foreach (var file in receiver.Emit(contract, server))
                {
                    files["Callbacks." + file.Key] = file.Value;
                }

                Diagnostics.AddRange(receiver.Diagnostics.Where(d => !Diagnostics.Any(e => e.Id == d.Id && e.GetMessage(CultureInfo.InvariantCulture) == d.GetMessage(CultureInfo.InvariantCulture))));
            }

            return files;
        }
    }
}
