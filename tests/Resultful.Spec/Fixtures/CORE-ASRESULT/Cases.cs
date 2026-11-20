// Decision 14 (09): AsResult widens Result<T, TError> to Result<T> when TError derives from Error, keeping the instance.
using Resultful;
using Resultful.Spec.Support;

public abstract record OrderError(string Message) : Error(Message);
public sealed record OrderLocked(int Id) : OrderError($"order {Id} is locked");

public static class Cases
{
    [SpecCase]
    public static void Widening_keeps_the_value_and_the_error_instance()
    {
        var locked = new OrderLocked(7);
        Result<string, OrderError> failed = locked;
        Result<string> widened = failed.AsResult();
        Check.True(widened.TryGetError(out var e) && ReferenceEquals(e, locked), "same instance");

        Result<string, OrderError> ok = "order";
        Check.Equal("order", ok.AsResult().Value, "value");
    }

    [SpecCase]
    public static void A_wrapping_MapError_widens_back_with_AsResult()
    {
        Result<int> Save() => Result.Fail<int>("db timeout");
        Result<int> saved = Save().MapError(e => new OrderLocked(1) { Cause = e }).AsResult();
        Check.True(saved.TryGetError(out var e) && e is OrderLocked && e.Cause?.Message == "db timeout", "cause kept");
    }
}
