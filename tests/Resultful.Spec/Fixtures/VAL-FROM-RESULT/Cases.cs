// Decision 14 (10): a Result converts to a Validation implicitly; one error is a list of one.
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    static Result<int, string> Parse(string s) => int.TryParse(s, out var v) ? v : $"'{s}' isn't a number";

    [SpecCase]
    public static void A_result_steps_into_a_validation()
    {
        var v = Validation.Combine<int, int, string, int>(Parse("x"), Parse("y"), (a, b) => a + b);
        Check.Equal(2, v.Errors.Count, "both parse errors");
        Validation<int> fromCore = Result.Fail<int>("no");
        Check.Equal(1, fromCore.Errors.Count, "Result<T> to Validation<T>");
    }
}
