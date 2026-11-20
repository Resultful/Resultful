// Decision 14 and decision 16 item 4 (09): WithContext on a result keeps the result's and the error's types.
using Resultful;
using Resultful.Spec.Support;

public abstract record OrderError(string Message) : Error(Message);
public sealed record OrderNotFound(int Id) : OrderError($"order {Id} was not found");

public static class Cases
{
    [SpecCase]
    public static void Context_on_a_typed_result_keeps_its_types()
    {
        Result<string, OrderError> r = new OrderNotFound(4);
        Result<string, OrderError> noted = r.WithContext("loading order 4");
        Check.True(noted.TryGetError(out var e) && e is OrderNotFound && e.Context[0] == "loading order 4", "still OrderNotFound, with the note");
    }

    [SpecCase]
    public static void Context_on_Result_of_T()
    {
        Result<int> r = Result.Fail<int>("db");
        var noted = r.WithContext("invoicing").MapError(e => e.WithContext("job 9"));
        Check.True(noted.TryGetError(out var e) && e.Context.Count == 2 && e.Context[1] == "job 9", "two notes, in order");
    }
}
