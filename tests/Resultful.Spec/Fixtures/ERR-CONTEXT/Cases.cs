// Decision 14 (06): WithContext adds a note and keeps the type; the context list is copied on add.
using Resultful;
using Resultful.Spec.Support;

public abstract record OrderError(string Message) : Error(Message);
public sealed record OrderNotFound(int Id) : OrderError($"order {Id} was not found");

public static class Cases
{
    [SpecCase]
    public static void A_note_keeps_the_type_and_a_switch_still_matches()
    {
        OrderError e = new OrderNotFound(3);
        OrderError noted = e.WithContext("loading order 3").WithContext("checkout");
        var status = noted switch { OrderNotFound => 404, _ => 500 };
        Check.Equal(404, status, "switch");
        Check.SequenceEqual(new[] { "loading order 3", "checkout" }, noted.Context, "notes, innermost first");
    }

    [SpecCase]
    public static void Adding_a_note_doesnt_change_the_original()
    {
        var original = new OrderNotFound(3).WithContext("one");
        var more = original.WithContext("two");
        Check.Equal(1, original.Context.Count, "original unchanged");
        Check.Equal(2, more.Context.Count, "copy has both");
    }

    [SpecCase]
    public static void TryFind_finds_the_same_thing_after_a_note()
    {
        Error e = new OrderNotFound(3) { Cause = Error.From("db") };
        Check.True(e.WithContext("x").TryFind<MessageError>(out var inner) && inner.Message == "db", "law 5");
    }
}
