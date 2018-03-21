using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OneOf.ROP
{
    public static partial class Result
    {
        //Builder for VoidResult<TError>
        public static VoidResult<TError> Fail<TError>(this TError value)
            => new VoidResult<TError>(value);

        public static VoidResult<IEnumerable<TError>> Fail<TError>(params TError[] errors)
            => new VoidResult<IEnumerable<TError>>(errors);

        public static VoidResult<TError> Ok<TError>()
            => new VoidResult<TError>(Unit.Value);

        //Builder for VoidResult
        public static VoidResult Fail(this IEnumerable<string> value)
            => new VoidResult(value);

        public static VoidResult Fail(params string[] errors)
            => new VoidResult(errors);

        public static VoidResult Ok()
            => new VoidResult(Unit.Value);

        //Plus on VoidResult<TError>
        public static VoidResult<TError> Plus<TError>(this VoidResult<TError> left, VoidResult<TError> right, Func<TError, TError, TError> mergeFunc)
            => left.Match(
                leftValue => right.Match(rightValue => Ok<TError>(), Fail),
                error => right.Match(rightValue => Fail(error), otherError => Fail(mergeFunc(error, otherError)))
            );

        public static VoidResult<TError> Plus<TError>(this VoidResult<TError> left, VoidResult<TError> right) where TError : IPlus<TError>
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

        public static VoidResult<TError> Fold<TError>(this IEnumerable<VoidResult<TError>> values) where TError : IPlus<TError>
            => values.Aggregate(Ok<TError>(), (acc, item) => acc.Plus(item));

        //Fold on VoidResult
        public static VoidResult Fold(this IEnumerable<VoidResult> values)
            => values.Aggregate(Ok(), (acc, item) => acc.Plus(item));


        //Bind on VoidResult<TError>
        public static VoidResult<TError> Bind<TError>(this VoidResult<TError> value, Func<VoidResult<TError>> bindFunc)
            => value.Match(x => bindFunc(), error => error.Fail());

        public static VoidResult Bind(this VoidResult<IEnumerable<string>> value, Func<VoidResult> bindFunc)
            => value.Match(x => bindFunc(), error => error.Fail());

        public static Result<T, TError> Bind<T, TError>(this VoidResult<TError> value, Func<Result<T, TError>> bindFunc)
            => value.Match(x => bindFunc(), error => error.Fail<T, TError>());

        public static Result<T> Bind<T>(this VoidResult<IEnumerable<string>> value, Func<Result<T>> bindFunc)
            => value.Match(x => bindFunc(), error => error.Fail<T>());

        //Bind on VoidResult
        public static VoidResult Bind(this VoidResult value, Func<VoidResult> bindFunc)
            => value.Match(x => bindFunc(), error => error.Fail());

        public static Result<T> Bind<T>(this VoidResult value, Func<Result<T>> bindFunc)
            => value.Match(x => bindFunc(), error => error.Fail<T>());


        //Map on VoidResult<TError>
        public static Result<TResult, TError> Map<TResult, TError>(this VoidResult<TError> value, Func<TResult> mapFunc)
            => value.Map2(mapFunc, Id);

        public static Result<TResult, TErrorResult> Map2<TResult, TError, TErrorResult>(this VoidResult<TError> value, Func<TResult> mapFunc, Func<TError, TErrorResult> errorMapFunc)
            => value.Match(
                success => mapFunc().Ok<TResult, TErrorResult>(),
                errors => errorMapFunc(errors).Fail<TResult, TErrorResult>());

        //Map on VoidResult
        public static Result<TResult> Map<TResult>(this VoidResult value, Func<TResult> mapFunc)
            => value.Map2(mapFunc, Id);

        public static Result<TResult, TError> Map2<TResult, TError>(this VoidResult value, Func<TResult> mapFunc, Func<IEnumerable<string>, TError> errorMapFunc)
            => value.Match(
                success => mapFunc().Ok<TResult, TError>(),
                errors => errorMapFunc(errors).Fail<TResult, TError>());

        //Flatten on VoidResult<TError>
        public static VoidResult<TError> Flatten<TError>(this VoidResult<VoidResult<TError>> value)
            => value.Match(x => Ok<TError>(), Id);

        //Tee on VoidResult<TError>
        public static VoidResult<TError> Tee<TError>(this VoidResult<TError> value, Action action)
            => value.Map(() =>
            {
                action();
                return Unit.Value;
            });

        //Tee on VoidResult
        public static VoidResult Tee(this VoidResult value, Action action)
            => value.Map(() =>
            {
                action();
                return Unit.Value;
            });

        //MapAsync on VoidResult<TError>
        public static Task<Result<TResult, TError>> MapAsync<TResult, TError>(this VoidResult<TError> value, Func<Task<TResult>> mapFunc)
            => value.Map2Async(mapFunc, Task.FromResult);

        public static Task<Result<TResult, TErrorResult>> Map2Async<TResult, TError, TErrorResult>(this VoidResult<TError> value, Func<Task<TResult>> mapFunc, Func<TError, Task<TErrorResult>> errorMapFunc)
            => value.Match(
                async success => (await mapFunc().ConfigureAwait(false)).Ok<TResult, TErrorResult>(),
                async errors => (await errorMapFunc(errors).ConfigureAwait(false)).Fail<TResult, TErrorResult>());

        public static Task<Result<TResult, TError>> MapAsync<TResult, TError>(this Task<VoidResult<TError>> value, Func<TResult> mapFunc)
            => value.WrapAsync(item => item.Map(mapFunc));

        public static Task<Result<TResult, TErrorResult>> Map2Async<TResult, TError, TErrorResult>(this Task<VoidResult<TError>> value, Func<TResult> mapFunc, Func<TError, TErrorResult> errorMapFunc)
            => value.WrapAsync(item => item.Map2(mapFunc, errorMapFunc));

        public static Task<Result<TResult, TError>> MapAsync<TResult, TError>(this Task<VoidResult<TError>> value, Func<Task<TResult>> mapFunc)
            => value.WrapAsync(item => item.MapAsync(mapFunc));

        public static Task<Result<TResult, TErrorResult>> Map2Async<TResult, TError, TErrorResult>(this Task<VoidResult<TError>> value, Func<Task<TResult>> mapFunc, Func<TError, Task<TErrorResult>> errorMapFunc)
            => value.WrapAsync(item => item.Map2Async(mapFunc, errorMapFunc));

        //MapAsync on VoidResult
        public static async Task<Result<TResult>> MapAsync<TResult>(this VoidResult value, Func<Task<TResult>> mapFunc)
            => await value.Map2Async(mapFunc, Task.FromResult).ConfigureAwait(false);

        public static Task<Result<TResult>> MapAsync<TResult>(this Task<VoidResult> value, Func<TResult> mapFunc)
            => value.WrapAsync(item => item.Map(mapFunc));

        public static Task<Result<TResult, TError>> Map2Async<TResult, TError>(this Task<VoidResult> value, Func<TResult> mapFunc, Func<IEnumerable<string>, TError> errorMapFunc)
            => value.WrapAsync(item => item.Map2(mapFunc, errorMapFunc));

        public static Task<Result<TResult>> MapAsync<TResult>(this Task<VoidResult> value, Func<Task<TResult>> mapFunc)
            => value.WrapAsync(item => item.MapAsync(mapFunc));

        public static Task<Result<TResult, TError>> Map2Async<TResult, TError>(this Task<VoidResult> value, Func<Task<TResult>> mapFunc, Func<IEnumerable<string>, Task<TError>> errorMapFunc)
            => value.WrapAsync(item => item.Map2Async(mapFunc, errorMapFunc));

        //BindAsync on VoidResult<TError>
        public static Task<VoidResult<TError>> BindAsync<TError>(this VoidResult<TError> value, Func<Task<VoidResult<TError>>> bindFunc)
            => value.Match(x => bindFunc(), error => Task.FromResult(error.Fail()));

        public static Task<VoidResult> BindAsync(this VoidResult<IEnumerable<string>> value, Func<Task<VoidResult>> bindFunc)
            => value.Match(x => bindFunc(), error => Task.FromResult(error.Fail()));

        public static Task<Result<T, TError>> BindAsync<T, TError>(this VoidResult<TError> value, Func<Task<Result<T, TError>>> bindFunc)
            => value.Match(x => bindFunc(), error => Task.FromResult(error.Fail<T, TError>()));

        public static Task<Result<T>> BindAsync<T>(this VoidResult<IEnumerable<string>> value, Func<Task<Result<T>>> bindFunc)
            => value.Match(x => bindFunc(), error => Task.FromResult(error.Fail<T>()));

        public static Task<VoidResult<TError>> BindAsync<TError>(this Task<VoidResult<TError>> value, Func<VoidResult<TError>> bindFunc)
            => value.WrapAsync(item => item.Bind(bindFunc));

        public static Task<VoidResult> BindAsync(this Task<VoidResult<IEnumerable<string>>> value, Func<VoidResult> bindFunc)
            => value.WrapAsync(item => item.Bind(bindFunc));

        public static Task<Result<T, TError>> BindAsync<T, TError>(this Task<VoidResult<TError>> value, Func<Result<T, TError>> bindFunc)
            => value.WrapAsync(item => item.Bind(bindFunc));

        public static Task<Result<T>> BindAsync<T>(this Task<VoidResult<IEnumerable<string>>> value, Func<Result<T>> bindFunc)
            => value.WrapAsync(item => item.Bind(bindFunc));

        public static Task<VoidResult<TError>> BindAsync<TError>(this Task<VoidResult<TError>> value, Func<Task<VoidResult<TError>>> bindFunc)
            => value.WrapAsync(item => item.BindAsync(bindFunc));

        public static Task<VoidResult> BindAsync(this Task<VoidResult<IEnumerable<string>>> value, Func<Task<VoidResult>> bindFunc)
            => value.WrapAsync(item => item.BindAsync(bindFunc));

        public static Task<Result<T, TError>> BindAsync<T, TError>(this Task<VoidResult<TError>> value, Func<Task<Result<T, TError>>> bindFunc)
            => value.WrapAsync(item => item.BindAsync(bindFunc));

        public static Task<Result<T>> BindAsync<T>(this Task<VoidResult<IEnumerable<string>>> value, Func<Task<Result<T>>> bindFunc)
            => value.WrapAsync(item => item.BindAsync(bindFunc));

        //BindAsync on VoidResult
        public static Task<VoidResult> BindAsync(this VoidResult value, Func<Task<VoidResult>> bindFunc)
            => value.Match(x => bindFunc(), error => Task.FromResult(error.Fail()));

        public static Task<Result<T>> BindAsync<T>(this VoidResult value, Func<Task<Result<T>>> bindFunc)
            => value.Match(x => bindFunc(), error => Task.FromResult(error.Fail<T>()));

        public static Task<VoidResult> BindAsync(this Task<VoidResult> value, Func<VoidResult> bindFunc)
            => value.WrapAsync(item => item.Bind(bindFunc));

        public static Task<Result<T>> BindAsync<T>(this Task<VoidResult> value, Func<Result<T>> bindFunc)
            => value.WrapAsync(item => item.Bind(bindFunc));

        public static Task<VoidResult> BindAsync(this Task<VoidResult> value, Func<Task<VoidResult>> bindFunc)
            => value.WrapAsync(item => item.BindAsync(bindFunc));

        public static Task<Result<T>> BindAsync<T>(this Task<VoidResult> value, Func<Task<Result<T>>> bindFunc)
            => value.WrapAsync(item => item.BindAsync(bindFunc));


        //TeeAsync on VoidResult<TError>
        public static async Task<VoidResult<TError>> TeeAsync<TError>(this VoidResult<TError> value, Func<Task> asyncFunc)
            => await value.MapAsync(async () =>
            {
                await asyncFunc().ConfigureAwait(false);
                return Unit.Value;
            }).ConfigureAwait(false);

        public static Task<VoidResult<TError>> TeeAsync<TError>(this Task<VoidResult<TError>> value, Action action)
            => value.WrapAsync(item => item.Tee(action));

        public static Task<VoidResult<TError>> TeeAsync<TError>(this Task<VoidResult<TError>> value, Func<Task> asyncFunc)
            => value.WrapAsync(item => item.TeeAsync(asyncFunc).AsTask());

        //TeeAsync on VoidResult
        public static async Task<VoidResult> TeeAsync(this VoidResult value, Func<Task> asyncFunc)
            => await value.MapAsync(async () =>
            {
                await asyncFunc().ConfigureAwait(false);
                return Unit.Value;
            }).ConfigureAwait(false);

        public static Task<VoidResult> TeeAsync(this Task<VoidResult> value, Action action)
            => value.WrapAsync(item => item.Tee(action));

        public static Task<VoidResult> TeeAsync(this Task<VoidResult> value, Func<Task> asyncFunc)
            => value.WrapAsync(item => item.TeeAsync(asyncFunc).AsTask());

        //FlattenAsync on VoidResult<T>
        public static Task<VoidResult<TError>> FlattenAsync<TError>(this Task<VoidResult<VoidResult<TError>>> value)
            => value.WrapAsync(item => item.Flatten());
    }
}
