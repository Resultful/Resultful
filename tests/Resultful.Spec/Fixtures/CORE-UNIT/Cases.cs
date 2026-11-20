// Decision 14 (12): a step with no value is Result<Unit>; Result.Ok() returns it. Unit stays a class.
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    static Result<Unit> Cancel(bool locked) => locked ? Error.From("locked") : Unit.Value;

    [SpecCase]
    public static void Ok_with_no_value_is_a_Result_of_Unit()
    {
        Result<Unit> ok = Result.Ok();
        Check.True(ok.IsSuccess, "Result.Ok()");
        Check.True(ReferenceEquals(Unit.Value, ok.Value), "the one Unit");
        Check.True(typeof(Unit).IsClass, "Unit is a class");
    }

    [SpecCase]
    public static void A_typed_step_with_no_value()
    {
        Result<Unit, string> ok = Result.Ok<string>();
        Check.True(ok.IsSuccess, "Result.Ok<TError>()");
        Check.True(Cancel(true).IsFailure, "return an error");
        Check.True(Cancel(false).IsSuccess, "return Unit.Value");
    }

    [SpecCase]
    public static void DiscardValue_gives_a_Result_of_Unit()
    {
        Result<Unit> r = Result.Ok(5).DiscardValue();
        Check.True(r.IsSuccess, "DiscardValue");
    }
}
