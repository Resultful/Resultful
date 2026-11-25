using Microsoft.CodeAnalysis;

namespace Resultful.Analyzers;

/// <summary>
/// RF0001 to RF0004, RF0006, RF0008 and RF0009: the IDs, severities, titles and the .editorconfig keys are the 0.3 analyzer contract, and
/// AnalyzerReleases.Unshipped.md lists them. Each description says what the rule covers and what it doesn't.
/// </summary>
public static class Descriptors
{
    /// <summary>The category of every Resultful rule.</summary>
    public const string Category = "Resultful.Usage";

    /// <summary>The .editorconfig key for third-party types (and methods) treated as [MustObserve].</summary>
    public const string MustObserveTypesKey = "resultful.must_observe_types";

    /// <summary>RF0001's title.</summary>
    public const string MustObserveTitle =
        "A [MustObserve] value is never observed (includes an unawaited Task or ValueTask of one, and an async void handler passed to Switch; only `_ =` passes)";

    /// <summary>RF0002's title.</summary>
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
        + "`await Task.WhenAll(t1, t2)` over locals, fields or parameters is not reported, since each task can be awaited as itself. "
        + "Like Rust's #[must_use], a generic method that returns its type argument is reported when that argument is a [MustObserve] type "
        + "(`Interlocked.Exchange(ref r, next);`, `results.Single();`). An async void handler on a dropped value (`r.Tee(async x => ...);`) "
        + "gets two RF0001s: the `_ =` fix only fixes the dropped value, and the handler must become non-async or the method's async form. "
        + "Not covered: a field or local assigned and never read, `var _ = Make();` (a local named `_`), assigning to a lambda parameter named `_`, "
        + "a value consumed by a method that drops it (`xs.Select(Make).ToList()`), and arrays of zero values (`new Result<int>[3]`).";

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
            + "Every other zero value is, including `default` assigned to an out parameter: suppress RF0002 where a zero value is meant. "
            + "Not covered: arrays of zero values (`new Result<int>[3]`) and fields no constructor assigns.");

    /// <summary>RF0003: <c>r.Value</c> where no check of <c>r</c> guards the read.</summary>
    public static readonly DiagnosticDescriptor UncheckedValue = new(
        "RF0003",
        "Value is read without checking IsSuccess",
        "{0}.Value is read without checking IsSuccess: it throws on a failure",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Value throws on a failure (and on the zero value). Scope: the Value property of a [MustObserve] type that also has IsSuccess "
            + "(Result, Validation, a user's own). A read of a local, parameter or field passes when a check of the same variable guards it: "
            + "inside `if (r.IsSuccess)`, `r.IsSuccess ? r.Value : …`, `r.IsSuccess && r.Value…`, the else of `if (r.IsFailure)`, "
            + "a pattern (`r is { IsSuccess: true }`, `r.IsSuccess is true`), a `TryGetValue` call in the condition, or after "
            + "`if (!r.IsSuccess) return;` (any exit) earlier in an enclosing block, switch section or the top-level statements. "
            + "Any other receiver, such as `Get().Value`, is reported. Not covered: an assignment to the variable between the check and the read, "
            + "a check in a switch arm or nested pattern, a check in a helper method, and `Debug.Assert`.");

    /// <summary>RF0004: a string literal converted implicitly to a result whose success type is string.</summary>
    public static readonly DiagnosticDescriptor BareString = new(
        "RF0004",
        "A bare string becomes the success of a Result<string>",
        "This string becomes the success of a {0}: say which with Result.Ok(…) or Result.Fail<string>(…)",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Before 0.3, `Result<T> r = \"message\";` was a failure. Now the string is the value (decision 9), so code written as a failure "
            + "compiles as a success. Info, not a warning: since 0.3 the conversion is a valid success, often intended. Scope: a string literal, an interpolated string or a concatenation with one, converted implicitly by a "
            + "[MustObserve] type's conversion from its first type parameter (or a [ResultShape]'s conversion from string). Not reported: a string "
            + "variable or call (assumed to be a value), an explicit cast, and `Result.Ok(\"x\")`. The fix wraps it in Result.Ok or Result.Fail, "
            + "offered for Resultful.Result<string> only; other types are fixed by hand. No fix-all: each one is a choice.");

    /// <summary>RF0006: a result field that no constructor assigns (decision 10, layer 3).</summary>
    public static readonly DiagnosticDescriptor UnassignedField = new(
        "RF0006",
        "A result field is never assigned by a constructor (decision 10 layer 3)",
        "{0} is a {1} that a constructor never assigns: until it is, it holds the zero value",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The zero value of a result holds neither a value nor an error (decision 10). Scope: a field or get-only auto-property of a result "
            + "type (as RF0002 defines it) in a class, with no initializer, that some constructor leaves unassigned: a declared constructor that "
            + "doesn't chain to `this(…)` and doesn't assign it (directly, by `out`/`ref`, or in a deconstruction), or the implicit one. "
            + "A static field needs an initializer or a static constructor that assigns it. Not reported: `required` members, settable properties "
            + "(an object initializer may set them), and structs (whose own zero value holds zero results anyway). "
            + "Not covered: a constructor that assigns through a helper method, and arrays of zero values (`new Result<int>[3]`).");

    /// <summary>RF0008: an implicitly typed handler passed to a generated multi-case Match, Switch or TryMap.</summary>
    public static readonly DiagnosticDescriptor UntypedHandler = new(
        "RF0008",
        "A handler passed to a generated multi-case Match, Switch or TryMap isn't explicitly typed or a method group (core's two-handler Match is exempt)",
        "This handler for {0} doesn't name its parameter type: write ({1} …) => so a reordered case doesn't compile",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A generated Match, Switch or TryMap takes one handler per case, in an order that renaming or adding a case can change. "
            + "An untyped lambda such as `_ => NotFound()` would then silently handle another case; with the type, the compiler rejects the call. "
            + "Scope: every lambda (or anonymous method) argument without parameter types to a Match, Switch or TryMap on a [ClosedHierarchy] type "
            + "with two or more case handlers, or on a [ResultShape] with two or more leaf handlers besides onSuccess. A method group, a typed lambda "
            + "and a delegate variable pass. Exempt: core's Match(onSuccess, onError) and a shape's two-handler Match, whose order is fixed. "
            + "The fix adds the types (fix-all too). Configure with dotnet_diagnostic.RF0008.severity.");

    /// <summary>RF0009: Map whose selector returns a result.</summary>
    public static readonly DiagnosticDescriptor MapToBind = new(
        "RF0009",
        "Map's selector returns a result: use Bind",
        "Map's selector returns a {0}, so this is a {1}: use {2}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A result nested in a result hides the inner failure behind an outer success. Scope: Map or MapAsync whose selector returns a "
            + "result type (as RF0002 defines it; for MapAsync, a Task or ValueTask of one), on a result or a Task or ValueTask of one. The fix replaces Map with Bind (MapAsync with BindAsync), "
            + "fix-all too, offered only where Bind binds: when the error types differ, map the error first.");
}
