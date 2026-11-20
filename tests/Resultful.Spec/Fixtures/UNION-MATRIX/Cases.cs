// Decision 3 (24, net11.0): once the core types are unions, IUnion.Value agrees with the case for the matrix (CORE-MATRIX
// pins the named methods on every target): identical case types, a null success, the zero value, from a second assembly.
// spec: tfm net11.0
// spec: uses core-union
using System.Runtime.CompilerServices;
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    [SpecCase]
    public static void IUnion_Value_holds_the_case()
    {
        Check.True(((IUnion)Producers.SameTypesOk()).Value is "value", "Result<string, string> success");
        Check.True(((IUnion)Producers.SameTypesFail()).Value is "error", "Result<string, string> failure");
        Check.True(((IUnion)Producers.ListSuccess()).Value is System.Collections.Generic.List<string>, "a List<string> success");
        Check.True(((IUnion)Producers.NullSuccess()).Value is null && Producers.NullSuccess().IsSuccess, "a null success");
        Check.True(((IUnion)Producers.Zero()).Value is null, "the zero value has no case");
    }
}
