// Decision 14 (09): Result<T> holds one Error instead of IEnumerable<string>. A leaf converts implicitly.
using Resultful;
using Resultful.Spec.Support;

public sealed record NotFound(string Entity, int Id) : Error($"{Entity} {Id} was not found");

public static class Cases
{
    static Result<string> Find(int id) => id == 1 ? "alice" : new NotFound("user", id);

    [SpecCase]
    public static void A_failure_carries_the_typed_error()
    {
        var message = Find(2).Match(_ => "found", error => error switch
        {
            NotFound n => $"missing {n.Entity} {n.Id}",
            _ => error.Message,
        });
        Check.Equal("missing user 2", message, "match");
    }

    [SpecCase]
    public static void Fail_from_a_message_makes_a_MessageError()
    {
        var r = Result.Fail<int>("boom");
        Check.True(r.TryGetError(out var e) && e is MessageError { Message: "boom" }, "MessageError");
    }

    [SpecCase]
    public static void Bind_stops_at_the_first_failure()
    {
        var steps = 0;
        var r = Find(2).Bind(name => { steps++; return Result.Ok(name.Length); });
        Check.Equal(0, steps, "the step after a failure doesn't run");
        Check.True(r.IsFailure, "still a failure");
    }
}
