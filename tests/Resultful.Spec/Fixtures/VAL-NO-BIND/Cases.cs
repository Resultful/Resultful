// Decision 14 and DECISIONS-TAKEN (10): Validation has no Bind and no `+`; a dependent step moves to Result.
using System.Linq;
using System.Reflection;
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    static Validation<int, string> Age(int v) => v;

    static bool IsValidation(System.Type t) => t.IsGenericType && t.GetGenericTypeDefinition() is var d && (d == typeof(Validation<,>) || d == typeof(Validation<>));

    [SpecCase]
    public static void Nothing_binds_or_adds_a_Validation()
    {
        var methods = typeof(Unit).Assembly.GetExportedTypes().SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)).ToList();
        var binds = methods.Where(m => m.Name is "Bind" or "BindAsync" or "op_Addition")
            .Where(m => IsValidation(m.DeclaringType!) || m.GetParameters().FirstOrDefault() is { } p && IsValidation(p.ParameterType)).ToList();
        Check.True(binds.Count == 0, "found: " + string.Join(", ", binds));
        Check.True(typeof(Validation<,>).IsValueType, "Validation exists");
    }

    [SpecCase]
    public static void A_dependent_step_goes_through_ToResult()
    {
        var r = Age(20).ToResult(es => es[0]).Bind(a => a >= 18 ? Result.Ok<int, string>(a) : "too young");
        Check.Equal(20, r.Value, "Result.Bind after ToResult");
    }
}
