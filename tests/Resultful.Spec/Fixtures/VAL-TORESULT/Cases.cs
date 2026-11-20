// Decision 14 laws 3 and 6 (10): ToResult() carries every error as one ValidationFailed; ToResult(combine) asks how.
using System.Linq;
using Resultful;
using Resultful.Spec.Support;

public sealed record Required(string Field) : Error($"{Field} is required");

public static class Cases
{
    [SpecCase]
    public static void ToResult_carries_the_list_as_ValidationFailed()
    {
        Validation<int, Required> v = Validation.Fail<int, Required>(new[] { new Required("a"), new Required("b") });
        Result<int> r = v.ToResult();
        Check.True(r.TryGetError(out var e) && e is ValidationFailed<Required> vf && vf.Errors.Count == 2, "ValidationFailed");
        Check.True(e!.TryFind<Required>(out var found) && found.Field == "a", "TryFind sees each");
    }

    [SpecCase]
    public static void ToResult_with_combine_and_the_round_trip()
    {
        Result<int, string> failed = "only";
        Validation<int, string> v = failed;
        Check.Equal(failed, v.ToResult(es => es[0]), "law 3: Result to Validation and back");
        var joined = Validation.Fail<int, string>(new[] { "a", "b" }).ToResult(es => string.Join("+", es));
        Check.Equal("a+b", joined.Error, "combine");
    }

    [SpecCase]
    public static void Validation_of_T_widens_to_Result_of_T()
    {
        Validation<int> v = Error.From("x");
        Check.True(v.ToResult().Error is ValidationFailed<Error> { Errors.Count: 1 }, "Validation<T>.ToResult()");
    }
}
