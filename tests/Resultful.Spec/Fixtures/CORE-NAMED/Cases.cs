// M2 named methods (05): IsSuccess, IsFailure and Value, with nullable flow from [MemberNotNullWhen].
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    static Result<string, int> Ok() => "order 7";
    static Result<string, int> Failed() => 404;

    [SpecCase]
    public static void A_success_reports_itself_and_its_value()
    {
        var r = Ok();
        Check.True(r.IsSuccess, "IsSuccess");
        Check.False(r.IsFailure, "IsFailure");
        Check.Equal(7, r.Value.Length, "Value");
    }

    [SpecCase]
    public static void A_failure_reports_its_error_and_value_throws()
    {
        var r = Failed();
        Check.True(r.IsFailure, "IsFailure");
        Check.Equal(404, r.Error, "Error");
        Check.Throws<System.InvalidOperationException>(() => _ = r.Value, "Value on a failure");
        Check.Throws<System.InvalidOperationException>(() => _ = Ok().Error, "Error on a success");
    }

    // No CS8602: IsSuccess tells the compiler Value isn't null.
    // expect-clean
    public static int Length(Result<string?, int> r) => r.IsSuccess ? r.Value.Length : r.Error;
}
