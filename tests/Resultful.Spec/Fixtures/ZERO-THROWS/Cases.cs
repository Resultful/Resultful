// Decision 10 and the accepted quality rule (08b): only reading the value or the error of a zero value throws.
// IsSuccess and IsFailure are both false on it and don't throw (DECISIONS-TAKEN, 04c review default).
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
        Check.Throws<System.InvalidOperationException>(() => _ = zero.Error, "Error");
        Check.Throws<System.InvalidOperationException>(() => zero.TryGetValue(out _), "TryGetValue");
        Check.Throws<System.InvalidOperationException>(() => zero.TryGetError(out _), "TryGetError");
        Check.Throws<System.InvalidOperationException>(() => zero.Switch(_ => { }, _ => { }), "Switch");
    }

    [SpecCase]
    public static void The_zero_value_is_neither_a_success_nor_a_failure()
    {
        var zero = default(Result<int, string>);
        Check.False(zero.IsSuccess, "IsSuccess");
        Check.False(zero.IsFailure, "IsFailure");
        Check.False(default(Result<int>).IsSuccess || default(Result<int>).IsFailure, "Result<T>");
    }

    [SpecCase]
    public static void An_unassigned_field_and_an_array_slot_are_not_successes()
    {
        Check.Throws<System.InvalidOperationException>(() => new Holder().Field.Map(x => x + 1).Match(v => v, _ => 0), "field");
        Check.Throws<System.InvalidOperationException>(() => _ = (new Result<string>[1])[0].Value, "array slot");
    }
}
