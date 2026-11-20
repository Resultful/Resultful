// SHAPE-GATHER (15): a ManyErrors shape fails with a non-empty list and switches over two arms; Validation converts into it.
// spec: uses generator
using Resultful;
using Resultful.CodeAnalysis;
using Resultful.Spec.Support;

[ClosedHierarchy]
public abstract partial record InputError : Error;
public sealed record Required(string Field) : InputError($"{Field} is required");
public sealed record Registration(string Name, string Email);

[ResultShape(typeof(InputError), Success = typeof(Registration), ManyErrors = true)]
public readonly partial struct RegisterResult;

public static class Cases
{
    static Validation<string, InputError> Field(string name, string? v) => v is null ? new Required(name) : v;

    static RegisterResult Register(string? n, string? e) =>
        Validation.Combine(Field("name", n), Field("email", e), (a, b) => new Registration(a, b));

    [SpecCase]
    public static void Every_error_is_in_the_one_failure()
    {
        Check.True(Register(null, null).TryGetErrors(out var errors) && errors.Count == 2, "both");
        Check.True(Register("a", "b").TryGetValue(out Registration? reg) && reg.Name == "a", "success");
#if NET11_0_OR_GREATER
        var arms = Register(null, "b") switch { Registration => 0, System.Collections.Generic.IReadOnlyList<InputError> list => list.Count };
        Check.Equal(1, arms, "two arms");
#endif
    }
}
