#nullable enable
using System.Threading.Tasks;
using OneOf;

namespace Resultful
{
    // Decision 1: OneOf interop moves out of the core. In namespace Resultful, so `using Resultful;` still finds it.
    public static class OneOfExtensions
    {
        public static OneOf<T, TError> ToOneOf<T, TError>(this Result<T, TError> value) => throw null!;
        public static OneOf<T, Error> ToOneOf<T>(this Result<T> value) => throw null!;
        public static OneOf<T, OneOf.Types.None> ToOneOf<T>(this Option<T> value) => throw null!;
        public static Task<OneOf<T, TError>> ToOneOf<T, TError>(this Task<Result<T, TError>> value) => throw null!;
        public static Task<OneOf<T, Error>> ToOneOf<T>(this Task<Result<T>> value) => throw null!;
        public static Task<OneOf<T, OneOf.Types.None>> ToOneOf<T>(this Task<Option<T>> value) => throw null!;

        public static Result<T, TError> FromOneOf<T, TError>(this OneOf<T, TError> value) => throw null!;
        public static Option<T> FromOneOf<T>(this OneOf<T, OneOf.Types.None> value) => throw null!;

        public static None ToResultful(this OneOf.Types.None value) => throw null!;
        public static OneOf.Types.None ToOneOf(this None value) => throw null!;
    }
}
