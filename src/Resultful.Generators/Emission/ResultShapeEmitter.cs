using System.Linq;
using Resultful.Generators.Models;

namespace Resultful.Generators.Emission;

internal static class ResultShapeEmitter
{
    private const string InlineAttr =
        "[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]";

    public static string Emit(ResultShapeModel m, bool unionAvailable, EquatableArray<string> peers)
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

        // Decision 17: a union error's direct cases are the shape's leaves, wrapped into the union to fail; a nested
        // union stays one case, and its own leaves still convert in.
        var union = m.ManyErrors ? null : m.Union;
        var hasLeaves = (m.ErrorIsClosed || union is not null) && m.Leaves.Length > 0;
        string Wrap(string leaf) => union is null ? leaf : $"new {err}({leaf})";

        // The core has no static Ok/Fail on the result structs: the named builders are on Result and Validation.
        string OkCore(string value) => m.ManyErrors
            ? $"global::Resultful.Validation.Ok<{ok}, {err}>({value})"
            : $"global::Resultful.Result.Ok<{ok}, {err}>({value})";
        string FailCore(string error) => m.ManyErrors
            ? $"global::Resultful.Validation.Fail<{ok}, {err}>({error})"
            : $"global::Resultful.Result.Fail<{ok}, {err}>({error})";

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
            w.WriteLine($"public {m.Name}({ok} value) => _inner = {OkCore("value")};");
            if (m.ManyErrors)
            {
                w.WriteLine(InlineAttr);
                w.WriteLine($"public {m.Name}({errListType} errors) => _inner = {FailCore("errors")};");
            }
            else if (hasLeaves)
            {
                for (var i = 0; i < m.Leaves.Length; i++)
                {
                    var leaf = m.Leaves[i];
                    w.WriteLine(InlineAttr);
                    w.WriteLine($"public {m.Name}({leaf.FullName} error) => _inner = {FailCore(Wrap("error"))};");
                }
            }
            else
            {
                w.WriteLine(InlineAttr);
                w.WriteLine($"public {m.Name}({err} error) => _inner = {FailCore("error")};");
            }

            w.WriteLine();
            w.WriteLine("public bool HasValue");
            w.OpenBrace();
            w.WriteLine(InlineAttr);
            w.WriteLine("get => _inner.IsSuccess || _inner.IsFailure;");
            w.CloseBrace();

            // Decision 10: Value on the zero value throws, as the core's TryGetValue does; HasValue is the check that
            // doesn't throw.
            w.WriteLine("public object? Value");
            w.OpenBrace();
            w.WriteLine(InlineAttr);
            w.WriteLine(m.ManyErrors
                ? "get => _inner.TryGetValue(out var v) ? (object?)v : (_inner.TryGetErrors(out var e) ? e : null);"
                : union is not null
                    ? "get => _inner.TryGetValue(out var v) ? (object?)v : (_inner.TryGetError(out var e) ? e.Value : null);"
                    : "get => _inner.TryGetValue(out var v) ? (object?)v : (_inner.TryGetError(out var e) ? e : null);");
            w.CloseBrace();

            if (m.ManyErrors)
            {
                w.WriteLine(InlineAttr);
                w.WriteLine($"public bool TryGetValue([global::System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out {errListType} errors) => _inner.TryGetErrors(out errors);");
            }
            else if (union is not null && hasLeaves)
            {
                for (var i = 0; i < m.Leaves.Length; i++)
                {
                    var leaf = m.Leaves[i];
                    w.WriteLine(InlineAttr);
                    w.WriteLine($"public bool TryGetValue([global::System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out {leaf.FullName} error)");
                    w.OpenBrace();
                    w.WriteLine($"if (_inner.TryGetError(out var e) && e.Value is {leaf.FullName} c) {{ error = c; return true; }}");
                    w.WriteLine("error = default;");
                    w.WriteLine("return false;");
                    w.CloseBrace();
                }
            }
            else if (hasLeaves)
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

        // Decision 10: true for the zero value only, which the core reads as neither a success nor a failure.
        w.WriteLine();
        w.WriteLine("public bool IsDefault");
        w.OpenBrace();
        w.WriteLine(InlineAttr);
        w.WriteLine("get => !_inner.IsSuccess && !_inner.IsFailure;");
        w.CloseBrace();

        // Core factories
        w.WriteLine();
        w.WriteLine(InlineAttr);
        if (m.IsValueless)
        {
            // A step with no value succeeds with Unit (decision 14).
            w.WriteLine($"public static {m.Name} Ok() => new({OkCore("global::Resultful.Unit.Value")});");
        }
        else
        {
            w.WriteLine($"public static {m.Name} Ok({ok} value) => new({OkCore("value")});");
        }

        if (m.ManyErrors)
        {
            w.WriteLine(InlineAttr);
            w.WriteLine($"public static {m.Name} Fail({errListType} errors) => new({FailCore("errors")});");
            w.WriteLine(InlineAttr);
            w.WriteLine($"public {coreType} AsValidation() => _inner;");
        }
        else
        {
            w.WriteLine(InlineAttr);
            w.WriteLine($"public static {m.Name} Fail({err} error) => new({FailCore("error")});");
            w.WriteLine(InlineAttr);
            w.WriteLine($"public {coreType} AsResult() => _inner;");
        }

        // Implicit conversions
        w.WriteLine();
        if (!m.SuccessIsInterface)
        {
            w.WriteLine(InlineAttr);
            w.WriteLine($"public static implicit operator {m.Name}({ok} value) => new({OkCore("value")});");
        }

