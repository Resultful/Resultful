// Decision 10 (08b): the zero value is invalid; reading its value or error throws InvalidOperationException.
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    sealed class Holder { public Result<int> Field; }

    [SpecCase]
    public static void Reads_of_the_zero_value_throw()
    {
        var zero = default(Result<int, string>);
        Check.Throws<System.InvalidOperationException>(() => zero.Match(v => v, e => -1), "Match");
        Check.Throws<System.InvalidOperationException>(() => _ = zero.Value, "Value");
        Check.Throws<System.InvalidOperationException>(() => zero.TryGetValue(out _), "TryGetValue");
        Check.Throws<System.InvalidOperationException>(() => zero.Switch(_ => { }, _ => { }), "Switch");
        Check.Throws<System.InvalidOperationException>(() => _ = zero.IsSuccess, "IsSuccess");
    }

    [SpecCase]
    public static void An_unassigned_field_and_an_array_slot_are_not_successes()
    {
        Check.Throws<System.InvalidOperationException>(() => new Holder().Field.Map(x => x + 1).Match(v => v, _ => 0), "field");
        Check.Throws<System.InvalidOperationException>(() => _ = (new Result<string>[1])[0].Value, "array slot");
    }
}
