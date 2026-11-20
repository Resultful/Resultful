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
        description: "A value of a [MustObserve] type, or returned by a [MustObserve] method, carries a failure that is lost if the value is thrown away. "
            + "Awaiting a task of one and dropping the result loses it too. Only an explicit `_ =` discard says the loss is meant.");

    /// <summary>RF0001, for an async lambda that becomes async void as a handler of a [MustObserve] type's method.</summary>
    public static readonly DiagnosticDescriptor MustObserveAsyncVoid = new(
        "RF0001",
        MustObserveTitle,
        "This async handler passed to {0} is async void: its task is never observed and an exception in it is lost",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The method takes a void delegate, so an async lambda passed to it runs fire-and-forget. Use the method's async overload, or a handler that isn't async.");

    /// <summary>RF0002: default of a type whose zero value is not a valid value (decision 10), or of an error union (decision 17).</summary>
    public static readonly DiagnosticDescriptor DefaultResult = new(
        "RF0002",
        DefaultResultTitle,
        "A default {0} holds neither a value nor an error: create it with a value or an error",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The zero value of a result is uninitialised: reading it throws, and it must never pass as a success (decision 10). "
            + "The zero value of an error union has no case, and a switch over it throws (decision 17).");
}
