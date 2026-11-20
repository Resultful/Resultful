using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace Resultful
{
    // Decision 14: one failure. Categories are records the user derives from this one.
    /// <summary>
    /// One failure. Derive a record from it for each kind of failure, and put the failure's data in typed properties.
    /// </summary>
    /// <remarks>
    /// Two errors are equal when they have the same type, <see cref="Message"/>, <see cref="Cause"/> and the same
    /// <see cref="Context"/>, compared element by element, plus whatever the derived record compares.
    /// <see cref="ToString"/> prints the type name, message, context and causes, never a derived record's own properties.
    /// </remarks>
    public abstract record Error
    {
        private static readonly IReadOnlyList<string> NoContext = new ReadOnlyCollection<string>(Array.Empty<string>());

        // ToString stops going deeper after this many levels, and after printing this many errors, and writes "…".
        private const int MaxPrintDepth = 64;
        private const int MaxPrintCount = 1000;

        private IReadOnlyList<string> _context = NoContext;

        /// <summary>Creates an error with a message for people to read.</summary>
        /// <param name="message">What went wrong, in a sentence.</param>
        /// <exception cref="ArgumentNullException"><paramref name="message"/> is <c>null</c>.</exception>
        protected Error(string message)
        {
            Message = message ?? throw new ArgumentNullException(nameof(message));
        }

        /// <summary>What went wrong, for people to read.</summary>
        public string Message { get; }

        /// <summary>The lower error this one wraps, if any. Find it, or anything under it, with <see cref="TryFind{TError}"/>.</summary>
        public Error? Cause { get; init; }

        /// <summary>Notes added on the way up with <see cref="ErrorExtensions.WithContext{TError}(TError, string)"/>, innermost first.</summary>
        /// <remarks>Never <c>null</c>. Setting it copies the list, so a later change to the list given has no effect.</remarks>
        /// <exception cref="ArgumentNullException">Set to <c>null</c>, or to a list holding <c>null</c>.</exception>
        public IReadOnlyList<string> Context
        {
            get => _context;
            init => _context = CopyContext(value);
        }

        /// <summary>Creates a <see cref="MessageError"/>: an error with only a message.</summary>
        /// <param name="message">What went wrong, in a sentence.</param>
        /// <returns>A <see cref="MessageError"/> with <paramref name="message"/>, no cause and no context.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="message"/> is <c>null</c>.</exception>
        public static Error From(string message) => new MessageError(message);

        /// <summary>The errors held inside this one, such as the list in a <see cref="ValidationFailed{TError}"/>.</summary>
        /// <returns>The errors held inside this one. The base returns none.</returns>
        public virtual IEnumerable<Error> Inner() => Enumerable.Empty<Error>();

        /// <summary>This error, then everything under it, depth first: its <see cref="Cause"/> and what is under that, then each <see cref="Inner"/> error and what is under that.</summary>
        /// <remarks>
        /// Each error object appears once: one reached again (an <see cref="Inner"/> override that returns an error
        /// above it, or the same object listed twice) is skipped, so this always ends. An <see cref="Inner"/> that
        /// returns <c>null</c> counts as none.
        /// </remarks>
        /// <returns>This error and every error under it, outer to inner.</returns>
        public IEnumerable<Error> Causes()
        {
            var seen = new HashSet<Error>(ReferenceComparer.Instance);
            var pending = new Stack<Error>();
            pending.Push(this);
            while (pending.Count > 0)
            {
                var error = pending.Pop();
                if (!seen.Add(error))
                {
                    continue;
                }

                yield return error;
                foreach (var inner in InnerOf(error).Reverse())
                {
                    pending.Push(inner);
                }

                if (error.Cause is { } cause)
                {
                    pending.Push(cause);
                }
            }
        }

        /// <summary>Finds the first error of type <typeparamref name="TError"/> in <see cref="Causes"/>: this error, its cause chain and its inner errors.</summary>
        /// <typeparam name="TError">The type of error to find.</typeparam>
        /// <param name="found">The first error of that type, outer to inner; <c>null</c> if there is none.</param>
        /// <returns><c>true</c> if an error of that type was found.</returns>
        public bool TryFind<TError>([NotNullWhen(true)] out TError? found)
            where TError : Error
        {
            foreach (var error in Causes())
            {
                if (error is TError match)
                {
                    found = match;
                    return true;
                }
            }

            found = null;
            return false;
        }

        /// <summary>
        /// Compares two errors by value: the same type, <see cref="Message"/> and <see cref="Cause"/>, and the same
        /// <see cref="Context"/> element by element. A derived record also compares its own properties.
        /// </summary>
        /// <param name="other">The error to compare with.</param>
        /// <returns><c>true</c> if the two errors are equal.</returns>
        public virtual bool Equals(Error? other) =>
            ReferenceEquals(this, other)
            || (other is not null
                && EqualityContract == other.EqualityContract
                && string.Equals(Message, other.Message, StringComparison.Ordinal)
                && EqualityComparer<Error?>.Default.Equals(Cause, other.Cause)
                && _context.SequenceEqual(other._context, StringComparer.Ordinal));

        /// <summary>A hash code that agrees with <see cref="Equals(Error)"/>, so equal errors have equal hash codes.</summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = EqualityContract.GetHashCode();
                hash = (hash * -1521134295) + StringComparer.Ordinal.GetHashCode(Message);
                hash = (hash * -1521134295) + (Cause?.GetHashCode() ?? 0);
                foreach (var note in _context)
                {
                    hash = (hash * -1521134295) + StringComparer.Ordinal.GetHashCode(note);
                }

                return hash;
            }
        }

        /// <summary>
        /// The error for a log: the type name, <see cref="Message"/>, <see cref="Context"/>, the inner errors and the
        /// cause chain, as in <c>SaveFailed: order 7 was not saved (context: checkout) ---&gt; DbTimeout: the database timed out</c>.
        /// </summary>
        /// <remarks>
        /// Sealed, so a derived record's own properties never appear: they may hold personal data. Put what a log
        /// needs in <see cref="Message"/>. The text is the same under every culture. Inner errors are printed before
        /// the cause, while <see cref="Causes"/> visits the cause first. Never throws: an error that contains itself
        /// is written as <c>…</c>, and so is anything past 64 levels deep or past the first 1000 errors.
        /// </remarks>
        /// <returns>The error as one line of text, unless a message or note contains line breaks.</returns>
        public sealed override string ToString()
        {
            var text = new StringBuilder();
            var budget = MaxPrintCount;
            Print(this, text, new HashSet<Error>(ReferenceComparer.Instance), 0, ref budget);
            return text.ToString();
        }

        /// <summary>A copy with one more note at the end of <see cref="Context"/>; the type and everything else are kept.</summary>
        internal Error AddContext(string message)
        {
            var notes = new string[_context.Count + 1];
            for (var i = 0; i < _context.Count; i++)
            {
                notes[i] = _context[i];
            }

            notes[notes.Length - 1] = message;
            var copy = this with { };
            copy._context = new ReadOnlyCollection<string>(notes);
            return copy;
        }

        private static IReadOnlyList<string> CopyContext(IReadOnlyList<string> value)
        {
            if ((value ?? throw new ArgumentNullException(nameof(value))).Count == 0)
            {
                return NoContext;
            }

            var notes = new string[value.Count];
            for (var i = 0; i < notes.Length; i++)
            {
                notes[i] = value[i] ?? throw new ArgumentNullException(nameof(value), "A context note is null.");
            }

            return new ReadOnlyCollection<string>(notes);
        }

        private static IEnumerable<Error> InnerOf(Error error) => error.Inner() ?? Enumerable.Empty<Error>();

        // path holds the errors above this one, so a cycle prints "…"; the same error listed twice still prints twice.
        private static void Print(Error error, StringBuilder text, HashSet<Error> path, int depth, ref int budget)
        {
            if (depth >= MaxPrintDepth || budget <= 0 || !path.Add(error))
            {
                text.Append('…');
                return;
            }

            budget--;
            PrintType(error.GetType(), text);
            text.Append(": ").Append(error.Message);
            if (error._context.Count > 0)
            {
                text.Append(" (context: ");
                for (var i = 0; i < error._context.Count; i++)
                {
                    if (i > 0)
                    {
                        text.Append("; ");
                    }

                    text.Append(error._context[i]);
                }

                text.Append(')');
            }

            var first = true;
            foreach (var inner in InnerOf(error))
            {
                text.Append(first ? " [" : "; ");
                first = false;
                Print(inner, text, path, depth + 1, ref budget);
            }

            if (!first)
            {
                text.Append(']');
            }

            if (error.Cause is { } cause)
            {
                text.Append(" ---> ");
                Print(cause, text, path, depth + 1, ref budget);
            }

            path.Remove(error);
        }

        // The simple name, with generic arguments as C# writes them: ValidationFailed<Required>, not ValidationFailed`1.
        private static void PrintType(Type type, StringBuilder text)
        {
            var name = type.Name;
            var tick = name.IndexOf('`');
            if (tick < 0 || !type.IsGenericType)
            {
                text.Append(name);
                return;
            }

            text.Append(name, 0, tick).Append('<');
            var arguments = type.GetGenericArguments();
            for (var i = 0; i < arguments.Length; i++)
            {
                if (i > 0)
                {
                    text.Append(", ");
                }

                PrintType(arguments[i], text);
            }

            text.Append('>');
        }
    }

    // Compares errors by reference, for the cycle guards (netstandard2.0 has no ReferenceEqualityComparer).
    internal sealed class ReferenceComparer : IEqualityComparer<Error>
    {
        public static readonly ReferenceComparer Instance = new();

        public bool Equals(Error? x, Error? y) => ReferenceEquals(x, y);

        public int GetHashCode(Error obj) => RuntimeHelpers.GetHashCode(obj);
    }

    /// <summary>An error with only a message. <see cref="Error.From(string)"/> creates one.</summary>
    public sealed record MessageError : Error
    {
        /// <summary>Creates an error with only a message.</summary>
        /// <param name="message">What went wrong, in a sentence.</param>
        /// <exception cref="ArgumentNullException"><paramref name="message"/> is <c>null</c>.</exception>
        public MessageError(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// A non-empty list of errors carried through a <c>Result</c> chain as one error, such as a failed <c>Validation</c>.
    /// Its <see cref="Error.Message"/> is "Validation failed.". <see cref="Error.TryFind{TError}"/> searches its errors.
    /// </summary>
    /// <typeparam name="TError">The type of the errors in the list.</typeparam>
    /// <remarks>Two are equal when their errors are equal element by element, in order, and the rest of the error is equal.</remarks>
    public sealed record ValidationFailed<TError> : Error
        where TError : Error
    {
        private const string ValidationMessage = "Validation failed.";

        /// <summary>Creates the error from a non-empty list, copied so a later change to the list given has no effect.</summary>
        /// <param name="errors">The errors, in order. At least one, and none <c>null</c>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="errors"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="errors"/> is empty or holds <c>null</c>.</exception>
        public ValidationFailed(IReadOnlyList<TError> errors)
            : base(ValidationMessage)
        {
            if ((errors ?? throw new ArgumentNullException(nameof(errors))).Count == 0)
            {
                throw new ArgumentException("A ValidationFailed needs at least one error.", nameof(errors));
            }

            var copy = new TError[errors.Count];
            for (var i = 0; i < copy.Length; i++)
            {
                copy[i] = errors[i] ?? throw new ArgumentException($"The error at index {i} is null.", nameof(errors));
            }

            Errors = new ReadOnlyCollection<TError>(copy);
        }

        /// <summary>The errors, in order. Never empty.</summary>
        public IReadOnlyList<TError> Errors { get; }

        /// <summary>The errors in <see cref="Errors"/>, in order.</summary>
        /// <returns>The errors in <see cref="Errors"/>.</returns>
        public override IEnumerable<Error> Inner() => Errors;

        /// <summary>Compares two by value: the rest of the error is equal, and <see cref="Errors"/> are equal element by element, in order.</summary>
        /// <param name="other">The error to compare with.</param>
        /// <returns><c>true</c> if the two are equal.</returns>
        public bool Equals(ValidationFailed<TError>? other) =>
            ReferenceEquals(this, other)
            || (other is not null && base.Equals(other) && Errors.SequenceEqual(other.Errors));

        /// <summary>A hash code that agrees with <see cref="Equals(ValidationFailed{TError})"/>.</summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = base.GetHashCode();
                foreach (var error in Errors)
                {
                    hash = (hash * -1521134295) + error.GetHashCode();
                }

                return hash;
            }
        }
    }

    /// <summary>Extensions on <see cref="Error"/> that keep the error's own type.</summary>
    public static class ErrorExtensions
    {
        /// <summary>
        /// Adds a note to the end of the error's <see cref="Error.Context"/> and keeps its type, so a <c>switch</c> over
        /// the error still matches it. The error itself doesn't change: the result is a copy.
        /// </summary>
        /// <typeparam name="TError">The error's type, which the copy keeps.</typeparam>
        /// <param name="error">The error to add a note to.</param>
        /// <param name="message">The note, such as what was being done when the error happened.</param>
        /// <returns>A copy of <paramref name="error"/> with one more note.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> or <paramref name="message"/> is <c>null</c>.</exception>
        public static TError WithContext<TError>(this TError error, string message)
            where TError : Error
        {
            return (TError)(error ?? throw new ArgumentNullException(nameof(error)))
                .AddContext(message ?? throw new ArgumentNullException(nameof(message)));
        }
    }
}
