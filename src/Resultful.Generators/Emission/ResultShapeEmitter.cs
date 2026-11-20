using System.Linq;
using Resultful.Generators.Models;

namespace Resultful.Generators.Emission;

internal static class ResultShapeEmitter
{
    private const string InlineAttr =
        "[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]";

    public static string Emit(ResultShapeModel m, bool unionAvailable)
    {
        var w = new SourceWriter();
        w.OpenNamespaceAndEnclosing(m.Namespace, m.EnclosingTypes);

        var ok = m.SuccessFullName;
        var err = m.ErrorFullName;
        var coreType = m.ManyErrors
            ? $"global::Resultful.Validation<{ok}, {err}>"
            : $"global::Resultful.Result<{ok}, {err}>";
        var ifaceType = m.ManyErrors
            ? $"global::Resultful.IValidationShape<{ok}, {err}>"
            : $"global::Resultful.IResultShape<{ok}, {err}>";
        var errListType = $"global::System.Collections.Generic.IReadOnlyList<{err}>";

        w.WriteLine("[global::Resultful.CodeAnalysis.MustObserve]");
        if (unionAvailable)
        {
            w.WriteLine("[global::System.Runtime.CompilerServices.Union]");
        }

        var bases = unionAvailable
            ? $"{ifaceType}, global::System.Runtime.CompilerServices.IUnion"
            : ifaceType;
        w.WriteLine($"{m.Accessibility} readonly partial struct {m.Name} : {bases}");
        w.OpenBrace();

        // Single backing field of the core type
        w.WriteLine($"private readonly {coreType} _inner;");
        w.WriteLine();

        // Parameterless ctor obsolete error (Decision 10)
        w.WriteLine("[global::System.Obsolete(\"Use Ok, Fail, or an implicit conversion.\", error: true)]");
        w.WriteLine(InlineAttr);
        w.WriteLine($"public {m.Name}() => _inner = default;");
        w.WriteLine();

        // Private core wrapper ctor
        w.WriteLine(InlineAttr);
        w.WriteLine($"private {m.Name}({coreType} inner) => _inner = inner;");

        // Union constructors (net11+)
        if (unionAvailable)
        {
            w.WriteLine();
            w.WriteLine(InlineAttr);
            w.WriteLine($"public {m.Name}({ok} value) => _inner = {coreType}.Ok(value);");
            if (m.ManyErrors)
            {
                w.WriteLine(InlineAttr);
                w.WriteLine($"public {m.Name}({errListType} errors) => _inner = {coreType}.Fail(errors);");
            }
            else if (m.ErrorIsClosed && m.Leaves.Length > 0)
            {
                for (var i = 0; i < m.Leaves.Length; i++)
                {
                    var leaf = m.Leaves[i];
                    w.WriteLine(InlineAttr);
                    w.WriteLine($"public {m.Name}({leaf.FullName} error) => _inner = {coreType}.Fail(error);");
                }
            }
            else
            {
                w.WriteLine(InlineAttr);
                w.WriteLine($"public {m.Name}({err} error) => _inner = {coreType}.Fail(error);");
            }

            w.WriteLine();
            w.WriteLine("public bool HasValue");
            w.OpenBrace();
            w.WriteLine(InlineAttr);
            w.WriteLine("get => !_inner.IsDefault;");
            w.CloseBrace();

            w.WriteLine("public object? Value");
            w.OpenBrace();
            w.WriteLine(InlineAttr);
            w.WriteLine(m.ManyErrors
                ? "get => _inner.TryGetValue(out var v) ? (object?)v : (_inner.TryGetErrors(out var e) ? e : null);"
                : "get => _inner.TryGetValue(out var v) ? (object?)v : (_inner.TryGetError(out var e) ? e : null);");
            w.CloseBrace();

            if (m.ManyErrors)
            {
                w.WriteLine(InlineAttr);
                w.WriteLine($"public bool TryGetValue([global::System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out {errListType} errors) => _inner.TryGetErrors(out errors);");
            }
            else if (m.ErrorIsClosed && m.Leaves.Length > 0)
            {
                for (var i = 0; i < m.Leaves.Length; i++)
                {
                    var leaf = m.Leaves[i];
                    w.WriteLine(InlineAttr);
                    w.WriteLine($"public bool TryGetValue([global::System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out {leaf.FullName} error) => (error = _inner.TryGetError(out var e) ? e as {leaf.FullName} : null) is not null;");
                }
            }
            else
            {
                w.WriteLine(InlineAttr);
                w.WriteLine($"public bool TryGetValue([global::System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out {err} error) => _inner.TryGetError(out error);");
            }
        }

        // Core factories
        w.WriteLine();
        w.WriteLine(InlineAttr);
        w.WriteLine($"public static {m.Name} Ok({ok} value) => new({coreType}.Ok(value));");
        if (m.ManyErrors)
        {
            w.WriteLine(InlineAttr);
            w.WriteLine($"public static {m.Name} Fail({errListType} errors) => new({coreType}.Fail(errors));");
            w.WriteLine(InlineAttr);
            w.WriteLine($"public {coreType} AsValidation() => _inner;");
        }
        else
        {
            w.WriteLine(InlineAttr);
            w.WriteLine($"public static {m.Name} Fail({err} error) => new({coreType}.Fail(error));");
            w.WriteLine(InlineAttr);
            w.WriteLine($"public {coreType} AsResult() => _inner;");
        }

        // Implicit conversions
        w.WriteLine();
        if (!m.SuccessIsInterface)
        {
            w.WriteLine(InlineAttr);
            w.WriteLine($"public static implicit operator {m.Name}({ok} value) => Ok(value);");
        }

        if (m.ManyErrors)
        {
            w.WriteLine(InlineAttr);
            w.WriteLine($"public static implicit operator {m.Name}({coreType} validation) => new(validation);");
            w.WriteLine(InlineAttr);
            w.WriteLine($"public static implicit operator {coreType}({m.Name} shape) => shape._inner;");
        }
        else
        {
            w.WriteLine(InlineAttr);
            w.WriteLine($"public static implicit operator {m.Name}({err} error) => Fail(error);");
            w.WriteLine(InlineAttr);
            w.WriteLine($"public static implicit operator {m.Name}({coreType} result) => new(result);");
            w.WriteLine(InlineAttr);
            w.WriteLine($"public static implicit operator {coreType}({m.Name} shape) => shape._inner;");
            if (m.ErrorDerivesFromError)
            {
                w.WriteLine(InlineAttr);
                w.WriteLine($"public static implicit operator global::Resultful.Result<{ok}>({m.Name} shape) => shape._inner.AsResult();");
            }
        }

        // TryGetValue / TryGetError(s)
        w.WriteLine();
        w.WriteLine(InlineAttr);
        w.WriteLine($"public bool TryGetValue([global::System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out {ok} value) => _inner.TryGetValue(out value);");
        if (m.ManyErrors)
        {
            w.WriteLine(InlineAttr);
            w.WriteLine($"public bool TryGetErrors([global::System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out {errListType} errors) => _inner.TryGetErrors(out errors);");
            w.WriteLine(InlineAttr);
            w.WriteLine($"public TOut Match<TOut>(global::System.Func<{ok}, TOut> onSuccess, global::System.Func<{errListType}, TOut> onErrors) => _inner.Match(onSuccess, onErrors);");
            w.WriteLine(InlineAttr);
            w.WriteLine($"public global::Resultful.Validation<TOut, {err}> Map<TOut>(global::System.Func<{ok}, TOut> map) => _inner.Map(map);");
        }
        else
        {
            w.WriteLine(InlineAttr);
            w.WriteLine($"public bool TryGetError([global::System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out {err} error) => _inner.TryGetError(out error);");

            // Forwarded Map & Bind (per building-the-generator.md verified finding)
            w.WriteLine(InlineAttr);
            w.WriteLine($"public global::Resultful.Result<TOut, {err}> Map<TOut>(global::System.Func<{ok}, TOut> map) => _inner.Map(map);");
            w.WriteLine(InlineAttr);
            w.WriteLine($"public global::Resultful.Result<TOut, {err}> Bind<TOut>(global::System.Func<{ok}, global::Resultful.Result<TOut, {err}>> bind) => _inner.Bind(bind);");

            // Match
            if (m.ErrorIsClosed && m.Leaves.Length > 0)
            {
                var leafParams = string.Join(
                    ", ",
                    m.Leaves.Select(l => $"global::System.Func<{l.FullName}, TOut> on{l.ParamSuffix}"));
                w.WriteLine($"public TOut Match<TOut>(global::System.Func<{ok}, TOut> onSuccess, {leafParams})");
                w.OpenBrace();
                w.WriteLine("if (_inner.TryGetValue(out var v)) { return onSuccess(v); }");
                w.WriteLine("_inner.TryGetError(out var err);");
                w.WriteLine("switch (err)");
                w.OpenBrace();
                for (var i = 0; i < m.Leaves.Length; i++)
                {
                    var leaf = m.Leaves[i];
                    w.WriteLine($"case {leaf.FullName} e: return on{leaf.ParamSuffix}(e);");
                }

                w.WriteLine("default: throw new global::System.InvalidOperationException(\"Unexpected error case.\");");
                w.CloseBrace();
                w.CloseBrace();
            }
            else
            {
                w.WriteLine(InlineAttr);
                w.WriteLine($"public TOut Match<TOut>(global::System.Func<{ok}, TOut> onSuccess, global::System.Func<{err}, TOut> onError) => _inner.Match(onSuccess, onError);");
            }
        }

        w.CloseBrace();
        w.CloseNamespaceAndEnclosing(m.Namespace, m.EnclosingTypes);
        return w.ToString();
    }
}
