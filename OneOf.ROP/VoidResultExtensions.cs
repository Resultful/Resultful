using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OneOf.ROP.Utils;

namespace OneOf.ROP
{
    public static partial class Result
    {
        //Builder for VoidResult<TError>
        public static VoidResult<TError> Fail<TError>(this TError value)
            => new VoidResult<TError>(value);

        public static VoidResult<IEnumerable<TError>> Fail<TError>(params TError[] errors)
            => new VoidResult<IEnumerable<TError>>(errors ?? EmptyArray<TError>.Get);

        public static VoidResult<TError> Ok<TError>()
            => new VoidResult<TError>(Unit.Value);

        //Builder for VoidResult
        public static VoidResult Fail(this IEnumerable<string> value)
            => new VoidResult(value ?? EmptyArray<string>.Get);

        public static VoidResult Fail(params string[] errors)
            => new VoidResult(errors ?? EmptyArray<string>.Get);

        public static VoidResult Ok()
            => new VoidResult(Unit.Value);

        //Plus on VoidResult<TError>
        public static VoidResult<TError> Plus<TError>(this VoidResult<TError> left, VoidResult<TError> right, Func<TError, TError, TError> mergeFunc)
            => left.Match(
                leftValue => right.Match(
                    rightValue => Ok<TError>(), Fail
                ),
                error => right.Match(
                    rightValue => Fail(error),
                    otherError => Fail(mergeFunc.ThrowIfDefault(nameof(mergeFunc))(error, otherError))
                )
            );

        public static VoidResult<TError> Plus<TError>(this VoidResult<TError> left, VoidResult<TError> right) where TError : IPlus<TError, TError>
            => left.Plus(right, (leftError, rightError) => leftError.Plus(rightError));

        //Plus on VoidResult
        public static VoidResult Plus(this VoidResult left, VoidResult right)
            => left.Match(
                leftValue => right.Match(unit => Ok(), Fail),
                error => right.Match(rightValue => Fail(error), otherError => Fail(error.Concat(otherError)))
            );


        //Fold on VoidResult<TError>
        public static VoidResult<TError> Fold<TError>(this IEnumerable<VoidResult<TError>> values, Func<TError, TError, TError> mergeFunc)
            => values.Aggregate(Ok<TError>(), (acc, item) => acc.Plus(item, mergeFunc));

        public static VoidResult<TError> Fold<TError>(this IEnumerable<VoidResult<TError>> values) where TError : IPlus<TError, TError>
            => values.Aggregate(Ok<TError>(), (acc, item) => acc.Plus(item));

        public static VoidResult<TError> Fold<TError>(params VoidResult<TError>[] values) where TError : IPlus<TError, TError>
            => values.Aggregate(Ok<TError>(), (acc, item) => acc.Plus(item));

        //Fold on VoidResult
        public static VoidResult Fold(this IEnumerable<VoidResult> values)
            => values.Aggregate(Ok(), (acc, item) => acc.Plus(item));


        //Bind on VoidResult<TError>
        public static VoidResult<TError> Bind<TError>(this VoidResult<TError> value, Func<Unit, VoidResult<TError>> bindFunc)
            => value.Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), error => error.Fail());

        public static VoidResult Bind(this VoidResult<IEnumerable<string>> value, Func<Unit, VoidResult> bindFunc)
            => value.Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), error => error.Fail());

        public static Result<T, TError> Bind<T, TError>(this VoidResult<TError> value, Func<Unit, Result<T, TError>> bindFunc)
            => value.Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), error => error.Fail<T, TError>());

        public static Result<T> Bind<T>(this VoidResult<IEnumerable<string>> value, Func<Unit, Result<T>> bindFunc)
            => value.Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), error => error.Fail<T>());

        //Bind on VoidResult
        public static VoidResult Bind(this VoidResult value, Func<Unit, VoidResult> bindFunc)
            => value.Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), error => error.Fail());

        public static Result<T> Bind<T>(this VoidResult value, Func<Unit, Result<T>> bindFunc)
            => value.Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), error => error.Fail<T>());


        //Map on VoidResult<TError>
        public static Result<TResult, TError> Map<TResult, TError>(this VoidResult<TError> value, Func<Unit, TResult> mapFunc)
            => value.Map2(mapFunc, Id);

        public static Result<TResult, TErrorResult> Map2<TResult, TError, TErrorResult>(this VoidResult<TError> value, Func<Unit, TResult> mapFunc, Func<TError, TErrorResult> errorMapFunc)
            => value.Match(
                success => mapFunc.ThrowIfDefault(nameof(mapFunc))(success).Ok<TResult, TErrorResult>(),
                errors => errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(errors).Fail<TResult, TErrorResult>());

        //Map on VoidResult
        public static Result<TResult> Map<TResult>(this VoidResult value, Func<Unit, TResult> mapFunc)
            => value.Map2(mapFunc, Id);

        public static Result<TResult, TError> Map2<TResult, TError>(this VoidResult value, Func<Unit, TResult> mapFunc, Func<IEnumerable<string>, TError> errorMapFunc)
            => value.Match(
                success => mapFunc.ThrowIfDefault(nameof(mapFunc))(success).Ok<TResult, TError>(),
                errors => errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(errors).Fail<TResult, TError>());

        //Flatten on VoidResult<TError>
        public static VoidResult<TError> Flatten<TError>(this VoidResult<VoidResult<TError>> value)
            => value.Match(x => Ok<TError>(), Id);

        //Tee on VoidResult<TError>
        public static VoidResult<TError> Tee<TError>(this VoidResult<TError> value, Action action)
            => value.Map(unit=>
            {
                action.ThrowIfDefault(nameof(action))();
                return unit;
            });

        //Tee on VoidResult
        public static VoidResult Tee(this VoidResult value, Action action)
            => value.Map(unit =>
            {
                action.ThrowIfDefault(nameof(action))();
                return unit;
            });

    }
}
