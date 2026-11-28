using System;
using System.Collections.Generic;
using Resultful.Utils;

namespace Resultful
{
    // Proposed decision 24: steps as an iterator. An iterator yields results (or shapes, whose leaves convert in with
    // no wrapping); Yield reads every step and gathers every error, YieldFirst stops at the first error and disposes
    // the iterator, so its finally and using blocks run.
    public static partial class Result
    {
        /// <summary>Reads every step. Returns the success if it is the only thing yielded, otherwise every error, in the order yielded.</summary>
        /// <typeparam name="T">The type of the success value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="steps">The steps, usually an iterator method.</param>
        /// <remarks>Throws <see cref="InvalidOperationException"/> when no error was yielded and the steps yielded no success or more than one, and on a zero value step.</remarks>
        public static Validation<T, TError> Yield<T, TError>(IEnumerable<Result<T, TError>> steps)
        {
            steps.ThrowIfDefault(nameof(steps));
            var gather = new Gather<T, TError>();
            foreach (var step in steps)
            {
                gather.Add(step);
            }
            return gather.Finish();
        }

        /// <summary>Reads every step of a result shape. Returns the success if it is the only thing yielded, otherwise every error, in the order yielded.</summary>
        /// <typeparam name="TShape">The result shape.</typeparam>
        /// <typeparam name="T">The type of the success value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="steps">The steps, usually an iterator method.</param>
        /// <remarks>Throws <see cref="InvalidOperationException"/> when no error was yielded and the steps yielded no success or more than one, and on a zero value step.</remarks>
        public static Validation<T, TError> Yield<TShape, T, TError>(IEnumerable<TShape> steps)
            where TShape : IResultShape<T, TError>
        {
            steps.ThrowIfDefault(nameof(steps));
            var gather = new Gather<T, TError>();
            foreach (var step in steps)
            {
                gather.Add(step.AsResult());
            }
            return gather.Finish();
        }

        /// <summary>Reads the steps up to the first error and returns it, or else the one success. The iterator is disposed at once, so its <c>finally</c> and <c>using</c> blocks run.</summary>
        /// <typeparam name="T">The type of the success value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="steps">The steps, usually an iterator method.</param>
        /// <remarks>Throws <see cref="InvalidOperationException"/> when the steps yield no success, a second success, or a zero value step.</remarks>
        public static Result<T, TError> YieldFirst<T, TError>(IEnumerable<Result<T, TError>> steps)
        {
            steps.ThrowIfDefault(nameof(steps));
            Result<T, TError>? success = null;
            foreach (var step in steps)
            {
                if (!step.TryGetValue(out _)) return step;
                if (success is not null) throw SecondSuccess();
                success = step;
            }
            return success ?? throw NoSuccess();
        }

        /// <summary>Reads the steps of a result shape up to the first error and returns it, or else the one success. The iterator is disposed at once, so its <c>finally</c> and <c>using</c> blocks run.</summary>
        /// <typeparam name="TShape">The result shape.</typeparam>
        /// <typeparam name="T">The type of the success value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="steps">The steps, usually an iterator method.</param>
        /// <remarks>Throws <see cref="InvalidOperationException"/> when the steps yield no success, a second success, or a zero value step.</remarks>
        public static TShape YieldFirst<TShape, T, TError>(IEnumerable<TShape> steps)
            where TShape : struct, IResultShape<T, TError>
        {
            steps.ThrowIfDefault(nameof(steps));
            TShape? success = null;
            foreach (var step in steps)
            {
                if (!step.AsResult().TryGetValue(out _)) return step;
                if (success is not null) throw SecondSuccess();
                success = step;
            }
            return success ?? throw NoSuccess();
        }

        private static InvalidOperationException SecondSuccess()
            => new("The steps yielded a second success; exactly one is needed.");

        private static InvalidOperationException NoSuccess()
            => new("The steps yielded no success and no error; exactly one success is needed.");

        private struct Gather<T, TError>
        {
            private List<TError>? _errors;
            private T _value;
            private int _successes;

            public void Add(Result<T, TError> step)
            {
                // TryGetValue throws on a zero value step (decision 10).
                if (step.TryGetValue(out var value))
                {
                    _value = value;
                    _successes++;
                }
                else if (step.TryGetError(out var error))
                {
                    (_errors ??= new List<TError>()).Add(error);
                }
            }

            public Validation<T, TError> Finish()
            {
                if (_errors is not null) return Validation<T, TError>.Gathered(_errors);
                return _successes switch
                {
                    1 => Validation.Ok<T, TError>(_value),
                    0 => throw NoSuccess(),
                    _ => throw SecondSuccess(),
                };
            }
        }
    }
}
