// Decision 3 (24, net11.0): no case is misclassified once the core types are unions: identical case types, a list success
// beside an error, a null success, the zero value, all from a second assembly. IUnion.Value agrees with the named methods.
// spec: tfm net11.0
// spec: uses core-union
using System.Runtime.CompilerServices;
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    [SpecCase]
    public static void Overlapping_and_identical_case_types_keep_their_case()
    {
        var ok = Producers.SameTypesOk();
        var failed = Producers.SameTypesFail();
        Check.True(ok.IsSuccess && ok.Value == "value" && ((IUnion)ok).Value is "value", "Result<string, string> success");
        Check.True(failed.IsFailure && failed.Error == "error" && ((IUnion)failed).Value is "error", "Result<string, string> failure");
        Check.True(Producers.ListSuccess().IsSuccess, "a List<string> success isn't an error");
    }

    [SpecCase]
    public static void A_null_success_and_the_zero_value()
    {
        var nul = Producers.NullSuccess();
        Check.True(nul.IsSuccess && nul.Value is null, "a null success is a success");
        Check.True(Producers.Zero().IsDefault, "the zero value is IsDefault");
        Check.True(((IUnion)Producers.Zero()).Value is null, "and has no case");
    }
}
