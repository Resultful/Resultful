// Decision 14 (06): Error has Message, Cause, Inner, Causes and TryFind; Error.From makes a MessageError.
using System.Linq;
using Resultful;
using Resultful.Spec.Support;

public sealed record DbTimeout(int Seconds) : Error($"the database timed out after {Seconds}s");
public sealed record SaveFailed(int OrderId) : Error($"order {OrderId} was not saved");

public static class Cases
{
    [SpecCase]
    public static void A_wrapper_keeps_its_cause_and_TryFind_reaches_it()
    {
        Error error = new SaveFailed(7) { Cause = new DbTimeout(30) };
        Check.True(error.TryFind<DbTimeout>(out var timeout) && timeout.Seconds == 30, "TryFind through Cause");
        Check.False(error.TryFind<MessageError>(out _), "nothing else");
        Check.SequenceEqual(new[] { "SaveFailed", "DbTimeout" }, error.Causes().Select(e => e.GetType().Name), "Causes: this, then the chain");
        Check.Equal(0, error.Inner().Count(), "no inner errors");
    }

    [SpecCase]
    public static void From_makes_a_MessageError()
    {
        var e = Error.From("plain");
        Check.True(e is MessageError, "MessageError");
        Check.Equal("plain", e.Message, "Message");
        Check.Equal(0, e.Context.Count, "no context");
        Check.Equal(null, e.Cause, "no cause");
    }
}
