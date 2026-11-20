using System;
using Resultful.Utils;

namespace Resultful
{
    // Sequential composition: run a chain of steps where each step only runs if everything before it succeeded.
    // The first failure is returned unchanged (including any errors it already accumulated); later steps never run.
    public static partial class Result
    {
        //Sequence: start a chain of VoidResult steps
        public static VoidResult Sequence(params Func<VoidResult>[] steps)
            => Ok().Then(steps);

        public static VoidResult<TError> Sequence<TError>(params Func<VoidResult<TError>>[] steps)
            => Ok<TError>().Then(steps);

        //Then on VoidResult
        public static VoidResult Then(this VoidResult value, params Func<VoidResult>[] steps)
        {
            foreach (var step in Steps(steps))
            {
                value = value.Bind(_ => step());
            }
            return value;
        }

        public static Result<T> Then<T>(this VoidResult value, Func<Result<T>> next)
        {
            next.ThrowIfDefault(nameof(next));
            return value.BindValue(_ => next());
        }

        //Then on VoidResult<TError>
        public static VoidResult<TError> Then<TError>(this VoidResult<TError> value, params Func<VoidResult<TError>>[] steps)
        {
            foreach (var step in Steps(steps))
            {
                value = value.Bind(_ => step());
            }
            return value;
        }

        public static Result<T, TError> Then<T, TError>(this VoidResult<TError> value, Func<Result<T, TError>> next)
        {
            next.ThrowIfDefault(nameof(next));
            return value.BindValue(_ => next());
        }

        //Then on Result<T>: steps that transform the value without changing its type
        public static Result<T> Then<T>(this Result<T> value, params Func<T, Result<T>>[] steps)
        {
            foreach (var step in Steps(steps))
            {
                value = value.Bind(step);
            }
            return value;
        }

        //Then on Result<T, TError>
        public static Result<T, TError> Then<T, TError>(this Result<T, TError> value, params Func<T, Result<T, TError>>[] steps)
        {
            foreach (var step in Steps(steps))
            {
                value = value.Bind(step);
            }
            return value;
        }

        //Check on Result<T>: steps that can fail but keep the value
        public static Result<T> Check<T>(this Result<T> value, params Func<T, VoidResult>[] checks)
        {
            foreach (var check in Steps(checks, nameof(checks)))
            {
                value = value.Bind(item => check(item).Map(item));
            }
            return value;
        }

        //Check on Result<T, TError>
        public static Result<T, TError> Check<T, TError>(this Result<T, TError> value, params Func<T, VoidResult<TError>>[] checks)
        {
            foreach (var check in Steps(checks, nameof(checks)))
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
