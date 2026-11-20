#nullable enable
using System;
using System.Threading.Tasks;

namespace Resultful
{
    [Resultful.CodeAnalysis.MustObserve]
    public readonly partial struct Option<T>
    {
        //Implicit Converters
        public static implicit operator Option<T>(T value) => throw null!;
        public static implicit operator Option<T>(None value) => throw null!;
        //Builder
        public static Option<T> None => throw null!;

        //Local Methods
        public void Switch(Action<T> successfulFunc, Action<None> noneFunc) => throw null!;
        public TResult Match<TResult>(Func<T, TResult> successfulFunc, Func<None, TResult> noneFunc) => throw null!;
        public Task SwitchAsync(Func<T, Task> someFunc, Func<None, Task> noneFunc) => throw null!;
        public Task<TResult> MatchAsync<TResult>(Func<T, Task<TResult>> someFunc, Func<None, Task<TResult>> noneFunc) => throw null!;
        public Option<TResult> Bind<TResult>(Func<T, Option<TResult>> bindFunc) => throw null!;
        public Task<Option<TResult>> BindAsync<TResult>(Func<T, Task<Option<TResult>>> bindFunc) => throw null!;
        public Option<TResult> Map<TResult>(Func<T, TResult> bindFunc) => throw null!;
        public Task<Option<TResult>> MapAsync<TResult>(Func<T, Task<TResult>> bindFunc) => throw null!;
        public Option<T> Tee(Action<T> teeAction) => throw null!;
        public Task<Option<T>> TeeAsync(Func<T, Task> asyncFunc) => throw null!;
        public TResult Fold<TResult>(TResult seed, Func<TResult, T, TResult> foldFunc) => throw null!;
        public Task<TResult> FoldAsync<TResult>(TResult seed, Func<TResult, T, Task<TResult>> foldFunc) => throw null!;
        public Option<TResult> FoldUntil<TResult>(TResult seed, Func<TResult, T, Option<TResult>> foldFunc) => throw null!;
        public Task<Option<TResult>> FoldUntilAsync<TResult>(TResult seed, Func<TResult, T, Task<Option<TResult>>> foldFunc) => throw null!;
        public Option<T> Or(Func<Option<T>> otherFunc) => throw null!;
        public T Or(Func<T> otherFunc) => throw null!;
        public Option<T> Or(Option<T> other) => throw null!;
        public T Or(T otherValue) => throw null!;
        public Task<Option<T>> OrAsync(Func<Task<Option<T>>> otherFunc) => throw null!;
        public Task<T> OrAsync(Func<Task<T>> other) => throw null!;
        public Task<Option<T>> OrAsync(Task<Option<T>> other) => throw null!;
        public Task<T> OrAsync(Task<T> otherValue) => throw null!;
        public Option<TCast> CastError<TCast>() => throw null!;
    }
}
