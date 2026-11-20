#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Resultful
{
    // Decision 14: one failure. Categories are records the user derives.
    public abstract record Error
    {
        protected Error(string message) => throw null!;

        public string Message => throw null!;

        // The lower error this one wraps.
        public Error? Cause { get => throw null!; init => throw null!; }

        // Notes added on the way up, innermost first. Copied on add (GOAL 06); compared element by element.
        public IReadOnlyList<string> Context { get => throw null!; init => throw null!; }

        public static Error From(string message) => throw null!;

        // Errors held inside this one: ValidationFailed's list. The base returns none.
        public virtual IEnumerable<Error> Inner() => throw null!;

        // This error, then depth-first through Cause and Inner().
        public IEnumerable<Error> Causes() => throw null!;

        public bool TryFind<TError>([NotNullWhen(true)] out TError? found) where TError : Error => throw null!;

        // Structural: the record's own equality, with Context compared element by element (ERR-EQ).
        public virtual bool Equals(Error? other) => throw null!;
        public override int GetHashCode() => throw null!;

        // Sealed: leaf type name, Message, Context and the cause chain; never the leaf's own properties (ERR-TOSTRING).
        public sealed override string ToString() => throw null!;
    }

    public sealed record MessageError : Error
    {
        public MessageError(string message) : base(message) => throw null!;
    }

    // A Validation's errors carried through a Result chain. Never empty (VAL-NONEMPTY); equal element by element (VAL-EQ).
    public sealed record ValidationFailed<TError> : Error where TError : Error
    {
        public ValidationFailed(IReadOnlyList<TError> errors) : base((string)null!) => throw null!;
        public IReadOnlyList<TError> Errors => throw null!;
        public override IEnumerable<Error> Inner() => throw null!;
        public bool Equals(ValidationFailed<TError>? other) => throw null!;
        public override int GetHashCode() => throw null!;
    }

    public static class ErrorExtensions
    {
        // Adds a note and keeps the error's type.
        public static TError WithContext<TError>(this TError error, string message) where TError : Error => throw null!;
    }

    // Decision 17: core helpers for an error union. `where TError : Error` overloads live in Result and Validation;
    // these live in their own class because overloads can't differ by constraint alone.
    public static class ErrorUnionExtensions
    {
        public static Result<T> AsResult<T, TError>(this Result<T, TError> result) where TError : struct, IErrorUnion => throw null!;
        public static Result<T> ToResult<T, TError>(this Validation<T, TError> validation) where TError : struct, IErrorUnion => throw null!;
    }
}
