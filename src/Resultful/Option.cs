using System;
using System.Threading.Tasks;
using Resultful.Utils;

namespace Resultful
{
    /// <summary>A <typeparamref name="T"/> value, or none.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <remarks>The zero value (<c>default</c>) is <see cref="None"/> (decision 10): unlike a result's, it has a safe meaning, so it doesn't throw.</remarks>
    [CodeAnalysis.MustObserve]
    public struct Option<T>
    {
        //Private Members
        private readonly T _value;
        // State.Zero only for the zero value (default), which reads as None (decision 10). None is the unit struct, so a
        // None holds nothing else (decision 1).
        private readonly byte _state;

        //Constructors
        private Option(T value)
        {
            _value = value;
            _state = State.Success;
        }

        private Option(None value)
        {
            _value = default!;
            _state = State.Failure;
        }

        //Implicit Converters
        /// <summary>Converts a value to an option that has it.</summary>
        /// <param name="value">The value.</param>
        /// <remarks>A <c>null</c> value still gives an option with a value.</remarks>
        public static implicit operator Option<T>(T value)
            => new Option<T>(value);

        /// <summary>Converts None to an option with no value.</summary>
        /// <param name="value">The none value.</param>
        public static implicit operator Option<T>(None value)
            => new Option<T>(value);

        //Builder
        /// <summary>An option with no value.</summary>
        public static Option<T> None
            => new Option<T>(new None());

        //Local Methods
        /// <summary>Calls <paramref name="successfulFunc"/> with the value if there is one, or <paramref name="noneFunc"/> if the option is None.</summary>
        /// <param name="successfulFunc">Called with the value.</param>
        /// <param name="noneFunc">Called if the option is None.</param>
        public void Switch(Action<T> successfulFunc, Action<None> noneFunc)
        {
            successfulFunc.ThrowIfDefault(nameof(successfulFunc));
            noneFunc.ThrowIfDefault(nameof(noneFunc));
            if (_state != State.Success) noneFunc(default); else successfulFunc(_value);
        }

        /// <summary>Calls <paramref name="successfulFunc"/> with the value if there is one, or <paramref name="noneFunc"/> if the option is None, and returns what it returns.</summary>
        /// <typeparam name="TResult">The type both functions return.</typeparam>
        /// <param name="successfulFunc">Called with the value.</param>
        /// <param name="noneFunc">Called if the option is None.</param>
        public TResult Match<TResult>(Func<T, TResult> successfulFunc, Func<None, TResult> noneFunc)
        {
            successfulFunc.ThrowIfDefault(nameof(successfulFunc));
            noneFunc.ThrowIfDefault(nameof(noneFunc));
            return _state != State.Success ? noneFunc(default) : successfulFunc(_value);
        }

        /// <summary>Calls <paramref name="someFunc"/> with the value if there is one, or <paramref name="noneFunc"/> if the option is None, and returns a task that completes when the call's task does.</summary>
        /// <param name="someFunc">Called with the value.</param>
        /// <param name="noneFunc">Called if the option is None.</param>
        public Task SwitchAsync(Func<T, Task> someFunc, Func<None, Task> noneFunc)
        {
            someFunc.ThrowIfDefault(nameof(someFunc));
            noneFunc.ThrowIfDefault(nameof(noneFunc));
            return _state != State.Success ? noneFunc(default) : someFunc(_value);
        }

        /// <summary>Calls <paramref name="someFunc"/> with the value if there is one, or <paramref name="noneFunc"/> if the option is None, and returns its task.</summary>
        /// <typeparam name="TResult">The type both functions return.</typeparam>
        /// <param name="someFunc">Called with the value.</param>
        /// <param name="noneFunc">Called if the option is None.</param>
        public Task<TResult> MatchAsync<TResult>(Func<T, Task<TResult>> someFunc, Func<None, Task<TResult>> noneFunc)
        {
            someFunc.ThrowIfDefault(nameof(someFunc));
            noneFunc.ThrowIfDefault(nameof(noneFunc));
            return _state != State.Success ? noneFunc(default) : someFunc(_value);
        }

