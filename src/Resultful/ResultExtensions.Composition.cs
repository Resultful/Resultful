using System;
using Resultful.Utils;

namespace Resultful
{
    // Sequential composition: run a chain of steps where each step only runs if everything before it succeeded.
    // The first failure is returned unchanged (including any errors it already accumulated); later steps never run.
    public static partial class Result
    {
        //Sequence: start a chain of VoidResult steps
        /// <summary>Runs <paramref name="steps"/> in order, starting from a success, and stops at the first failure, which is returned unchanged. Later steps don't run.</summary>
        /// <param name="steps">The steps to run.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="steps"/> or any step is <c>null</c>, before any step runs.</remarks>
        public static VoidResult Sequence(params Func<VoidResult>[] steps)
            => Ok().Then(steps);

        /// <summary>Runs <paramref name="steps"/> in order, starting from a success, and stops at the first failure, which is returned unchanged. Later steps don't run.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="steps">The steps to run.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="steps"/> or any step is <c>null</c>, before any step runs.</remarks>
        public static VoidResult<TError> Sequence<TError>(params Func<VoidResult<TError>>[] steps)
            => Ok<TError>().Then(steps);

        //Then on VoidResult
        /// <summary>Runs <paramref name="steps"/> in order while the result is a success, and stops at the first failure, which is returned unchanged. Later steps don't run.</summary>
        /// <param name="value">The result.</param>
        /// <param name="steps">The steps to run.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="steps"/> or any step is <c>null</c>, before any step runs, and <see cref="System.InvalidOperationException"/> on the zero value, even with no steps.</remarks>
        public static VoidResult Then(this VoidResult value, params Func<VoidResult>[] steps)
        {
            Steps(steps);
            value.ThrowIfZero();
            foreach (var step in steps)
            {
                value = value.Bind(_ => step());
            }
            return value;
        }

        /// <summary>On success, returns the result <paramref name="next"/> gives. A failure keeps the error and <paramref name="next"/> isn't called.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The result.</param>
        /// <param name="next">Gives the next result.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="next"/> is <c>null</c>, even on a failure.</remarks>
        public static Result<T> Then<T>(this VoidResult value, Func<Result<T>> next)
        {
            next.ThrowIfDefault(nameof(next));
            return value.BindValue(_ => next());
        }

        //Then on VoidResult<TError>
        /// <summary>Runs <paramref name="steps"/> in order while the result is a success, and stops at the first failure, which is returned unchanged. Later steps don't run.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The result.</param>
        /// <param name="steps">The steps to run.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="steps"/> or any step is <c>null</c>, before any step runs.</remarks>
        public static VoidResult<TError> Then<TError>(this VoidResult<TError> value, params Func<VoidResult<TError>>[] steps)
        {
            Steps(steps);
            value.ThrowIfZero();
            foreach (var step in steps)
            {
                value = value.Bind(_ => step());
            }
            return value;
        }

        /// <summary>On success, returns the result <paramref name="next"/> gives. A failure keeps the error and <paramref name="next"/> isn't called.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The result.</param>
        /// <param name="next">Gives the next result.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="next"/> is <c>null</c>, even on a failure.</remarks>
        public static Result<T, TError> Then<T, TError>(this VoidResult<TError> value, Func<Result<T, TError>> next)
        {
            next.ThrowIfDefault(nameof(next));
            return value.BindValue(_ => next());
        }

        //Then on Result<T>: steps that transform the value without changing its type
        /// <summary>Passes the value through <paramref name="steps"/> in order while the result is a success, and stops at the first failure, which is returned unchanged. Later steps don't run.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The result.</param>
        /// <param name="steps">The steps to run.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="steps"/> or any step is <c>null</c>, before any step runs.</remarks>
        public static Result<T> Then<T>(this Result<T> value, params Func<T, Result<T>>[] steps)
        {
            Steps(steps);
            value.ThrowIfZero();
            foreach (var step in steps)
            {
                value = value.Bind(step);
            }
            return value;
        }

        //Then on Result<T, TError>
        /// <summary>Passes the value through <paramref name="steps"/> in order while the result is a success, and stops at the first failure, which is returned unchanged. Later steps don't run.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The result.</param>
        /// <param name="steps">The steps to run.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="steps"/> or any step is <c>null</c>, before any step runs.</remarks>
        public static Result<T, TError> Then<T, TError>(this Result<T, TError> value, params Func<T, Result<T, TError>>[] steps)
        {
            Steps(steps);
            value.ThrowIfZero();
            foreach (var step in steps)
            {
                value = value.Bind(step);
            }
            return value;
        }

        //Check on Result<T>: steps that can fail but keep the value
        /// <summary>Runs <paramref name="checks"/> in order on the value while the result is a success. A check that fails stops the chain and its errors become the result; otherwise the value is kept. Later checks don't run.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The result.</param>
        /// <param name="checks">The checks to run on the value.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="checks"/> or any check is <c>null</c>, before any check runs.</remarks>
        public static Result<T> Check<T>(this Result<T> value, params Func<T, VoidResult>[] checks)
        {
            Steps(checks, nameof(checks));
            value.ThrowIfZero();
            foreach (var check in checks)
            {
                value = value.Bind(item => check(item).Map(item));
            }
            return value;
        }

        //Check on Result<T, TError>
        /// <summary>Runs <paramref name="checks"/> in order on the value while the result is a success. A check that fails stops the chain and its errors become the result; otherwise the value is kept. Later checks don't run.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The result.</param>
        /// <param name="checks">The checks to run on the value.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="checks"/> or any check is <c>null</c>, before any check runs.</remarks>
        public static Result<T, TError> Check<T, TError>(this Result<T, TError> value, params Func<T, VoidResult<TError>>[] checks)
        {
            Steps(checks, nameof(checks));
            value.ThrowIfZero();
            foreach (var check in checks)
            {
                value = value.Bind(item => check(item).Map(item));
            }
            return value;
        }

        // Validates the whole array up front so a null step fails fast, whatever the runtime outcome.
        private static TStep[] Steps<TStep>(TStep[] steps, string argName = "steps") where TStep : class
        {
            steps.ThrowIfDefault(argName);
            foreach (var step in steps)
            {
                step.ThrowIfDefault(argName);
            }
            return steps;
        }
    }
}
