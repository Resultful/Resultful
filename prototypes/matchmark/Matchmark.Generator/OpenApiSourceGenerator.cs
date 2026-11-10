using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Microsoft.OpenApi.Reader;

namespace Matchmark.Generator
{
    /// <summary>
    /// Reads every AdditionalFiles item marked <c>Matchmark="true"</c> and generates, from the OpenAPI document,
    /// the DTOs, the Resultful-shaped handler interfaces, request validation and the Minimal API endpoints.
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class OpenApiSourceGenerator : IIncrementalGenerator
    {
        private const string MetadataPrefix = "build_metadata.AdditionalFiles.";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var specs = context.AdditionalTextsProvider
                .Combine(context.AnalyzerConfigOptionsProvider)
                .Select(static (pair, ct) => ReadSpec(pair.Left, pair.Right, ct))
                .Where(static spec => spec is not null)
                .Select(static (spec, _) => spec!)
                .WithTrackingName(TrackingNames.Specs);

            // The OpenAPI document configuration is only emitted when the app describes itself with
            // Microsoft.AspNetCore.OpenApi; without it the generated code would not compile.
            // Likewise the fake, and fake-server hosting, need the Matchmark.Fakes runtime.
            var references = context.CompilationProvider
                .Select(static (compilation, _) => (
                    DescribesItself: compilation.GetTypeByMetadataName("Microsoft.AspNetCore.OpenApi.OpenApiOptions") is not null,
                    HasFakes: compilation.GetTypeByMetadataName("Matchmark.Fakes.Scenario") is not null,
                    HasRegression: compilation.GetTypeByMetadataName("Matchmark.Testing.RegressionSuite") is not null,
                    HasRules: compilation.GetTypeByMetadataName("Matchmark.Rules.Cel") is not null,
                    HasEntityFramework: compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.ModelBuilder") is not null,
                    HasValidation: compilation.GetTypeByMetadataName("Microsoft.Extensions.Validation.IValidatableInfoResolver") is not null,
                    HasValidationAttributes: compilation.GetTypeByMetadataName("Matchmark.Validation.MultipleOfAttribute") is not null))
                .WithTrackingName(TrackingNames.References);

            // Arazzo documents for the Regression part (MatchmarkArazzo), and Overlays applied to the spec first (MatchmarkOverlay):
            // AdditionalFiles joined to a spec by MatchmarkApiName.
            var arazzo = Companions(context, "MatchmarkArazzo");
            var overlays = Companions(context, "MatchmarkOverlay");
            // The other files of a spec split across them (question 2, option a): MatchmarkSpecPart, joined by MatchmarkApiName.
            var specParts = Companions(context, "MatchmarkSpecPart");

            // A contract compiled in a referenced project with a resolver for .NET's validation layer (Q45): the server part
            // then registers it. Looked up by the spec's own type name, so only a bool reaches the output step.
            var referencedResolvers = specs.Combine(context.CompilationProvider)
                .Select(static (pair, _) => (pair.Left.ApiName, Has: pair.Right.GetTypeByMetadataName($"{pair.Left.Namespace}.{pair.Left.ApiName}ValidationResolver") is not null))
                .WithTrackingName(TrackingNames.ReferencedResolvers)
                .Collect();

            context.RegisterSourceOutput(specs.Combine(references).Combine(arazzo).Combine(overlays).Combine(referencedResolvers).Combine(specParts), static (spc, withParts) =>
            {
                var all = withParts.Left;
                var pair = all.Left;
                Generate(spc, Bundled(With(pair.Left.Left.Left, pair.Right), withParts.Right), pair.Left.Left.Right.DescribesItself, pair.Left.Left.Right.HasFakes,
                    pair.Left.Left.Right.HasRegression, pair.Left.Right.Where(a => a.ApiName == pair.Left.Left.Left.ApiName).ToList(), pair.Left.Left.Right.HasRules,
                    pair.Left.Left.Right.HasEntityFramework, pair.Left.Left.Right.HasValidation, pair.Left.Left.Right.HasValidationAttributes,
                    all.Right.Any(r => r.ApiName == pair.Left.Left.Left.ApiName && r.Has));
            });

            // Each tag's endpoints from that tag's slice of the spec (question 4): an edit to one tag's operations changes
            // only its slice, so Roslyn reuses the other tags' output.
            var slices = specs.Combine(overlays).Combine(specParts)
                .SelectMany(static (pair, ct) => Slices(Bundled(With(pair.Left.Left, pair.Left.Right), pair.Right)))
                .WithTrackingName(TrackingNames.TagSlices);
            context.RegisterSourceOutput(slices.Combine(references), static (spc, pair) =>
                GenerateTag(spc, pair.Left, pair.Right.DescribesItself, pair.Right.HasFakes, pair.Right.HasRules, pair.Right.HasValidation, pair.Right.HasValidationAttributes));

            // The Verify part: check the project's hand-written endpoints (controllers, Minimal API Map calls) against the spec.
            var actions = context.SyntaxProvider
                .CreateSyntaxProvider(static (node, _) => CoverageVerifier.IsCandidateAction(node), static (ctx, ct) => CoverageVerifier.ReadAction(ctx, ct).ToImmutableArray())
                .SelectMany(static (endpoints, _) => endpoints);
            var maps = context.SyntaxProvider
                .CreateSyntaxProvider(static (node, _) => CoverageVerifier.IsCandidateMap(node), static (ctx, ct) => CoverageVerifier.ReadMap(ctx, ct).ToImmutableArray())
                .SelectMany(static (endpoints, _) => endpoints);
            var endpoints = actions.Collect().Combine(maps.Collect())
                .Select(static (pair, _) => pair.Left.AddRange(pair.Right));
            // The parse step (model.md, its shape in the pipeline): the spec, with its overlays and parts, read once into an
            // equatable ApiShape. Verify reads the shape, so an edit to a controller re-runs only the comparison, and an edit
            // to the spec that leaves its shape equal re-runs nothing after this step.
            var shapes = specs.Where(static spec => spec.Parts.Split(',', ';').Any(p => p.Trim().Equals("Verify", StringComparison.OrdinalIgnoreCase)))
                .Combine(overlays).Combine(specParts)
                .Select(static (pair, _) => Parse(Bundled(With(pair.Left.Left, pair.Left.Right), pair.Right)))
                .WithTrackingName(TrackingNames.Shapes);
            context.RegisterSourceOutput(shapes.Combine(endpoints), static (spc, pair) => Verify(spc, pair.Left, pair.Right));
        }

