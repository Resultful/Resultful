using System;
using System.Collections.Generic;
using System.Text;
using Resultful.Utils;

namespace Resultful
{
    public static partial class Result
    {
        //Builder for Fail<TError>
        public static Fail<IEnumerable<TError>> Err<TError>(this IEnumerable<TError> errors)
            => new Fail<IEnumerable<TError>>(errors ?? EmptyArray<TError>.Get);

        public static Fail<TError> Err<TError>(this TError value)
            => new Fail<TError>(value);

        public static Fail<IEnumerable<TError>> Err<TError>(params TError[] errors)
            => new Fail<IEnumerable<TError>>(errors ?? EmptyArray<TError>.Get);

        //Builder for Fail
        public static Fail Fail(this IEnumerable<string> value)
            => new Fail(value ?? EmptyArray<string>.Get);

        public static Fail Fail(params string[] errors)
            => new Fail(errors ?? EmptyArray<string>.Get);

        public static Fail Fail(this string item)
            => new Fail(new[] { item });

        //Builder for Ok
        public static Ok Ok()
            => new Ok(Unit.Value);

        public static Ok<T> Ok<T>(this T item)
            => new Ok<T>(item);

    }
}
