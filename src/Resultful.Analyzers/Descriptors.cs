using Microsoft.CodeAnalysis;

namespace Resultful.Analyzers;

/// <summary>
/// The 16a rules. IDs, severities, titles and keys are the target's
/// tests/Resultful.TargetApi/Resultful.Analyzers/diagnostics.tsv; a test holds them to it.
/// </summary>
public static class Descriptors
{
    public const string Category = "Resultful.Usage";

    /// <summary>The .editorconfig key for third-party types (and methods) treated as [MustObserve].</summary>
    public const string MustObserveTypesKey = "resultful.must_observe_types";

    public const string MustObserveTitle =
        "A [MustObserve] value is never observed (includes an unawaited Task or ValueTask of one, and an async void handler passed to Switch; only `_ =` passes)";

    public const string DefaultResultTitle = "A result is created with default (includes default of an error union)";

    /// <summary>RF0001: a value thrown away. The argument is a phrase such as "The Result&lt;int&gt; returned by Cancel".</summary>
    public static readonly DiagnosticDescriptor MustObserve = new(
        "RF0001",
        MustObserveTitle,
        "{0} is never observed: use it, or discard it with `_ =`",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: MustObserveDescription);

    const string MustObserveDescription =
        "A value of a [MustObserve] type, or returned by a [MustObserve] method, carries a failure that is lost if the value is thrown away. "
        + "Awaiting a task of one and dropping the result loses it too, as does converting a task of one to Task. Only an explicit `_ =` discard says the loss is meant. "
        + "Scope: a value left on its own as a statement (or a void lambda's body), an array of them, an unawaited or awaited-and-dropped task of one, "
        + "a fresh task of one converted to Task or ValueTask, and an async void handler given to a [MustObserve] value's (or task's) method. "
        + "Not covered: a field or local assigned and never read, `var _ = Make();` (a local named `_`), assigning to a lambda parameter named `_`, "
        + "a value consumed by a method that drops it (`xs.Select(Make).ToList()`), and arrays of zero values (`new Result<int>[3]`, RF0006 in 16b covers zero fields).";

    /// <summary>RF0001, for a task of a [MustObserve] value converted to the non-generic Task or ValueTask.</summary>
    public static readonly DiagnosticDescriptor MustObserveUpcast = new(
        "RF0001",
        MustObserveTitle,
        "{0} is used as a {1}, so its {2} is never observed: await it as itself",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: MustObserveDescription);

    /// <summary>RF0001, for an async lambda that becomes async void as a handler of a [MustObserve] type's method.</summary>
    public static readonly DiagnosticDescriptor MustObserveAsyncVoid = new(
        "RF0001",
        MustObserveTitle,
        "This async handler passed to {0} is async void: its task is never observed and an exception in it is lost",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: MustObserveDescription);

    /// <summary>RF0002: default of a type whose zero value is not a valid value (decision 10), or of an error union (decision 17).</summary>
    public static readonly DiagnosticDescriptor DefaultResult = new(
        "RF0002",
        DefaultResultTitle,
        "A default {0} holds neither a value nor an error: create it with a value or an error",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The zero value of a result is uninitialised: reading it throws, and it must never pass as a success (decision 10). "
            + "The zero value of an error union has no case, and a switch over it throws (decision 17). "
            + "Scope: `default(T)`, a `default` literal and `new T()` with no declared constructor, where T is a struct that forbids `new T()` "
            + "with an [Obsolete(..., error: true)] parameterless constructor, carries [ResultShape], or is an error union. "
            + "Comparing with default (`r == default`, `r.Equals(default)`) is not reported, nor is `default` in a pattern (the compiler's own rules apply). "
            + "Not covered: arrays of zero values (`new Result<int>[3]`) and fields no constructor assigns (RF0006, 16b).");
}
