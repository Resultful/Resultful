#nullable enable
using System;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using FSharpUnit = Microsoft.FSharp.Core.Unit;

namespace Resultful.FSharp
{
    /// <summary>
    /// Conversions between Resultful's types and FSharp.Core's <c>Result</c>, <c>option</c> and <c>voption</c>
    /// (decision 19). From F#, call them as methods (<c>r.ToFSharp()</c>, <c>fsResult.ToResultful()</c>) or as
    /// functions (<c>r |&gt; FSharpInterop.ToFSharp</c>).
    /// </summary>
    /// <remarks>
    /// Values and error objects are kept by reference; a validation's error list is copied into a new list, in order.
    /// A result's or a validation's zero value (<c>default</c>) has no case, so converting it throws
    /// <see cref="InvalidOperationException"/>, as every other member that reads it does. Converting back to Resultful
    /// goes through <c>Result.Fail</c> and <c>Validation.Fail</c>, so a <c>null</c> error, an empty error list, or (on
    /// <c>net11.0</c>) an error union holding no case, is refused as it is there.
    /// Going back gives the typed <c>Result&lt;T, TError&gt;</c> and <c>Validation&lt;T, TError&gt;</c>; for
    /// <c>Result&lt;T&gt;</c> and <c>Validation&lt;T&gt;</c>, follow with the core's <c>AsResult()</c> or
    /// <c>AsValidation()</c>. No overload returning <c>Result&lt;T&gt;</c> or <c>Validation&lt;T&gt;</c> will be added:
    /// it would silently change the type existing calls return.
    /// </remarks>
    public static class FSharpInterop
    {
        //Result -> F#
        /// <summary>Returns the result as an F# <c>Result</c>: <c>Ok</c> with the value, or <c>Error</c> with the error.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The result.</param>
        /// <exception cref="InvalidOperationException"><paramref name="value"/> is the zero value (<c>default</c>).</exception>
        public static FSharpResult<T, TError> ToFSharp<T, TError>(this Result<T, TError> value)
            => value.Match(FSharpResult<T, TError>.NewOk, FSharpResult<T, TError>.NewError);

        /// <summary>Returns the result as an F# <c>Result</c>: <c>Ok</c> with the value, or <c>Error</c> with the error.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The result.</param>
        /// <exception cref="InvalidOperationException"><paramref name="value"/> is the zero value (<c>default</c>).</exception>
        public static FSharpResult<T, Error> ToFSharp<T>(this Result<T> value)
            => value.Match(FSharpResult<T, Error>.NewOk, FSharpResult<T, Error>.NewError);

        /// <summary>Returns a step with no value as an F# <c>Result&lt;unit, 'E&gt;</c>: <c>Ok ()</c>, or <c>Error</c> with the error.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The result.</param>
        /// <exception cref="InvalidOperationException"><paramref name="value"/> is the zero value (<c>default</c>).</exception>
        public static FSharpResult<FSharpUnit?, TError> ToFSharpUnit<TError>(this Result<Unit, TError> value)
            => value.Match(_ => FSharpResult<FSharpUnit?, TError>.NewOk(null), FSharpResult<FSharpUnit?, TError>.NewError);

        /// <summary>Returns a step with no value as an F# <c>Result&lt;unit, Error&gt;</c>: <c>Ok ()</c>, or <c>Error</c> with the error.</summary>
        /// <param name="value">The result.</param>
        /// <exception cref="InvalidOperationException"><paramref name="value"/> is the zero value (<c>default</c>).</exception>
        public static FSharpResult<FSharpUnit?, Error> ToFSharpUnit(this Result<Unit> value)
            => value.Match(_ => FSharpResult<FSharpUnit?, Error>.NewOk(null), FSharpResult<FSharpUnit?, Error>.NewError);

        //Validation -> F#
        /// <summary>Returns the validation as an F# <c>Result</c>: <c>Ok</c> with the value, or <c>Error</c> with every error, in order.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of each error.</typeparam>
        /// <param name="value">The validation.</param>
        /// <exception cref="InvalidOperationException"><paramref name="value"/> is the zero value (<c>default</c>).</exception>
        public static FSharpResult<T, FSharpList<TError>> ToFSharp<T, TError>(this Validation<T, TError> value)
            => value.Match(FSharpResult<T, FSharpList<TError>>.NewOk, errors => FSharpResult<T, FSharpList<TError>>.NewError(ListModule.OfSeq(errors)));

        /// <summary>Returns the validation as an F# <c>Result</c>: <c>Ok</c> with the value, or <c>Error</c> with every error, in order.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The validation.</param>
        /// <exception cref="InvalidOperationException"><paramref name="value"/> is the zero value (<c>default</c>).</exception>
        public static FSharpResult<T, FSharpList<Error>> ToFSharp<T>(this Validation<T> value)
            => value.Match(FSharpResult<T, FSharpList<Error>>.NewOk, errors => FSharpResult<T, FSharpList<Error>>.NewError(ListModule.OfSeq(errors)));

