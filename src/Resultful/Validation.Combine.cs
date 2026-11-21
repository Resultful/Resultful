#if NET
using System;
using System.Collections.Generic;

namespace Resultful
{
    // Decision 22 item 4: any number of same-typed validations, beyond the eight-argument overloads. The tuple overload for
    // two values still wins for Combine(a, b), as a method applicable in its normal form beats a params expansion; a
    // collection expression, Combine([a, b]), or a span picks this one. net10.0 and later: ReadOnlySpan needs a package
    // on netstandard2.0 and the core references nothing (decision 1).
    public static partial class Validation
    {
        /// <summary>Combines any number of validations of one type: every value, in order, or every error from every validation, in order.</summary>
        /// <typeparam name="T">The type of each value.</typeparam>
        /// <typeparam name="TError">The type of each error.</typeparam>
        /// <param name="validations">The validations. None is a success with an empty list.</param>
        /// <remarks>Every validation is read, so every check's errors are kept; none stops the others.</remarks>
        /// <exception cref="InvalidOperationException">A validation is the zero value (<c>default</c>).</exception>
        public static Validation<IReadOnlyList<T>, TError> Combine<T, TError>(params ReadOnlySpan<Validation<T, TError>> validations)
        {
            var values = new List<T>(validations.Length);
            List<TError>? errors = null;
            foreach (var validation in validations)
            {
                if (validation.Gather(ref errors))
                    values.Add(validation.SuccessValue);
            }

            return errors is null
                ? Ok<IReadOnlyList<T>, TError>(values.AsReadOnly())
                : Validation<IReadOnlyList<T>, TError>.Gathered(errors);
        }

        /// <summary>Combines any number of validations of one type: every value, in order, or every error from every validation, in order.</summary>
        /// <typeparam name="T">The type of each value.</typeparam>
        /// <param name="validations">The validations. None is a success with an empty list.</param>
        /// <remarks>Every validation is read, so every check's errors are kept; none stops the others.</remarks>
        /// <exception cref="InvalidOperationException">A validation is the zero value (<c>default</c>).</exception>
        public static Validation<IReadOnlyList<T>> Combine<T>(params ReadOnlySpan<Validation<T>> validations)
        {
            var values = new List<T>(validations.Length);
            List<Error>? errors = null;
            foreach (var validation in validations)
            {
                if (validation.Inner.Gather(ref errors))
                    values.Add(validation.Inner.SuccessValue);
            }

            return errors is null
                ? Ok<IReadOnlyList<T>>(values.AsReadOnly())
                : new(Validation<IReadOnlyList<T>, Error>.Gathered(errors));
        }
    }
}
#endif