        if (m.ManyErrors)
        {
            w.WriteLine(InlineAttr);
            w.WriteLine($"public static implicit operator {m.Name}({coreType} validation) => new(validation);");
            w.WriteLine(InlineAttr);
            w.WriteLine($"public static implicit operator {coreType}({m.Name} shape) => shape._inner;");
            if (m.ErrorDerivesFromError)
            {
                // The list travels as one ValidationFailed<TError> (decision 14).
                w.WriteLine(InlineAttr);
                w.WriteLine($"public static implicit operator global::Resultful.Result<{ok}>({m.Name} shape) => global::Resultful.Validation.ToResult(shape._inner);");
            }
        }
        else
        {
            w.WriteLine(InlineAttr);
            w.WriteLine($"public static implicit operator {m.Name}({err} error) => Fail(error);");
            if (hasLeaves)
            {
                for (var i = 0; i < m.Leaves.Length; i++)
                {
                    w.WriteLine(InlineAttr);
                    w.WriteLine($"public static implicit operator {m.Name}({m.Leaves[i].FullName} error) => Fail({Wrap("error")});");
                }
            }

            // C# applies one user-defined conversion at a time, so leaf -> inner union -> outer union needs its own.
            var nested = union?.NestedLeaves ?? EquatableArray<NestedLeafModel>.Empty;
            for (var i = 0; i < nested.Length; i++)
            {
                w.WriteLine(InlineAttr);
                w.WriteLine($"public static implicit operator {m.Name}({nested[i].FullName} error) => Fail({nested[i].WrapOpen}error{new string(')', nested[i].Depth)});");
            }

            w.WriteLine(InlineAttr);
            w.WriteLine($"public static implicit operator {m.Name}({coreType} result) => new(result);");
            w.WriteLine(InlineAttr);
            w.WriteLine($"public static implicit operator {coreType}({m.Name} shape) => shape._inner;");
            if (m.ErrorDerivesFromError)
            {
                w.WriteLine(InlineAttr);
                w.WriteLine($"public static implicit operator global::Resultful.Result<{ok}>({m.Name} shape) => global::Resultful.Result.AsResult(shape._inner);");
            }
            else if (union?.ErrorAccess is { } access)
            {
                // Widens to the leaf, through IErrorUnion.Error, as a hierarchy's shape does.
                w.WriteLine(InlineAttr);
                w.WriteLine($"public static implicit operator global::Resultful.Result<{ok}>({m.Name} shape) => shape._inner.MapError(static e => {access});");
            }
        }

        // From another shape with the same success type (decision 16 item 5) whose error is a closed base nested under
        // this one (cast up) or a case of this shape's error union (wrapped). The zero value converts to the zero value:
        // only reading it throws (decision 10).
        for (var i = 0; i < peers.Length; i++)
        {
            var inner = m.ManyErrors ? "other.AsValidation()" : "other.AsResult()";
            w.WriteLine(InlineAttr);
            w.WriteLine($"public static implicit operator {m.Name}({peers[i]} other) => other.IsDefault ? default({m.Name}) : new {m.Name}({inner}.MapError<{err}>(static e => {Wrap("e")}));");
        }

        // A note on the error that keeps the shape (decision 16 item 4), so it can still be switched over.
        if (m.ErrorDerivesFromError)
        {
            w.WriteLine();
            w.WriteLine(InlineAttr);
            w.WriteLine(m.ManyErrors
                ? $"public {m.Name} WithContext(string message) => new(_inner.MapError(e => global::Resultful.ErrorExtensions.WithContext(e, message)));"
                : $"public {m.Name} WithContext(string message) => new(global::Resultful.Result.WithContext(_inner, message));");
        }
        else if (union is { HasWithContext: true })
        {
            // The union's WithContext rewraps the leaf, so the shape and its union keep their types.
            w.WriteLine();
            w.WriteLine(InlineAttr);
            w.WriteLine($"public {m.Name} WithContext(string message) => message is null ? throw new global::System.ArgumentNullException(nameof(message)) : new(_inner.MapError(e => e.WithContext(message)));");
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
            w.WriteLine($"public TResult Match<TResult>(global::System.Func<{ok}, TResult> onSuccess, global::System.Func<{errListType}, TResult> onErrors) => _inner.Match(onSuccess, onErrors);");
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
            if (hasLeaves)
            {
                var leafParams = string.Join(
                    ", ",
                    m.Leaves.Select(l => $"global::System.Func<{l.FullName}, TResult> on{LeafParam(l)}"));
                w.WriteLine($"public TResult Match<TResult>(global::System.Func<{ok}, TResult> onSuccess, {leafParams})");
                w.OpenBrace();
                w.WriteLine("if (_inner.TryGetValue(out var v)) { return onSuccess(v); }");
                w.WriteLine("_inner.TryGetError(out var err);");
                w.WriteLine(union is not null ? "switch (err.Value)" : "switch (err)");
                w.OpenBrace();
                for (var i = 0; i < m.Leaves.Length; i++)
                {
                    var leaf = m.Leaves[i];
                    w.WriteLine($"case {leaf.FullName} e: return on{LeafParam(leaf)}(e);");
                }

                w.WriteLine("default: throw new global::System.InvalidOperationException(\"Unexpected error case.\");");
                w.CloseBrace();
                w.CloseBrace();
            }
            else
            {
                w.WriteLine(InlineAttr);
                w.WriteLine($"public TResult Match<TResult>(global::System.Func<{ok}, TResult> onSuccess, global::System.Func<{err}, TResult> onError) => _inner.Match(onSuccess, onError);");
            }
        }

        w.CloseBrace();
        w.CloseNamespaceAndEnclosing(m.Namespace, m.EnclosingTypes);
        return w.ToString();
    }

    // A leaf named Success would clash with Match's onSuccess.
    private static string LeafParam(LeafModel leaf) =>
        leaf.ParamSuffix == "Success" ? "SuccessError" : leaf.ParamSuffix;
}
