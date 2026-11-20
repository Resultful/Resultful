#nullable enable
namespace Resultful
{
    // Decision 16 item 8: a generated shape is a named wrapper over one core value and implements one of these.
    public interface IResultShape<T, TError>
    {
        Result<T, TError> AsResult();
    }

    public interface IValidationShape<T, TError>
    {
        Validation<T, TError> AsValidation();
    }

    // Decision 17: the generator adds this to a `partial union` whose cases are all errors. Shipped on every target,
    // so the public API is the same everywhere; only net11.0 code can declare a union.
    public interface IErrorUnion
    {
        Error Error { get; }
    }
}
