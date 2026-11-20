// Quality (08b): Fail(null!) throws ArgumentNullException, so a null error can't become a failure.
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    [SpecCase]
    public static void A_null_error_is_rejected()
    {
        Check.Throws<System.ArgumentNullException>(() => Result.Fail<int, Error>(null!), "Fail<T, TError>");
        Check.Throws<System.ArgumentNullException>(() => Result.Fail<int, string>(null!), "a string error");
        Check.Throws<System.ArgumentNullException>(() => { Result<int, Error> r = (Error)null!; }, "implicit conversion");
    }
}
