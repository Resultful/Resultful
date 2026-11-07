using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Microsoft.OpenApi.Reader;

namespace Resultful.OpenApi.Generator
{
    /// <summary>
    /// Reads every AdditionalFiles item marked <c>ResultfulOpenApi="true"</c> and generates, from the OpenAPI document,
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
                .Select(static (spec, _) => spec!);

            // The OpenAPI document configuration is only emitted when the app describes itself with
            // Microsoft.AspNetCore.OpenApi; without it the generated code would not compile.
            // Likewise the fake, and fake-server hosting, need the Resultful.OpenApi.Fakes runtime.
            var references = context.CompilationProvider
                .Select(static (compilation, _) => (
                    DescribesItself: compilation.GetTypeByMetadataName("Microsoft.AspNetCore.OpenApi.OpenApiOptions") is not null,
                    HasFakes: compilation.GetTypeByMetadataName("Resultful.OpenApi.Fakes.Scenario") is not null));

            context.RegisterSourceOutput(specs.Combine(references), static (spc, pair) => Generate(spc, pair.Left, pair.Right.DescribesItself, pair.Right.HasFakes));
        }

        private static SpecInput? ReadSpec(AdditionalText text, AnalyzerConfigOptionsProvider options, System.Threading.CancellationToken ct)
        {
            var fileOptions = options.GetOptions(text);
            if (!fileOptions.TryGetValue(MetadataPrefix + "ResultfulOpenApi", out var enabled)
                || !string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var content = text.GetText(ct)?.ToString() ?? string.Empty;
            var fileName = Path.GetFileNameWithoutExtension(text.Path);

            if (!fileOptions.TryGetValue(MetadataPrefix + "ResultfulNamespace", out var ns) || string.IsNullOrWhiteSpace(ns))
            {
                options.GlobalOptions.TryGetValue("build_property.RootNamespace", out ns);
            }

            if (!fileOptions.TryGetValue(MetadataPrefix + "ResultfulApiName", out var apiName) || string.IsNullOrWhiteSpace(apiName))
            {
                apiName = Naming.Pascal(fileName);
            }

            // Contract: types, unions, handler interfaces, validation and the client. Server: endpoints and the OpenAPI
            // configuration. A contract library takes "Contract", the service that implements it "Server".
            fileOptions.TryGetValue(MetadataPrefix + "ResultfulParts", out var parts);
            return new SpecInput(text.Path, content, string.IsNullOrWhiteSpace(ns) ? "Generated" : ns!, apiName!,
                string.IsNullOrWhiteSpace(parts) ? "Contract;Server" : parts!);
        }

        private static void Generate(SourceProductionContext context, SpecInput spec, bool describesItself, bool hasFakes)
        {
            var location = Location.Create(spec.Path, default, default);
            var format = spec.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? "json" : "yaml";

            var settings = new OpenApiReaderSettings();
            settings.AddYamlReader();

            ReadResult read;
            try
            {
                read = Microsoft.OpenApi.OpenApiDocument.Parse(spec.Content, format, settings);
            }
#pragma warning disable CA1031 // A spec that cannot be read is reported, never thrown into the compiler.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.InvalidSpec, location, spec.Path, ex.Message));
                return;
            }

            if (read.Document is null || read.Diagnostic?.Errors.Count > 0)
            {
                var message = string.Join("; ", read.Diagnostic?.Errors.Select(e => e.ToString()) ?? Enumerable.Empty<string>());
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.InvalidSpec, location, spec.Path, message));
                return;
            }

            var emitter = new Emitter(read.Document, spec.Namespace, spec.ApiName, location, describesItself, hasFakes);
            var parts = spec.Parts.Split(';').Select(p => p.Trim()).ToList();
            var files = emitter.Emit(
                contract: parts.Contains("Contract", StringComparer.OrdinalIgnoreCase),
                server: parts.Contains("Server", StringComparer.OrdinalIgnoreCase));

            foreach (var diagnostic in emitter.Diagnostics)
            {
                context.ReportDiagnostic(diagnostic);
            }

            foreach (var file in files)
            {
                context.AddSource($"{spec.ApiName}.{file.Key}.g.cs", SourceText.From(file.Value, System.Text.Encoding.UTF8));
            }
        }
    }

    internal sealed class SpecInput : IEquatable<SpecInput>
    {
        public SpecInput(string path, string content, string ns, string apiName, string parts)
        {
            Parts = parts;
            Path = path;
            Content = content;
            Namespace = ns;
            ApiName = apiName;
        }

        public string Path { get; }

        public string Parts { get; }

        public string Content { get; }

        public string Namespace { get; }

        public string ApiName { get; }

        public bool Equals(SpecInput? other)
            => other is not null && Path == other.Path && Content == other.Content
               && Namespace == other.Namespace && ApiName == other.ApiName && Parts == other.Parts;

        public override bool Equals(object? obj) => Equals(obj as SpecInput);

        public override int GetHashCode() => (Path, Content, Namespace, ApiName, Parts).GetHashCode();
    }

    internal static class Diagnostics
    {
        private const string Category = "Resultful.OpenApi";

        public static readonly DiagnosticDescriptor InvalidSpec = new(
            "RAPI001", "OpenAPI document could not be read", "'{0}' could not be read: {1}",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor Unsupported = new(
            "RAPI002", "OpenAPI construct not supported", "{0} is not supported by the prototype generator and was skipped: {1}",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor MissingOperationId = new(
            "RAPI003", "Operation has no operationId", "{0} {1} has no operationId; it was skipped",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UndocumentedValidation = new(
            "RAPI004", "Request validation response is not documented",
            "Operation '{0}' has a request body but documents no 400 response with an HttpValidationProblemDetails body; validation failures will be returned anyway",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnsampleableString = new(
            "RAPI005", "No example for a constrained string",
            "{0} has a pattern the generated sample does not match; give it an example so the fake can answer with a valid body",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly ImmutableArray<DiagnosticDescriptor> All =
            ImmutableArray.Create(InvalidSpec, Unsupported, MissingOperationId, UndocumentedValidation, UnsampleableString);
    }
}
