// Decision 3's matrix on every target (08b): no case is misclassified by the own storage: identical case types, a
// List<string> success beside a list-like error, a null success and the zero value, all from a second assembly.
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    [SpecCase]
    public static void Overlapping_and_identical_case_types_keep_their_case()
    {
        var ok = Producers.SameTypesOk();
        var failed = Producers.SameTypesFail();
        Check.True(ok.IsSuccess && ok.Value == "value", "Result<string, string> success");
        Check.True(failed.IsFailure && failed.Error == "error", "Result<string, string> failure");
        Check.Equal("ok value", ok.Match(v => "ok " + v, e => "error " + e), "Match takes the success arm");
        var list = Producers.ListSuccess();
        Check.True(list.IsSuccess && list.Value.Count == 1, "a List<string> success isn't an error");
    }

    [SpecCase]
    public static void A_null_success_and_the_zero_value()
    {
        var nul = Producers.NullSuccess();
        Check.True(nul.IsSuccess && nul.Value is null, "a null success is a success");
        var zero = Producers.Zero();
        Check.True(zero.IsDefault && !zero.IsSuccess && !zero.IsFailure, "the zero value is neither case");
    }
}
