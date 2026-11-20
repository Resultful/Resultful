#nullable enable
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Results;

// M5 (21). An adapter: plumbing, not rules (decision 11). Decision 14: returns Validation<T>.
namespace Resultful.FluentValidation
{
    public sealed record FluentValidationError : Error
    {
        public FluentValidationError(ValidationFailure failure) : base((string)null!) => throw null!;
        public string PropertyName => throw null!;
        public string ErrorCode => throw null!;
    }

    public static class ValidatorExtensions
    {
        public static Validation<T> ValidateToResult<T>(this IValidator<T> validator, T instance) => throw null!;
        public static Task<Validation<T>> ValidateToResultAsync<T>(this IValidator<T> validator, T instance, CancellationToken cancellationToken) => throw null!;
        public static Validation<T> ToValidation<T>(this ValidationResult result, T instance) => throw null!;
    }
}
