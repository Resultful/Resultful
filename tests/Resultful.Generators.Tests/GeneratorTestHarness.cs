using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Resultful.Generators.Tests;

internal sealed record GeneratorRunOutput(
    Compilation InputCompilation,
    Compilation OutputCompilation,
    GeneratorDriverRunResult RunResult,
    ImmutableArray<Diagnostic> GeneratorDiagnostics,
    ImmutableArray<Diagnostic> CompilationDiagnostics,
    IReadOnlyDictionary<string, string> GeneratedSources);

internal static class GeneratorTestHarness
{
    public const string CoreStubSource = """
        using System;
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using System.Diagnostics.CodeAnalysis;

        namespace Resultful.CodeAnalysis
        {
            [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class | AttributeTargets.Method)]
            public sealed class MustObserveAttribute : Attribute;

            [AttributeUsage(AttributeTargets.Class)]
            public sealed class ClosedHierarchyAttribute : Attribute;

            [AttributeUsage(AttributeTargets.Struct)]
            public sealed class ResultShapeAttribute(Type errorType) : Attribute
            {
                public Type ErrorType { get; } = errorType;
                public Type? Success { get; set; }
                public bool ManyErrors { get; set; }
            }
        }

        namespace Resultful
        {
            public readonly struct Unit { public static Unit Value => default; }

            public abstract record Error
            {
                protected Error(string message) => Message = message;
                public string Message { get; }
                public Error? Cause { get; init; }
                public ImmutableList<string> Context { get; init; } = ImmutableList<string>.Empty;
                public IEnumerable<Error> Causes()
                {
                    for (var cur = this; cur is not null; cur = cur.Cause)
                        yield return cur;
                }
            }

            public interface IResultShape<T, TError>
            {
                Result<T, TError> AsResult();
            }

            public interface IValidationShape<T, TError>
            {
                Validation<T, TError> AsValidation();
            }

            public readonly struct Result<T>
            {
                private readonly byte _state;
                private readonly T _value;
                private readonly Error _error;
                private Result(T v) { _state = 1; _value = v; _error = default!; }
                private Result(Error e) { _state = 2; _value = default!; _error = e; }
                public bool IsDefault => _state == 0;
                public static Result<T> Ok(T v) => new(v);
                public static Result<T> Fail(Error e) => new(e);
            }

            public readonly struct Result<T, TError>
            {
                private readonly byte _state;
                private readonly T _value;
                private readonly TError _error;
                private Result(T v) { _state = 1; _value = v; _error = default!; }
                private Result(TError e) { _state = 2; _value = default!; _error = e; }
                public bool IsDefault => _state == 0;
                public static Result<T, TError> Ok(T v) => new(v);
                public static Result<T, TError> Fail(TError e) => new(e);
                public bool TryGetValue([MaybeNullWhen(false)] out T value)
                {
                    if (_state == 0) throw new InvalidOperationException();
                    if (_state == 1) { value = _value; return true; }
                    value = default; return false;
                }
                public bool TryGetError([MaybeNullWhen(false)] out TError error)
                {
                    if (_state == 0) throw new InvalidOperationException();
                    if (_state == 2) { error = _error; return true; }
                    error = default; return false;
                }
                public Result<TOut, TError> Map<TOut>(Func<T, TOut> map) =>
                    TryGetValue(out var v) ? Result<TOut, TError>.Ok(map(v)) : Result<TOut, TError>.Fail(_error);
                public Result<TOut, TError> Bind<TOut>(Func<T, Result<TOut, TError>> bind) =>
                    TryGetValue(out var v) ? bind(v) : Result<TOut, TError>.Fail(_error);
                public TOut Match<TOut>(Func<T, TOut> onOk, Func<TError, TOut> onErr) =>
                    TryGetValue(out var v) ? onOk(v) : onErr(_error);
                public Result<T> AsResult() =>
                    TryGetValue(out var v) ? Result<T>.Ok(v) : Result<T>.Fail((Error)(object)_error!);
            }

            public readonly struct Validation<T, TError>
            {
                private readonly byte _state;
                private readonly T _value;
                private readonly IReadOnlyList<TError> _errors;
                private Validation(T v) { _state = 1; _value = v; _errors = default!; }
                private Validation(IReadOnlyList<TError> e) { _state = 2; _value = default!; _errors = e; }
                public bool IsDefault => _state == 0;
                public static Validation<T, TError> Ok(T v) => new(v);
                public static Validation<T, TError> Fail(IReadOnlyList<TError> e) => new(e);
                public bool TryGetValue([MaybeNullWhen(false)] out T value)
                {
                    if (_state == 0) throw new InvalidOperationException();
                    if (_state == 1) { value = _value; return true; }
                    value = default; return false;
                }
                public bool TryGetErrors([MaybeNullWhen(false)] out IReadOnlyList<TError> errors)
                {
                    if (_state == 0) throw new InvalidOperationException();
                    if (_state == 2) { errors = _errors; return true; }
                    errors = default; return false;
                }
                public Validation<TOut, TError> Map<TOut>(Func<T, TOut> map) =>
                    TryGetValue(out var v) ? Validation<TOut, TError>.Ok(map(v)) : Validation<TOut, TError>.Fail(_errors);
                public TOut Match<TOut>(Func<T, TOut> onOk, Func<IReadOnlyList<TError>, TOut> onErrs) =>
                    TryGetValue(out var v) ? onOk(v) : onErrs(_errors);
            }
        }
        """;

