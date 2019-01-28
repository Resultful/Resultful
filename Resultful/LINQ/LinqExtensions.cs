using System;
using Resultful.Utils;

namespace Resultful.LINQ
{
    public static class LinqExtensions
    {

        //Linq Extensions for Option<T>
        public static Option<TResult> Select<TSource, TResult>(this Option<TSource> value, Func<TSource, TResult> mapFunc)
            => value.Map(mapFunc);

        public static Option<TResult> SelectMany<TSource, TResult>(this Option<TSource> value, Func<TSource, Option<TResult>> bindFunc)
            => value.Bind(bindFunc);

        public static Option<TResult> SelectMany<TSource, TCollection, TResult>(this Option<TSource> value, Func<TSource, Option<TCollection>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
            => value.Bind(src => bindFunc.ThrowIfDefault(nameof(bindFunc))(src).Map(elem => mapFunc.ThrowIfDefault(nameof(mapFunc))(src, elem)));

        /*
        public static Option<TSource> Where<TSource>(this Option<TSource> value, Func<TSource, bool> filterFunc)
            => value.Filter(filterFunc.ThrowIfDefault(nameof(filterFunc)));
        */

        //Linq Extensions for Result<T, TError>
        public static Result<TResult, TError> Select<TSource, TError, TResult>(this Result<TSource, TError> value, Func<TSource, TResult> mapFunc)
            => value.Map(mapFunc);

        public static Result<TResult, TError> SelectMany<TSource, TError, TResult>(this Result<TSource, TError> value, Func<TSource, Result<TResult, TError>> bindFunc)
            => value.Bind(bindFunc);

        public static Result<TResult, TError> SelectMany<TSource, TError, TCollection, TResult>(this Result<TSource, TError> value, Func<TSource, Result<TCollection, TError>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
            => value.Bind(src => bindFunc.ThrowIfDefault(nameof(bindFunc))(src).Map(elem => mapFunc.ThrowIfDefault(nameof(mapFunc))(src, elem)));

        //Linq Extensions for Result<T>
        public static Result<TResult> Select<TSource, TResult>(this Result<TSource> value, Func<TSource, TResult> mapFunc)
            => value.Map(mapFunc);

        public static Result<TResult> SelectMany<TSource, TResult>(this Result<TSource> value, Func<TSource, Result<TResult>> bindFunc)
            => value.Bind(bindFunc);

        public static Result<TResult> SelectMany<TSource, TCollection, TResult>(this Result<TSource> value, Func<TSource, Result<TCollection>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
            => value.Bind(src => bindFunc.ThrowIfDefault(nameof(bindFunc))(src).Map(elem => mapFunc.ThrowIfDefault(nameof(mapFunc))(src, elem)));



    }
}
