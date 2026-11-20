// Quality (06): Error equality is structural, Context compared element by element, before and after WithContext.
using Resultful;
using Resultful.Spec.Support;

public sealed record OrderNotFound(int Id) : Error($"order {Id} was not found");

public static class Cases
{
    [SpecCase]
    public static void Two_identical_errors_are_equal()
    {
        Check.Equal(new OrderNotFound(1), new OrderNotFound(1), "no context");
        Check.Equal(new OrderNotFound(1).WithContext("a"), new OrderNotFound(1).WithContext("a"), "same context");
        Check.Equal(new OrderNotFound(1).WithContext("a").GetHashCode(), new OrderNotFound(1).WithContext("a").GetHashCode(), "hash");
        Check.False(new OrderNotFound(1).WithContext("a").Equals(new OrderNotFound(1).WithContext("b")), "different context");
        Check.False(new OrderNotFound(1).Equals(new OrderNotFound(1).WithContext("a")), "context takes part");
        Check.Equal(Error.From("x") with { Cause = Error.From("y") }, Error.From("x") with { Cause = Error.From("y") }, "cause compared");
    }
}