        /// <summary>Two options are equal when both have equal values or both are None. The zero value is None.</summary>
        /// <param name="obj">The object to compare with.</param>
        /// <remarks>Values and errors are compared with <c>object.Equals</c>, as in 0.2.</remarks>
        public override bool Equals(object? obj)
            => obj is Option<T> other && (_state == State.Success) == (other._state == State.Success)
                && (_state != State.Success || Equals(_value, other._value));

        /// <summary>Compares two options with <see cref="Equals(object)"/>.</summary>
        /// <param name="left">The first option.</param>
        /// <param name="right">The second option.</param>
        public static bool operator ==(Option<T> left, Option<T> right) => left.Equals(right);

        /// <summary>Compares two options with <see cref="Equals(object)"/>.</summary>
        /// <param name="left">The first option.</param>
        /// <param name="right">The second option.</param>
        public static bool operator !=(Option<T> left, Option<T> right) => !left.Equals(right);

        /// <summary>A hash of the case and its value.</summary>
        public override int GetHashCode()
            => _state != State.Success ? State.Hash(default(None), 1) : State.Hash(_value, 0);

        /// <summary>If the option has a value, returns what <paramref name="bindFunc"/> returns for it. None stays None and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="bindFunc">Returns the next option from the value.</param>
        public Option<TResult> Bind<TResult>(Func<T, Option<TResult>> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), none => none);