        //Option -> F#
        /// <summary>Returns the option as an F# <c>option</c>: <c>Some</c> with the value, or <c>None</c>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The option. Its zero value (<c>default</c>) is None.</param>
        /// <returns><c>Some</c> with the value, or <c>None</c>.</returns>
        /// <remarks>
        /// An option holding <c>null</c> becomes <c>Some null</c>, not <c>None</c>. None is represented as <c>null</c> by
        /// FSharp.Core; from C# use <c>FSharpOption&lt;T&gt;.get_IsSome(o)</c>/<c>get_IsNone(o)</c> rather than <c>.Value</c>.
        /// The return is annotated non-null, as F# sees <c>'T option</c>, so F# code with nullness checks pipes it into
        /// <c>Option</c> functions without warnings.
        /// </remarks>
        public static FSharpOption<T> ToFSharp<T>(this Option<T> value)
            => value.Match(FSharpOption<T>.Some, _ => FSharpOption<T>.None!);

        /// <summary>Returns the option as an F# <c>voption</c>: <c>ValueSome</c> with the value, or <c>ValueNone</c>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The option. Its zero value (<c>default</c>) is None.</param>
        /// <remarks>An option holding <c>null</c> becomes <c>ValueSome null</c>, not <c>ValueNone</c>.</remarks>
        public static FSharpValueOption<T> ToFSharpValueOption<T>(this Option<T> value)
            => value.Match(FSharpValueOption<T>.NewValueSome, _ => FSharpValueOption<T>.ValueNone);

        //F# -> Result
        /// <summary>Returns the F# <c>Result</c> as a Resultful result: a success for <c>Ok</c>, a failure for <c>Error</c>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The F# result.</param>
        /// <remarks>For a <c>Result&lt;T&gt;</c>, follow with <c>AsResult()</c>: <c>fsResult.ToResultful().AsResult()</c>. <c>Ok null</c> is a success holding <c>null</c>.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <c>Error null</c>: a failure needs an error.</exception>
        /// <exception cref="ArgumentException">On <c>net11.0</c>, <paramref name="value"/> is <c>Error</c> holding the zero value of an error union, which holds no case.</exception>
        public static Result<T, TError> ToResultful<T, TError>(this FSharpResult<T, TError> value)
            => value.IsOk ? value.ResultValue.Ok<T, TError>() : Result.Fail<T, TError>(value: value.ErrorValue);

        /// <summary>Returns an F# <c>Result&lt;unit, 'E&gt;</c> as a step with no value: <c>Ok ()</c> is a success holding <see cref="Unit.Value"/>.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The F# result.</param>
        /// <remarks>Use this, not <c>ToResultful()</c>, for <c>Result&lt;unit, 'E&gt;</c>: <c>(Ok ()).ToResultful()</c> gives a <c>Result&lt;FSharp.Core.Unit, 'E&gt;</c>.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <c>Error null</c>: a failure needs an error.</exception>
        /// <exception cref="ArgumentException">On <c>net11.0</c>, <paramref name="value"/> is <c>Error</c> holding the zero value of an error union, which holds no case.</exception>
        public static Result<Unit, TError> ToResultfulUnit<TError>(this FSharpResult<FSharpUnit?, TError> value)
            => value.IsOk ? Unit.Value.Ok<Unit, TError>() : Result.Fail<Unit, TError>(value: value.ErrorValue);

        /// <summary>Returns an F# <c>Result</c> holding a list of errors as a validation: a success for <c>Ok</c>, a failure with the same errors, in order, for <c>Error</c>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of each error.</typeparam>
        /// <param name="value">The F# result.</param>
        /// <remarks>For a <c>Validation&lt;T&gt;</c>, follow with <c>AsValidation()</c>: <c>fsResult.ToValidation().AsValidation()</c>. The errors are copied into the validation's own list.</remarks>
        /// <exception cref="ArgumentException"><paramref name="value"/> is <c>Error []</c>: a failed validation is never empty.</exception>
        /// <exception cref="ArgumentNullException">The error list is <c>null</c> or holds <c>null</c>.</exception>
        public static Validation<T, TError> ToValidation<T, TError>(this FSharpResult<T, FSharpList<TError>> value)
        {
            if (value.IsOk) return Validation.Ok<T, TError>(value.ResultValue);
            // Checked here, so the exceptions name this method's parameter rather than Validation.Fail's.
            var errors = value.ErrorValue ?? throw new ArgumentNullException(nameof(value), "The error list is null.");
            if (errors.IsEmpty) throw new ArgumentException("A failed Validation needs at least one error.", nameof(value));
            foreach (var error in errors)
            {
                if (error is null) throw new ArgumentNullException(nameof(value), "A failure needs an error; null isn't one.");
            }

            return Validation.Fail<T, TError>(errors);
        }

        //F# -> Option
        /// <summary>Returns the F# <c>option</c> as a Resultful option: <c>Some</c> keeps its value, <c>None</c> is None.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The F# option. <c>None</c> is <c>null</c> at run time.</param>
        public static Option<T> ToResultful<T>(this FSharpOption<T>? value)
            => value is null ? Option<T>.None : value.Value.Some();

        /// <summary>Returns the F# <c>voption</c> as a Resultful option: <c>ValueSome</c> keeps its value, <c>ValueNone</c> is None.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The F# value option.</param>
        public static Option<T> ToResultful<T>(this FSharpValueOption<T> value)
            => value.IsValueSome ? value.Item.Some() : Option<T>.None;
    }
}
