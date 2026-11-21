using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Resultful.Assertions
{
    // Decision 18: the pieces every adapter shares, so the AwesomeAssertions and FluentAssertions adapters print the same
    // safe text and find a leaf the same way the core does. Internal, and compiled into each adapter from this file
    // (no InternalsVisibleTo), so a newer or older Resultful.Assertions can never break an adapter.
    /// <summary>
    /// What an assertion needs to read from an error: safe text for a failure message, and the leaf a test asked for.
    /// </summary>
    internal static class ResultInspection
    {
        /// <summary>
        /// The error as text for a failure message: <see cref="Error.ToString"/> for an <see cref="Error"/>, the held
        /// error's <see cref="Error.ToString"/> for an <see cref="IErrorUnion"/>, and only the type name for anything
        /// else.
        /// </summary>
        /// <typeparam name="TError">The result's error type.</typeparam>
        /// <param name="error">The error.</param>
        /// <returns>
        /// The text, which never contains a derived record's own properties: they may hold personal data.
        /// <c>"null"</c> for <c>null</c>.
        /// </returns>
        /// <remarks>
        /// An error that is not an <see cref="Error"/>, such as a <see cref="string"/> in a <c>Result&lt;T, string&gt;</c>,
        /// is printed as its type name only, because its own text could be anything.
        /// </remarks>
        public static string DescribeError<TError>(TError error)
        {
            if (error is null)
            {
                return "null";
            }

            return AsError(error) is { } found
                ? found.ToString()
                : $"{TypeName(error.GetType())} (not an Error, so only its type is shown)";
        }

        /// <summary>The error as an <see cref="Error"/>: itself, or the error an <see cref="IErrorUnion"/> holds.</summary>
        /// <typeparam name="TError">The result's error type.</typeparam>
        /// <param name="error">The error.</param>
        /// <returns>The <see cref="Error"/>, or <c>null</c> when <paramref name="error"/> is neither an <see cref="Error"/> nor an <see cref="IErrorUnion"/>.</returns>
        public static Error? AsError<TError>(TError error) => error switch
        {
            Error found => found,
            IErrorUnion union => union.Error,
            _ => null,
        };

        /// <summary>
        /// Finds the first <typeparamref name="TLeaf"/> in the error the way <see cref="Error.TryFind{TError}"/> does:
        /// the error itself, then its cause chain and inner errors, outer to inner. An <see cref="IErrorUnion"/> is
        /// searched from the error it holds.
        /// </summary>
        /// <typeparam name="TError">The result's error type.</typeparam>
        /// <typeparam name="TLeaf">The type of error to find.</typeparam>
        /// <param name="error">The error to search.</param>
        /// <param name="leaf">The first match; <c>null</c> if there is none.</param>
        /// <returns><c>true</c> if a <typeparamref name="TLeaf"/> was found.</returns>
        public static bool TryFindError<TError, TLeaf>(TError error, [NotNullWhen(true)] out TLeaf? leaf)
            where TLeaf : Error
        {
            if (AsError(error) is { } found)
            {
                return found.TryFind(out leaf);
            }

            leaf = null;
            return false;
        }

        /// <summary>
        /// Finds the first <typeparamref name="TLeaf"/> in a list of errors, such as a failed <c>Validation</c>'s:
        /// each error in order, searched as <see cref="TryFindError{TError, TLeaf}(TError, out TLeaf)"/> does.
        /// </summary>
        /// <remarks>A separate name, so a list is never taken for one error by overload resolution.</remarks>
        /// <typeparam name="TError">The type of the errors.</typeparam>
        /// <typeparam name="TLeaf">The type of error to find.</typeparam>
        /// <param name="errors">The errors to search, in order.</param>
        /// <param name="leaf">The first match; <c>null</c> if there is none.</param>
        /// <returns><c>true</c> if a <typeparamref name="TLeaf"/> was found.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="errors"/> is <c>null</c>.</exception>
        public static bool TryFindInErrors<TError, TLeaf>(IEnumerable<TError> errors, [NotNullWhen(true)] out TLeaf? leaf)
            where TLeaf : Error
        {
            foreach (var error in errors ?? throw new ArgumentNullException(nameof(errors)))
            {
                if (TryFindError(error, out leaf))
                {
                    return true;
                }
            }

            leaf = null;
            return false;
        }

        /// <summary>What a failure message says of a success: its value's runtime type, never the value.</summary>
        /// <param name="value">The success value.</param>
        /// <returns>"it succeeded with a value of type Order." or "it succeeded with null.".</returns>
        public static string DescribeSuccess(object? value)
            => value is null ? "it succeeded with null." : $"it succeeded with a value of type {TypeName(value.GetType())}.";

        /// <summary>
        /// A type's simple name, with generic arguments as C# writes them, the way <see cref="Error.ToString"/> prints
        /// types: <c>Result&lt;Int32, OrderError&gt;</c>, not <c>Result`2</c>.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>The name.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="type"/> is <c>null</c>.</exception>
        public static string TypeName(Type type)
        {
            var text = new StringBuilder();
            AppendTypeName(type ?? throw new ArgumentNullException(nameof(type)), text);
            return text.ToString();
        }

        private static void AppendTypeName(Type type, StringBuilder text)
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

                AppendTypeName(arguments[i], text);
            }

            text.Append('>');
        }
    }
}