        /// <summary>Each Arazzo document as JSON, for the regression suite; one that can't be read is a warning.</summary>
        private static List<string> Workflows(SourceProductionContext context, IReadOnlyList<(string ApiName, string Path, string Content)> arazzo)
        {
            var json = new List<string>();
            foreach (var (_, path, content) in arazzo)
            {
                try
                {
                    json.Add(SpecJson.Arazzo(content, path));
                }
#pragma warning disable CA1031 // An unreadable document is reported, never thrown into the compiler.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    context.ReportDiagnostic(Diagnostic.Create(Diagnostics.UnreadableArazzo, Location.Create(path, default, default), path, ex.Message));
                }
            }

            return json;
        }

        private static IncrementalValueProvider<ImmutableArray<(string ApiName, string Path, string Content)>> Companions(IncrementalGeneratorInitializationContext context, string flag)
            => context.AdditionalTextsProvider
                .Combine(context.AnalyzerConfigOptionsProvider)
                .Select((pair, ct) => ReadCompanion(pair.Left, pair.Right, flag, ct))
                .Where(static a => a is not null)
                .Select(static (a, _) => a!.Value)
                .Collect();

        /// <summary>Whether a spec's server endpoints are emitted per tag: a server part, OpenAPI, and a server that isn't turned off.</summary>
        private static bool Splittable(SpecInput spec)
            => spec.Parts.Split(',', ';').Any(p => p.Trim().Equals("Server", StringComparison.OrdinalIgnoreCase)) && !spec.OwnProblems
               && spec.SliceTag is null && !AsyncApi.IsAsyncApi(spec.Content);

        /// <summary>A spec cut into one slice per tag: the tag's operations, with every component, as JSON (overlays applied).</summary>
        private static ImmutableArray<SpecInput> Slices(SpecInput spec)
        {
            if (!Splittable(spec))
            {
                return ImmutableArray<SpecInput>.Empty;
            }

            JsonObject document;
            try
            {
                document = JsonNode.Parse(Overlay.Apply(spec.Content, spec.Path, spec.Overlays)) as JsonObject ?? new JsonObject();
            }
            catch (Exception ex) when (ex is InvalidOperationException or SharpYaml.YamlException or System.Text.Json.JsonException)
            {
                return ImmutableArray<SpecInput>.Empty;
            }

            var methods = new[] { "get", "put", "post", "delete", "options", "head", "patch", "trace", "query" };
            var tags = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var path in document["paths"] as JsonObject ?? new JsonObject())
            {
                foreach (var method in methods)
                {
                    if (path.Value?[method] is JsonObject operation)
                    {
                        tags.Add(Tag(operation));
                    }
                }
            }

            var slices = ImmutableArray.CreateBuilder<SpecInput>();
            foreach (var tag in tags)
            {
                var slice = (JsonObject)document.DeepClone();
                var paths = new JsonObject();
                foreach (var path in document["paths"] as JsonObject ?? new JsonObject())
                {
                    if (path.Value is not JsonObject item)
                    {
                        continue;
                    }

                    var kept = (JsonObject)item.DeepClone();
                    foreach (var method in methods.Where(m => item[m] is JsonObject o && Tag(o) != tag))
                    {
                        kept.Remove(method);
                    }

                    if (methods.Any(m => kept[m] is not null))
                    {
                        paths[path.Key] = kept;
                    }
                }

                slice["paths"] = paths;
                slice.Remove("webhooks");
                Prune(slice);
                slices.Add(spec.Sliced(slice.ToJsonString(), tag));
            }

            return slices.ToImmutable();
        }

        /// <summary>
        /// The slice's components cut to those its operations reach (model.md: a TagShape holds the component names it uses),
        /// so an edit to a component re-emits only the tags that use it. Reached means referred to, directly or through
        /// another reached component (a $ref or a discriminator mapping), or a schema that extends a reached one with allOf,
        /// since a union's implicit members are found that way. Security schemes are kept whole.
        /// </summary>
        private static void Prune(JsonObject slice)
        {
            if (slice["components"] is not JsonObject components)
            {
                return;
            }

            const string Prefix = "#/components/";
            var reached = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<JsonNode?>();
            pending.Push(slice["paths"]);
            void Visit()
            {
                while (pending.Count > 0)
                {
                    switch (pending.Pop())
                    {
                        case JsonObject o:
                            foreach (var property in o)
                            {
                                pending.Push(property.Value);
                            }

                            break;
                        case JsonArray a:
                            foreach (var item in a)
                            {
                                pending.Push(item);
                            }

                            break;
                        case JsonValue v when v.TryGetValue<string>(out var text) && text.StartsWith(Prefix, StringComparison.Ordinal):
                            var key = text.Substring(Prefix.Length);
                            var parts = key.Split('/');
                            if (parts.Length >= 2 && reached.Add(parts[0] + "/" + parts[1]) && components[parts[0]]?[parts[1]] is { } target)
                            {
                                pending.Push(target);
                            }

                            break;
                    }
                }
            }

            Visit();
            for (var grew = true; grew;)
            {
                // A schema that extends a reached one is reached too: it may be a member of the reached union.
                grew = false;
                foreach (var schema in components["schemas"] as JsonObject ?? new JsonObject())
                {
                    var name = "schemas/" + schema.Key;
                    if (!reached.Contains(name) && (schema.Value?["allOf"] as JsonArray ?? new JsonArray())
                            .Any(m => m?["$ref"]?.GetValue<string>() is { } r && reached.Contains(r.Substring(Prefix.Length))))
                    {
                        reached.Add(name);
                        pending.Push(schema.Value);
                        grew = true;
                    }
                }

                Visit();
            }

            foreach (var kind in components.Where(k => k.Key != "securitySchemes" && k.Value is JsonObject).Select(k => k.Key).ToList())
            {
                var section = (JsonObject)components[kind]!;
                foreach (var name in section.Select(e => e.Key).Where(name => !reached.Contains(kind + "/" + name)).ToList())
                {
                    section.Remove(name);
                }
            }
        }

        private static string Tag(JsonObject operation) => (operation["tags"] as JsonArray)?.FirstOrDefault()?.GetValue<string>() ?? string.Empty;

        /// <summary>One tag's endpoints, from its slice: added as {Api}.Endpoints.{Tag}.g.cs; the slice's diagnostics are the whole spec's, reported once by Generate.</summary>
        private static void GenerateTag(SourceProductionContext context, SpecInput slice, bool describesItself, bool hasFakes, bool hasRules, bool hasValidation, bool hasValidationAttributes)
        {
            if (SpecReader.Read(slice.Content, slice.Path, out _) is not { } document)
            {
                return;
            }

            var emitter = new Emitter(document, slice.Namespace, slice.ApiName, Location.None, describesItself, hasFakes, hasRules, slice.OwnProblems)
            {
                NetValidation = hasValidation,
                ValidationAttributes = hasValidationAttributes,
                PlainOutcomes = !slice.Resultful,
                ClientOnNet = slice.ClientOnNet,
            };
            var name = Naming.Pascal(string.IsNullOrEmpty(slice.SliceTag) ? slice.ApiName : slice.SliceTag!);
            context.AddSource($"{slice.ApiName}.Endpoints.{name}.g.cs", SourceText.From(emitter.EmitTagEndpoints(), System.Text.Encoding.UTF8));
        }

        /// <summary>
        /// A spec that refers to other files, made one from its parts (MatchmarkSpecPart, for its API name), so it's read as
        /// matchmark bundle would write it; unchanged when it refers to none, or has no parts.
        /// </summary>
        private static SpecInput Bundled(SpecInput spec, ImmutableArray<(string ApiName, string Path, string Content)> parts)
        {
            var mine = parts.Where(p => p.ApiName == spec.ApiName).ToList();
            if (mine.Count == 0 || !SpecBundle.Split(spec.Content))
            {
                return spec;
            }

            var root = SpecBundle.Normalize(spec.Path);
            var files = mine.ToDictionary(p => SpecBundle.Normalize(p.Path), p => p.Content, StringComparer.Ordinal);
            files[root] = spec.Content;
            var bundled = SpecBundle.Bundle(root, file => files.TryGetValue(file, out var text) ? text : null);
            return spec.WithContent(bundled.ToJsonString(), System.IO.Path.ChangeExtension(spec.Path, ".bundled.json"));
        }

        /// <summary>The spec with its overlays (those for its API name), applied when it's read.</summary>
        private static SpecInput With(SpecInput spec, ImmutableArray<(string ApiName, string Path, string Content)> overlays)
            => spec.WithOverlays(overlays.Where(o => o.ApiName == spec.ApiName).Select(o => (o.Content, o.Path)).ToList());

        private static (string ApiName, string Path, string Content)? ReadCompanion(AdditionalText text, AnalyzerConfigOptionsProvider options, string flag, System.Threading.CancellationToken ct)
        {
            var fileOptions = options.GetOptions(text);
            if (!fileOptions.TryGetValue(MetadataPrefix + flag, out var enabled) || !string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase)
                || !fileOptions.TryGetValue(MetadataPrefix + "MatchmarkApiName", out var apiName) || string.IsNullOrWhiteSpace(apiName))
            {
                return null;
            }

            return (apiName, text.Path, text.GetText(ct)?.ToString() ?? string.Empty);
        }

        private static SpecInput? ReadSpec(AdditionalText text, AnalyzerConfigOptionsProvider options, System.Threading.CancellationToken ct)
        {
            var fileOptions = options.GetOptions(text);
            if (!fileOptions.TryGetValue(MetadataPrefix + "Matchmark", out var enabled)
                || !string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var content = text.GetText(ct)?.ToString() ?? string.Empty;
            var fileName = Path.GetFileNameWithoutExtension(text.Path);

            if (!fileOptions.TryGetValue(MetadataPrefix + "MatchmarkNamespace", out var ns) || string.IsNullOrWhiteSpace(ns))
            {
                options.GlobalOptions.TryGetValue("build_property.RootNamespace", out ns);
            }

            if (!fileOptions.TryGetValue(MetadataPrefix + "MatchmarkApiName", out var apiName) || string.IsNullOrWhiteSpace(apiName))
            {
                apiName = Naming.Pascal(fileName);
            }

            // Contract: types, unions, handler interfaces, validation, the client and (with Matchmark.Fakes) the fake.
            // Server: endpoints and the OpenAPI configuration. FakeServer: hosting the fake as a server (hub, control API,
            // PetStoreFakeServer). A contract library takes "Contract", the service "Server", a fake server "Server,FakeServer".
            // Separate parts with commas: the value reaches the generator through an .editorconfig, where ';' starts a comment.
            fileOptions.TryGetValue(MetadataPrefix + "MatchmarkParts", out var parts);

            // MatchmarkAudit="true": what the spec leaves out (matchmark audit's static rules), one MM024 each.
            var audit = fileOptions.TryGetValue(MetadataPrefix + "MatchmarkAudit", out var audited) && string.Equals(audited, "true", StringComparison.OrdinalIgnoreCase);

            // MatchmarkProblems="Owned": the contract generates its own problem types, so it needs no ASP.NET Core.
            var owned = fileOptions.TryGetValue(MetadataPrefix + "MatchmarkProblems", out var problems) && string.Equals(problems, "Owned", StringComparison.OrdinalIgnoreCase);

            // MatchmarkOutcomes="Resultful": outcomes on Resultful and OneOf, the adapter; plain ones of the contract's own by default (Q12).
            var resultful = fileOptions.TryGetValue(MetadataPrefix + "MatchmarkOutcomes", out var outcomes) && string.Equals(outcomes, "Resultful", StringComparison.OrdinalIgnoreCase);

            // MatchmarkClientValidation="Net": the client checks requests on .NET's validation layer (question 3).
            var clientOnNet = fileOptions.TryGetValue(MetadataPrefix + "MatchmarkClientValidation", out var clientValidation) && string.Equals(clientValidation, "Net", StringComparison.OrdinalIgnoreCase);
            return new SpecInput(text.Path, content, string.IsNullOrWhiteSpace(ns) ? "Generated" : ns!, apiName!,
                string.IsNullOrWhiteSpace(parts) ? "Contract,Server" : parts!, audit) { OwnProblems = owned, Resultful = resultful, ClientOnNet = clientOnNet };
        }

        /// <summary>The spec's shape, or null when it can't be read (Generate reports that, with what the reader objected to).</summary>
        internal static ApiShape? Parse(SpecInput spec)
            => SpecReader.Read(spec.Content, spec.Path, out _, spec.Overlays) is { } document
                ? new Emitter(document, spec.Namespace, spec.ApiName, Location.None).Shape(spec.Path)
                : null;

        private static void Verify(SourceProductionContext context, ApiShape? spec, ImmutableArray<Endpoint> endpoints)
        {
            if (spec is null)
            {
                return; // Generate reports the unreadable spec.
            }

            foreach (var diagnostic in CoverageVerifier.Verify(spec, endpoints, Location.Create(spec.Path, default, default)))
            {
                context.ReportDiagnostic(diagnostic);
            }
        }

        private static void Generate(SourceProductionContext context, SpecInput spec, bool describesItself, bool hasFakes, bool hasRegression,
            IReadOnlyList<(string ApiName, string Path, string Content)> arazzo, bool hasRules = false, bool hasEntityFramework = false, bool hasValidation = false, bool hasValidationAttributes = false, bool referencedResolver = false)
        {
            var location = Location.Create(spec.Path, default, default);
            var document = SpecReader.Read(spec.Content, spec.Path, out var problems, spec.Overlays);
            if (document is null)
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.InvalidSpec, location, spec.Path, string.Join("; ", problems)));
                return;
            }

            foreach (var problem in problems)
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.ReaderProblem, location, spec.Path, problem));
            }

            var emitter = new Emitter(document, spec.Namespace, spec.ApiName, location, describesItself, hasFakes, hasRules, spec.OwnProblems) { EntityFramework = hasEntityFramework, NetValidation = hasValidation, ValidationAttributes = hasValidationAttributes, ReferencedResolver = referencedResolver, PlainOutcomes = !spec.Resultful, ClientOnNet = spec.ClientOnNet, SplitByTag = Splittable(spec) };
            var parts = spec.Parts.Split(',', ';').Select(p => p.Trim()).ToList();
            if (parts.Contains("Server", StringComparer.OrdinalIgnoreCase) && !hasValidation && !referencedResolver)
            {
                // No .NET validation layer to register: the server keeps Matchmark's own validator alone (validation.md).
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.NetValidationMissing, location));
            }

            var files = emitter.Emit(
                contract: parts.Contains("Contract", StringComparer.OrdinalIgnoreCase),
                server: parts.Contains("Server", StringComparer.OrdinalIgnoreCase),
                fakeServer: parts.Contains("FakeServer", StringComparer.OrdinalIgnoreCase),
                regression: parts.Contains("Regression", StringComparer.OrdinalIgnoreCase) && hasRegression,
                workflows: Workflows(context, arazzo));
            if (parts.Contains("Regression", StringComparer.OrdinalIgnoreCase) && !hasRegression)
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.RegressionNeedsRuntime, location, spec.Path));
            }

            foreach (var diagnostic in emitter.Diagnostics)
            {
                context.ReportDiagnostic(diagnostic);
            }

            if (spec.Audit)
            {
                var documents = new List<string>();
                foreach (var (_, path, content) in arazzo)
                {
                    try
                    {
                        documents.Add(SpecJson.Arazzo(content, path));
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or SharpYaml.YamlException or System.Text.Json.JsonException)
                    {
                        // Reported as MM008 by the Regression part; the audit goes on without it.
                    }
                }

                foreach (var finding in Audit.Run(SpecJson.Write(document), documents.ToArray()))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Diagnostics.Incomplete, location,
                        finding.Subject, finding.Message, finding.Rule.Id, finding.Rule.Unlocks, "#" + finding.Location));
                }
            }

            foreach (var file in files)
            {
                context.AddSource($"{spec.ApiName}.{file.Key}.g.cs", SourceText.From(file.Value, System.Text.Encoding.UTF8));
            }
        }
    }

    /// <summary>The generator's named pipeline steps, for tests that check what an edit re-runs (Roslyn's step tracking).</summary>
    public static class TrackingNames
    {
        /// <summary>Each spec read from its AdditionalFile and metadata.</summary>
        public const string Specs = "Matchmark.Specs";

        /// <summary>The parse step: each spec read once into its equatable shape.</summary>
        public const string Shapes = "Matchmark.Shapes";

        /// <summary>What the compilation references (OpenAPI, fakes, rules, validation): a handful of bools.</summary>
        public const string References = "Matchmark.References";

        /// <summary>Whether a referenced contract project has each spec's validation resolver.</summary>
        public const string ReferencedResolvers = "Matchmark.ReferencedResolvers";

        /// <summary>Each tag's slice of each spec, for its endpoints (question 4).</summary>
        public const string TagSlices = "Matchmark.TagSlices";
    }

    internal sealed class SpecInput : IEquatable<SpecInput>
    {
        public SpecInput(string path, string content, string ns, string apiName, string parts, bool audit = false)
        {
            Audit = audit;
            Parts = parts;
            Path = path;
            Content = content;
            Namespace = ns;
            ApiName = apiName;
        }

        public string Path { get; }

        public string Parts { get; }

        /// <summary>Report what the spec leaves out (MM024).</summary>
        public bool Audit { get; }

        /// <summary>Generate the spec's problem types instead of using ASP.NET Core's (MatchmarkProblems="Owned").</summary>
        public bool OwnProblems { get; set; }

        /// <summary>Outcomes on Resultful and OneOf (MatchmarkOutcomes="Resultful"), not the contract's own.</summary>
        public bool Resultful { get; set; }

        /// <summary>The client validates on .NET's layer (MatchmarkClientValidation="Net").</summary>
        public bool ClientOnNet { get; set; }

        public string Content { get; }

        public string Namespace { get; }

        public string ApiName { get; }

        /// <summary>Overlays (text and path) applied to the spec before it's read.</summary>
        public IReadOnlyList<(string Text, string Path)> Overlays { get; private set; } = Array.Empty<(string, string)>();

        /// <summary>One tag's slice of the spec, overlays already applied (question 4).</summary>
        public string? SliceTag { get; private set; }

        public SpecInput Sliced(string json, string tag)
            => new(Path + "." + (tag.Length == 0 ? "untagged" : tag) + ".json", json, Namespace, ApiName, Parts, Audit)
            {
                OwnProblems = OwnProblems, Resultful = Resultful, ClientOnNet = ClientOnNet, SliceTag = tag,
            };

        /// <summary>The same spec, read from other content (a bundle of its parts).</summary>
        public SpecInput WithContent(string content, string path)
            => new(path, content, Namespace, ApiName, Parts, Audit) { Overlays = Overlays, OwnProblems = OwnProblems, Resultful = Resultful, ClientOnNet = ClientOnNet };

        public SpecInput WithOverlays(IReadOnlyList<(string Text, string Path)> overlays)
            => new(Path, Content, Namespace, ApiName, Parts, Audit) { Overlays = overlays, OwnProblems = OwnProblems, Resultful = Resultful, ClientOnNet = ClientOnNet };

        public bool Equals(SpecInput? other)
            => other is not null && Path == other.Path && Content == other.Content
               && Namespace == other.Namespace && ApiName == other.ApiName && Parts == other.Parts && Audit == other.Audit && OwnProblems == other.OwnProblems && Resultful == other.Resultful && ClientOnNet == other.ClientOnNet && SliceTag == other.SliceTag
               && Overlays.SequenceEqual(other.Overlays);

        public override bool Equals(object? obj) => Equals(obj as SpecInput);

        public override int GetHashCode() => (Path, Content, Namespace, ApiName, Parts).GetHashCode();
    }

    internal static class Diagnostics
    {
        private const string Category = "Matchmark";

        public static readonly DiagnosticDescriptor InvalidSpec = new(
            "MM001", "OpenAPI document could not be read", "'{0}' could not be read: {1}",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor Unsupported = new(
            "MM002", "OpenAPI construct not supported", "{0} is not supported by the prototype generator and was skipped: {1}",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor MissingOperationId = new(
            "MM003", "Operation has no operationId", "{0} {1} has no operationId; it was skipped",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UndocumentedValidation = new(
            "MM004", "Request validation response is not documented",
            "Operation '{0}' has a request body but documents no 400 response with an HttpValidationProblemDetails body; validation failures will be returned anyway",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnsampleableString = new(
            "MM005", "No example for a constrained string",
            "{0} has a pattern the generated sample does not match; give it an example so the fake can answer with a valid body",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ReaderProblem = new(
            "MM007", "The OpenAPI reader objected to part of the spec",
            "{0}: {1}. Generated from the rest; the part objected to may be missing.",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor LooselyTyped = new(
            "MM009", "Typed as JSON",
            "{0} is {1}, so it's typed as JsonElement: it compiles and round-trips, but nothing checks its shape. Add a discriminator to type it.",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnreadableArazzo = new(
            "MM008", "Arazzo document could not be read",
            "'{0}' could not be read as an Arazzo document, so its workflows aren't in the suite: {1}",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor RegressionNeedsRuntime = new(
            "MM006", "The Regression part needs Matchmark.Testing",
            "{0} asks for the Regression part, but the project doesn't reference Matchmark.Testing, which runs the suite",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor OperationNotImplemented = new(
            "MM010", "Operation not implemented",
            "The spec's operation '{0}' ({1} {2}) has no endpoint: add a controller action or Map call for it",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor EndpointNotInSpec = new(
            "MM011", "Endpoint not in the spec",
            "{0} ({1} {2}) is not in the spec: document it, or leave it out of the OpenAPI description ([ApiExplorerSettings(IgnoreApi = true)] or ExcludeFromDescription())",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ResponseNotDeclared = new(
            "MM012", "Documented response not declared",
            "{0}: the spec documents a {1} response, but the endpoint declares none; declare it with {2}",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ResponseNotDocumented = new(
            "MM013", "Declared response not documented",
            "{0}: the endpoint declares a {1} response, which the spec does not document",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor OperationNameMismatch = new(
            "MM014", "Endpoint name is not the operationId",
            "{0} is named '{1}'; name it '{2}', the spec's operationId, which the OpenAPI description uses",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor RouteParameterMismatch = new(
            "MM015", "Route parameter named differently",
            "{0}: the route parameter is '{{{1}}}', the spec calls it '{{{2}}}'",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ParameterNotBound = new(
            "MM016", "Spec parameter not bound",
            "{0}: the spec's {1} parameter '{2}' is not bound; add {3}",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ParameterNotDocumented = new(
            "MM017", "Parameter not in the spec",
            "{0}: '{3}' binds {1} value '{2}', which the spec doesn't document",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor BindingTypeMismatch = new(
            "MM018", "Bound with a different type",
            "{0}: {1} is bound as {2}; the spec says {3}",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor OptionalParameterRequired = new(
            "MM019", "Optional parameter required",
            "{0}: '{1}' is optional in the spec, but binding it as a non-nullable {2} with no default makes it required; make it {2}? or give it a default",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor BodyMismatch = new(
            "MM020", "Request body not bound as the spec says",
            "{0}: {1}",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ModelConsistent = new(
            "MM021", "Model consistent, but not by construction",
            "{0}: {1} is {2}, which matches the schema {3} today; use the generated types so it stays consistent by construction",
            Category, DiagnosticSeverity.Info, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ModelDiffers = new(
            "MM022", "Model differs from its schema",
            "{0}: {1}",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ParameterConstraintDiffers = new(
            "MM023", "Parameter constraint differs",
            "{0}: {1}",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor RulesNotEnforced = new(
            "MM025", "Invariants declared but not enforced",
            "The spec declares {0}, which the generated server doesn't enforce: reference Matchmark.Rules to make a broken rule the spec's 400",
            Category, DiagnosticSeverity.Info, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor InvariantDeclaredWrongly = new(
            "MM027", "Invariant declared wrongly",
            "{0} has {1}, so it's ignored: see docs/reference/invariants.md",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor InvariantStatusMissing = new(
            "MM028", "No status for an invariant",
            "'{0}' can break {1} but documents none of {2}, so the twin answers the first as a problem; document how it refuses",
            Category, DiagnosticSeverity.Info, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor NetValidationMissing = new(
            "MM029", ".NET's validation layer not referenced",
            "Microsoft.Extensions.Validation isn't referenced, so the contract has no resolver for .NET's validation layer and keeps Matchmark's own validator: see docs/reference/validation.md",
            Category, DiagnosticSeverity.Info, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor KeywordUnchecked = new(
            "MM030", "No attribute for a keyword",
            "{0} has {1}, which no attribute on .NET's validation layer checks: the regression suite does (add Matchmark.Validation for multipleOf, uniqueItems and property counts)",
            Category, DiagnosticSeverity.Info, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor OwnedProblemsNeedNoServer = new(
            "MM026", "Owned problem types are for a contract without a server",
            "MatchmarkProblems=\"Owned\" generates the spec's problem types so the contract needs no ASP.NET Core; the Server and FakeServer parts write ASP.NET Core's problem results, so they weren't generated. Generate them in a project without it.",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor Incomplete = new(
            "MM024", "The spec leaves out something the suite would use",
            "{0} {1} ({2}): saying it unlocks {3}. At {4}; matchmark audit measures what it adds.",
            Category, DiagnosticSeverity.Info, isEnabledByDefault: true);

        public static readonly ImmutableArray<DiagnosticDescriptor> All =
            ImmutableArray.Create(InvalidSpec, Unsupported, MissingOperationId, UndocumentedValidation, UnsampleableString, RegressionNeedsRuntime, ReaderProblem, UnreadableArazzo, LooselyTyped,
                OperationNotImplemented, EndpointNotInSpec, ResponseNotDeclared, ResponseNotDocumented, OperationNameMismatch, RouteParameterMismatch,
                ParameterNotBound, ParameterNotDocumented, BindingTypeMismatch, OptionalParameterRequired, BodyMismatch, ModelConsistent, ModelDiffers, ParameterConstraintDiffers);
    }
}