    public static string RepoRootDir =>
        typeof(GeneratorTestHarness).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRootDir").Value!;

    public static List<MetadataReference> GetRuntimeReferences()
    {
        var trustedAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        return trustedAssemblies
            .Where(File.Exists)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
    }

    public static CSharpCompilation CreateCompilation(
        IEnumerable<(string FileName, string Source)> sources,
        IEnumerable<MetadataReference>? extraReferences = null,
        string assemblyName = "FixtureAssembly",
        bool includeCoreStub = true,
        OptimizationLevel optimizationLevel = OptimizationLevel.Release)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var trees = new List<SyntaxTree>();
        if (includeCoreStub)
        {
            trees.Add(CSharpSyntaxTree.ParseText(CoreStubSource, parseOptions, path: "CoreStub.cs"));
        }

        foreach (var (name, src) in sources)
        {
            trees.Add(CSharpSyntaxTree.ParseText(src, parseOptions, path: name));
        }

        var refs = GetRuntimeReferences();
        if (extraReferences is not null)
        {
            refs.AddRange(extraReferences);
        }

        return CSharpCompilation.Create(
            assemblyName,
            trees,
            refs,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: optimizationLevel,
                nullableContextOptions: NullableContextOptions.Enable,
                generalDiagnosticOption: ReportDiagnostic.Warn));
    }

    public static MetadataReference EmitToReference(CSharpCompilation compilation)
    {
        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);
        if (!result.Success)
        {
            var errs = string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            throw new InvalidOperationException($"Failed to emit reference assembly '{compilation.AssemblyName}':\n{errs}");
        }

        ms.Position = 0;
        return MetadataReference.CreateFromStream(ms);
    }

    public static GeneratorRunOutput Run(
        CSharpCompilation compilation,
        out GeneratorDriver driver)
    {
        var generator = new ResultShapeGenerator().AsSourceGenerator();
        driver = CSharpGeneratorDriver.Create(
            generators: [generator],
            parseOptions: (CSharpParseOptions)compilation.SyntaxTrees[0].Options,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var genDiags);
        var runResult = driver.GetRunResult();
        var generated = runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .ToDictionary(s => s.HintName, s => s.SourceText.ToString(), StringComparer.Ordinal);

        return new GeneratorRunOutput(
            compilation,
            outputCompilation,
            runResult,
            genDiags,
            outputCompilation.GetDiagnostics(),
            generated);
    }

    public static byte[] EmitAssemblyBytes(Compilation compilation)
    {
        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);
        if (!result.Success)
        {
            var errs = string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            throw new InvalidOperationException($"Emit failed:\n{errs}");
        }

        return ms.ToArray();
    }
}