        /// <summary>If the option has a value, returns what <paramref name="bindFunc"/> returns for it. None stays None and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="bindFunc">Returns the next option from the value.</param>
        public Task<Option<TResult>> BindAsync<TResult>(Func<T, Task<Option<TResult>>> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), none => Task.FromResult<Option<TResult>>(none));

        /// <summary>If the option has a value, returns an option with the value <paramref name="bindFunc"/> gives for it. None stays None.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="bindFunc">Maps the value.</param>
        public Option<TResult> Map<TResult>(Func<T, TResult> bindFunc)
            => Match(value => bindFunc.ThrowIfDefault(nameof(bindFunc))(value).Some(), none => none);

        /// <summary>If the option has a value, returns an option with the value <paramref name="bindFunc"/> gives for it. None stays None.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="bindFunc">Maps the value.</param>
        public Task<Option<TResult>> MapAsync<TResult>(Func<T, Task<TResult>> bindFunc)
            => Match(
                async item2 => (await bindFunc.ThrowIfDefault(nameof(bindFunc))(item2).ConfigureAwait(false)).Some(),
                none => Task.FromResult<Option<TResult>>(none));

        /// <summary>Calls <paramref name="teeAction"/> with the value if there is one, and returns the option unchanged.</summary>
        /// <param name="teeAction">Called with the value.</param>
        public Option<T> Tee(Action<T> teeAction)
            => Map(value =>
            {
                teeAction.ThrowIfDefault(nameof(teeAction))(value);
                return value;
            });

        /// <summary>Calls <paramref name="asyncFunc"/> with the value if there is one, awaits it, and returns the option unchanged.</summary>
        /// <param name="asyncFunc">Called with the value.</param>
        public Task<Option<T>> TeeAsync(Func<T, Task> asyncFunc)
            => MapAsync(async x =>
            {
                await asyncFunc.ThrowIfDefault(nameof(asyncFunc))(x).ConfigureAwait(false);
                return x;
            });

        /// <summary>If the option has a value, returns what <paramref name="foldFunc"/> returns for <paramref name="seed"/> and the value. Otherwise returns <paramref name="seed"/>.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <param name="seed">The starting value.</param>
        /// <param name="foldFunc">Combines the seed with the value.</param>
        public TResult Fold<TResult>(TResult seed, Func<TResult, T, TResult> foldFunc)
            => Match(
                item => foldFunc.ThrowIfDefault(nameof(foldFunc))(seed, item),
                _ => seed);

        /// <summary>If the option has a value, returns what <paramref name="foldFunc"/> returns for <paramref name="seed"/> and the value. Otherwise returns <paramref name="seed"/>.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <param name="seed">The starting value.</param>
        /// <param name="foldFunc">Combines the seed with the value.</param>
        public Task<TResult> FoldAsync<TResult>(TResult seed, Func<TResult, T, Task<TResult>> foldFunc)
            => Match(
                item => foldFunc.ThrowIfDefault(nameof(foldFunc))(seed, item),
                _ => Task.FromResult(seed));

        /// <summary>If the option has a value, returns what <paramref name="foldFunc"/> returns for <paramref name="seed"/> and the value. Otherwise returns <paramref name="seed"/> as an option with a value.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <param name="seed">The starting value.</param>
        /// <param name="foldFunc">Combines the seed with the value.</param>
        public Option<TResult> FoldUntil<TResult>(TResult seed, Func<TResult, T, Option<TResult>> foldFunc)
            => Match(
                item => foldFunc.ThrowIfDefault(nameof(foldFunc))(seed, item),
                _ => seed.Some());

        /// <summary>If the option has a value, returns what <paramref name="foldFunc"/> returns for <paramref name="seed"/> and the value. Otherwise returns <paramref name="seed"/> as an option with a value.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <param name="seed">The starting value.</param>
        /// <param name="foldFunc">Combines the seed with the value.</param>
        public Task<Option<TResult>> FoldUntilAsync<TResult>(TResult seed, Func<TResult, T, Task<Option<TResult>>> foldFunc)
            => Match(
                item => foldFunc.ThrowIfDefault(nameof(foldFunc))(seed, item),
                _ => Task.FromResult<Option<TResult>>(seed));


        /// <summary>Returns the option if it has a value, otherwise what <paramref name="otherFunc"/> returns.</summary>
        /// <param name="otherFunc">Called only if the option is None.</param>
        public Option<T> Or(Func<Option<T>> otherFunc)
            => Match(x => x, _ => otherFunc.ThrowIfDefault(nameof(otherFunc))());

        /// <summary>Returns the value if there is one, otherwise what <paramref name="otherFunc"/> returns.</summary>
        /// <param name="otherFunc">Called only if the option is None.</param>
        public T Or(Func<T> otherFunc)
            => Match(x => x, _ => otherFunc.ThrowIfDefault(nameof(otherFunc))());

        /// <summary>Returns the option if it has a value, otherwise <paramref name="other"/>.</summary>
        /// <param name="other">Used if the option is None.</param>
        public Option<T> Or(Option<T> other)
            => Match(x => x, _ => other);

        /// <summary>Returns the value if there is one, otherwise <paramref name="otherValue"/>.</summary>
        /// <param name="otherValue">Used if the option is None.</param>
        public T Or(T otherValue)
            => Match(x => x, _ => otherValue);

        /// <summary>Returns the option if it has a value, otherwise what <paramref name="otherFunc"/> returns.</summary>
        /// <param name="otherFunc">Called only if the option is None.</param>
        public Task<Option<T>> OrAsync(Func<Task<Option<T>>> otherFunc)
            => Match(
                x => Task.FromResult<Option<T>>(x),
                _ => otherFunc.ThrowIfDefault(nameof(otherFunc))());

        /// <summary>Returns the value if there is one, otherwise what <paramref name="other"/> returns.</summary>
        /// <param name="other">Called only if the option is None.</param>
        public Task<T> OrAsync(Func<Task<T>> other)
            => Match(
                Task.FromResult,
                _ => other());

        /// <summary>Returns a completed task holding the option if it has a value, otherwise <paramref name="other"/> itself.</summary>
        /// <param name="other">Returned as it is if the option is None. It is never awaited here, and it runs either way.</param>
        public Task<Option<T>> OrAsync(Task<Option<T>> other)
            => Match(
                x => Task.FromResult<Option<T>>(x),
                _ => other);

        /// <summary>Returns a completed task holding the value if there is one, otherwise <paramref name="otherValue"/> itself.</summary>
        /// <param name="otherValue">Returned as it is if the option is None. It is never awaited here, and it runs either way.</param>
        public Task<T> OrAsync(Task<T> otherValue)
            => Match(
                Task.FromResult,
                _ => otherValue);

        /// <summary>If the option has a value, casts it to <typeparamref name="TCast"/>. None stays None.</summary>
        /// <typeparam name="TCast">The type to cast the value to.</typeparam>
        /// <remarks>Despite the name, this casts the value; an option has no error. Throws <see cref="System.InvalidCastException"/> if the value isn't a <typeparamref name="TCast"/>.</remarks>
        public Option<TCast> CastError<TCast>() => Map(x => (TCast)(object)x!);

    }
}
