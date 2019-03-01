using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resultful.Utils;
using TaskExt;
using OneOf;
using OneOf.Types;

namespace Resultful
{
    public static partial class Result
    {
        //ToOneOf
        public static Task<OneOf<Unit, IEnumerable<string>>> ToOneOf<T>(this Task<VoidResult> value)
            => value.Map(x => x.ToOneOf());

        public static Task<OneOf<Unit, TError>> ToOneOf<T, TError>(this Task<VoidResult<TError>> value)
            => value.Map(x => x.ToOneOf());

        //Switch on Task<Result<T>>
        public static Task Switch<T>(this Task<VoidResult> value, Action<Unit> successFunc, Action<IEnumerable<string>> errorFunc)
            => value.Discard(item => item.Switch(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        public static Task SwitchAsync<T>(this Task<VoidResult> value, Func<Unit, Task> successFunc, Func<IEnumerable<string>, Task> errorFunc)
            => value.DiscardAsync(item => item.Match(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        //Match on Task<Option<T>>
        public static Task<TResult> MatchAsync<T, TResult>(this Task<VoidResult> value, Func<Unit, Task<TResult>> successFunc, Func<IEnumerable<string>, Task<TResult>> errorFunc)
            => value.Bind(item => item.MatchAsync(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        public static Task<TResult> Match<T, TResult>(this Task<VoidResult> value, Func<Unit, TResult> successFunc, Func<IEnumerable<string>, TResult> errorFunc)
            => value.Map(item => item.Match(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        //Switch on Task<Result<T, TError>>
        public static Task Switch<T, TError>(this Task<VoidResult<TError>> value, Action<Unit> successFunc, Action<TError> errorFunc)
            => value.Discard(item => item.Switch(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        public static Task SwitchAsync<T, TError>(this Task<VoidResult<TError>> value, Func<Unit, Task> successFunc, Func<TError, Task> errorFunc)
            => value.DiscardAsync(item => item.Match(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        //Match on Task<Result<T, TError>>
        public static Task<TResult> MatchAsync<T, TError, TResult>(this Task<VoidResult<TError>> value, Func<Unit, Task<TResult>> successFunc, Func<TError, Task<TResult>> errorFunc)
            => value.Bind(item => item.MatchAsync(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        public static Task<TResult> Match<T, TError, TResult>(this Task<VoidResult<TError>> value, Func<Unit, TResult> successFunc, Func<TError, TResult> errorFunc)
            => value.Map(item => item.Match(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        //MapAsync on VoidResult<TError>
        public static Task<Result<TResult, TError>> Map<TResult, TError>(this Task<VoidResult<TError>> value, Func<Unit, TResult> mapFunc)
            => value.Map(item => item.Map(mapFunc));

        public static Task<Result<TResult, TErrorResult>> Map2<TResult, TError, TErrorResult>(this Task<VoidResult<TError>> value, Func<TError, TErrorResult> errorMapFunc, Func<Unit, TResult> mapFunc)
            => value.Map(item => item.Map2(errorMapFunc, mapFunc));

        public static Task<Result<TResult, TError>> MapAsync<TResult, TError>(this Task<VoidResult<TError>> value, Task<TResult> mapFunc)
            => value.Bind(item => item.MapAsync(mapFunc));

        public static Task<Result<TResult, TError>> Map<TResult, TError>(this Task<VoidResult<TError>> value, TResult mapFunc)
            => value.Map(item => item.Map(mapFunc));

        public static Task<Result<T, TError>> MapAsync<T, TError>(this Task<VoidResult<TError>> value, Func<Unit, Task<T>> bindFunc)
            => value.Bind(item => item.MapAsync(bindFunc));

        public static Task<Result<TResult, TErrorResult>> Map2Async<TResult, TError, TErrorResult>(this Task<VoidResult<TError>> value, Func<TError, Task<TErrorResult>> errorMapFunc, Func<Task<TResult>> mapFunc)
            => value.Bind(item => item.Map2Async(errorMapFunc, mapFunc));

        //MapAsync on VoidResult
        public static Task<Result<TResult>> Map<TResult>(this Task<VoidResult> value, Func<Unit, TResult> mapFunc)
            => value.Map(item => item.Map(mapFunc));

        public static Task<Result<TResult, TError>> Map2<TResult, TError>(this Task<VoidResult> value, Func<IEnumerable<string>, TError> errorMapFunc, Func<Unit, TResult> mapFunc)
            => value.Map(item => item.Map2(errorMapFunc, mapFunc));

        public static Task<Result<TResult>> MapAsync<TResult>(this Task<VoidResult> value, Task<TResult> mapFunc)
            => value.Bind(item => item.MapAsync(mapFunc));

        public static Task<Result<T>> Map<T>(this Task<VoidResult> value, T bindFunc)
            => value.Map(item => item.Map(bindFunc));

        public static Task<Result<T>> MapAsync<T>(this Task<VoidResult> value, Func<Unit, Task<T>> bindFunc)
            => value.Bind(item => item.MapAsync(bindFunc));

        public static Task<Result<TResult, TError>> Map2Async<TResult, TError>(this Task<VoidResult> value, Func<IEnumerable<string>, Task<TError>> errorMapFunc, Func<Task<TResult>> mapFunc)
            => value.Bind(item => item.Map2Async(errorMapFunc, mapFunc));

        //MapErrorAync on VoidResult
        public static async Task<VoidResult<TErrorResult>> MapError<TErrorResult>(this Task<VoidResult> value, Func<IEnumerable<string>, TErrorResult> errorMapFunc)
            => await value.Map(item => item.Map2(errorMapFunc, Id)).ConfigureAwait(false);

        public static async Task<VoidResult<TErrorResult>> MapErrorAsync<TErrorResult>(this Task<VoidResult> value, Func<IEnumerable<string>, Task<TErrorResult>> errorMapFunc)
            => await value.Bind(item => item.Map2Async(errorMapFunc, () => Task.FromResult(Unit.Value))).ConfigureAwait(false);

        //MapErrorAsync on VoidResult<Terror>
        public static async Task<VoidResult<TErrorResult>> MapError<TError, TErrorResult>(this Task<VoidResult<TError>> value, Func<TError, TErrorResult> errorMapFunc)
            => await value.Map(item => item.MapError(errorMapFunc));

        public static Task<VoidResult<TErrorResult>> MapErrorAsync<TError, TErrorResult>(this Task<VoidResult<TError>> value, Func<TError, Task<TErrorResult>> errorMapFunc)
            => value.Bind(item => item.MapErrorAsync(errorMapFunc));

        public static Task<VoidResult> MapError(this Task<VoidResult> value, Func<IEnumerable<string>, IEnumerable<string>> errorMapFunc)
            => value.Map(item => item.MapError(errorMapFunc));

        public static Task<VoidResult> MapErrorAsync(this Task<VoidResult> value, Func<IEnumerable<string>, Task<IEnumerable<string>>> errorMapFunc)
            => value.Bind(item => item.MapErrorAsync(errorMapFunc));

        //BindAsync on VoidResult<TError>
        public static Task<VoidResult<TError>> Bind<TError>(this Task<VoidResult<TError>> value, Func<Unit, VoidResult<TError>> bindFunc)
            => value.Map(item => item.Bind(bindFunc));

        public static Task<VoidResult<TError>> BindAsync<TError>(this Task<VoidResult<TError>> value, Func<Unit, Task<VoidResult<TError>>> bindFunc)
            => value.Bind(item => item.BindAsync(bindFunc));

        //BindAsync on VoidResult
        public static Task<VoidResult> Bind(this Task<VoidResult> value, Func<Unit, VoidResult> bindFunc)
            => value.Map(item => item.Bind(bindFunc));

        public static Task<VoidResult> BindAsync(this Task<VoidResult> value, Func<Unit, Task<VoidResult>> bindFunc)
            => value.Bind(item => item.BindAsync(bindFunc));

        //BindValueAsync on VoidResult<TError>
        public static Task<Result<T, TError>> BindValue<T, TError>(this Task<VoidResult<TError>> value, Func<Unit, Result<T, TError>> bindFunc)
            => value.Map(item => item.BindValue(bindFunc));

        public static Task<Result<T, TError>> BindValueAsync<T, TError>(this Task<VoidResult<TError>> value, Func<Unit, Task<Result<T, TError>>> bindFunc)
            => value.Bind(item => item.BindValueAsync(bindFunc));

        //BindValueAsync on VoidResult
        public static Task<Result<T>> BindValue<T>(this Task<VoidResult> value, Func<Unit, Result<T>> bindFunc)
            => value.Map(item => item.BindValue(bindFunc));

        public static Task<Result<T>> BindValueAsync<T>(this Task<VoidResult> value, Func<Unit, Task<Result<T>>> bindFunc)
            => value.Bind(item => item.BindValueAsync(bindFunc));

        //TeeAsync on VoidResult<TError>
        public static Task<VoidResult<TError>> Tee<TError>(this Task<VoidResult<TError>> value, Action<Unit> action)
            => value.Map(item => item.Tee(action));

        public static Task<VoidResult<TError>> TeeAsync<TError>(this Task<VoidResult<TError>> value, Func<Unit ,Task> asyncFunc)
            => value.Tee(item => item.TeeAsync(asyncFunc).Discard());

        //TeeAsync on VoidResult
        public static Task<VoidResult> Tee(this Task<VoidResult> value, Action<Unit> action)
            => value.Map(item => item.Tee(action));

        public static Task<VoidResult> TeeAsync(this Task<VoidResult> value, Func<Unit, Task> asyncFunc)
            => value.Tee(item => item.TeeAsync(asyncFunc).Discard());

        //TeeErrorAsync on VoidResult<TError>
        public static Task<VoidResult<TError>> TeeError<TError>(this Task<VoidResult<TError>> value, Action<TError> action)
            => value.Map(item => item.TeeError(action));

        public static Task<VoidResult<TError>> TeeErrorAsync<TError>(this Task<VoidResult<TError>> value, Func<TError, Task> asyncFunc)
            => value.Tee(item => item.TeeErrorAsync(asyncFunc).Discard());

        //TeeErrorAsync on VoidResult
        public static Task<VoidResult> TeeError(this Task<VoidResult> value, Action<IEnumerable<string>> action)
            => value.Map(item => item.TeeError(action));

        public static Task<VoidResult> TeeErrorAsync(this Task<VoidResult> value, Func<IEnumerable<string>, Task> asyncFunc)
            => value.Tee(item => item.TeeErrorAsync(asyncFunc).Discard());


        //FlattenAsync on VoidResult<T>
        public static Task<VoidResult<TError>> Flatten<TError>(this Task<VoidResult<VoidResult<TError>>> value)
            => value.Map(item => item.Flatten());
    }
}
