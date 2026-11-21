// The probe of decision 22 item 6: `await` on a result as a short-circuit. Everything here is internal to this test
// project and is not package API; docs/explorations/await-short-circuit.md has the findings.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Resultful.AwaitProbe
{
    /// <summary>What a builder reads from an awaiter that may hold a failure, without resuming the method.</summary>
    internal interface IShortCircuitAwaiter
    {
        /// <summary>True, with the error, when the awaited result is known to be a failure; false while it is pending.</summary>
        bool TryGetFailure([NotNullWhen(true)] out object? error);
    }

    /// <summary>
    /// A failure reached by unwinding: thrown by an awaiter's GetResult on a failure, so <c>finally</c> blocks and
    /// <c>using</c> disposals run, and turned back into the failure by the builder's SetException. Outside a result
    /// builder (a plain <c>async Task</c>) it faults the task instead of hanging it. The message never holds the
    /// error's contents (personal-data rule); the error is on <see cref="Error"/>.
    /// </summary>
    [SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Probe-internal: always created with its error.")]
    [SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "Probe-internal: not package API.")]
    internal sealed class AwaitedFailureException(object error) : Exception(Text)
    {
        public const string Text = "A failed result was awaited outside a result-builder method; its failure became this exception.";

        public object Error { get; } = error;
    }

    /// <summary>The awaiter for <c>await result</c>: completes on success; on a failure a result builder ends the method.</summary>
    [DebuggerNonUserCode]
    internal readonly struct ResultAwaiter<T>(Result<T> result) : ICriticalNotifyCompletion, IShortCircuitAwaiter
    {
        // The zero value is neither: IsCompleted is false, and TryGetFailure's read of Error throws (decision 10).
        public bool IsCompleted => result.IsSuccess;

        public T GetResult() => result.TryGetValue(out var value) ? value : throw new AwaitedFailureException(result.Error);

        public bool TryGetFailure([NotNullWhen(true)] out object? error)
        {
            error = result.Error;
            return true;
        }

        // Only a foreign builder (AsyncTaskMethodBuilder in a plain async method) calls these: resuming at once makes
        // GetResult throw, so the task faults. Not resuming would hang it forever (NaiveAwaiter proves it).
        public void OnCompleted(Action continuation) => continuation();

        public void UnsafeOnCompleted(Action continuation) => continuation();
    }

    /// <summary>The awaiter for <c>await result</c> on a typed result.</summary>
    [DebuggerNonUserCode]
    internal readonly struct ResultAwaiter<T, TError>(Result<T, TError> result) : ICriticalNotifyCompletion, IShortCircuitAwaiter
    {
        public bool IsCompleted => result.IsSuccess;

        public T GetResult() => result.TryGetValue(out var value) ? value : throw new AwaitedFailureException(result.Error!);

        public bool TryGetFailure([NotNullWhen(true)] out object? error)
        {
            error = result.Error!;
            return true;
        }

        public void OnCompleted(Action continuation) => continuation();

        public void UnsafeOnCompleted(Action continuation) => continuation();
    }

    /// <summary>
    /// The awaiter for <c>await task.ShortCircuit()</c>: awaits the task as real asynchronous work, then acts as
    /// <see cref="ResultAwaiter{T}"/> on the result it gives.
    /// </summary>
    [DebuggerNonUserCode]
    internal readonly struct TaskResultAwaiter<T>(Task<Result<T>> task) : ICriticalNotifyCompletion, IShortCircuitAwaiter
    {
        public bool IsCompleted => task.IsCompleted && (!task.IsCompletedSuccessfully || task.Result.IsSuccess);

        // After a real wait the builder isn't asked again, so a failure can only end the method by unwinding.
        public T GetResult() => new ResultAwaiter<T>(task.GetAwaiter().GetResult()).GetResult();

        public bool TryGetFailure([NotNullWhen(true)] out object? error)
        {
            if (task.IsCompletedSuccessfully && !task.Result.IsSuccess) return new ResultAwaiter<T>(task.Result).TryGetFailure(out error);
            error = null;
            return false;
        }

        public void OnCompleted(Action continuation) => task.GetAwaiter().OnCompleted(continuation);

        public void UnsafeOnCompleted(Action continuation) => task.GetAwaiter().UnsafeOnCompleted(continuation);
    }

    /// <summary>Turns an awaited error into the method's failure result: <c>Result&lt;T&gt;</c> or <c>Result&lt;T, TError&gt;</c>.</summary>
    internal static class Failure<TResult>
    {
        public static readonly Func<object, TResult> From = Build();

        // Reflection once per result type: the core has no static abstract `Fail` (24c explores one), so a builder over
        // ValueTask<TResult> can't name TResult's failure constructor otherwise. Not AOT-safe; a shipped builder would need it.
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Probe-internal; tests don't run native AOT.")]
        [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = "Probe-internal; tests aren't trimmed.")]
        static Func<object, TResult> Build()
        {
            var type = typeof(TResult);
            var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : null;
            var factory = definition == typeof(Result<>) ? nameof(FailureFactory.OfError)
                : definition == typeof(Result<,>) ? nameof(FailureFactory.OfTyped)
                : null;
            if (factory is null) return _ => throw new NotSupportedException($"A result builder returns Result<T> or Result<T, TError>, not {type}.");
            return typeof(FailureFactory).GetMethod(factory, BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(type.GenericTypeArguments).CreateDelegate<Func<object, TResult>>();
        }
    }

    internal static class FailureFactory
    {
        public const string Mismatch = "The awaited result's error type doesn't convert to the method's error type.";

        internal static Result<T> OfError<T>(object error) => error is Error e ? e.Fail<T>() : throw new InvalidOperationException(Mismatch);

        internal static Result<T, TError> OfTyped<T, TError>(object error) => error is TError e ? e.Fail<T, TError>() : throw new InvalidOperationException(Mismatch);
    }

    /// <summary>
    /// The method builder for <c>async Result&lt;T&gt;</c> that abandons the method on a failure: it records the error
    /// and never resumes the state machine. Nothing allocates, but <c>finally</c> blocks and <c>using</c> disposals
    /// after the failed await never run.
    /// </summary>
    // StackTraceHidden: an exception's trace shows the method, not the builder that rethrows it.
    [DebuggerNonUserCode, StackTraceHidden]
    internal struct ResultBuilder<T>
    {
        Result<T> _result;
        ExceptionDispatchInfo? _exception;

        public static ResultBuilder<T> Create() => default;

        public readonly Result<T> Task => _exception is null ? _result : Rethrow(_exception);

        public readonly void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine => stateMachine.MoveNext();

        public readonly void SetStateMachine(IAsyncStateMachine stateMachine) { }

        public void SetResult(T value) => _result = value;

        public void SetException(Exception exception)
        {
            if (exception is AwaitedFailureException failed) _result = Failure<Result<T>>.From(failed.Error);
            else _exception = ExceptionDispatchInfo.Capture(exception);
        }

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
            => _result = Sync.Abandon<TAwaiter, Result<T>>(ref awaiter);

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
            => _result = Sync.Abandon<TAwaiter, Result<T>>(ref awaiter);

        static Result<T> Rethrow(ExceptionDispatchInfo exception)
        {
            exception.Throw();
            throw new UnreachableException();
        }
    }

    /// <summary>
    /// The method builder for <c>async Result&lt;T&gt;</c> that unwinds on a failure: it resumes the state machine so
    /// GetResult throws <see cref="AwaitedFailureException"/>, <c>finally</c> and <c>using</c> run, and SetException turns
    /// the exception back into the failure. Costs one exception per failure, and a <c>catch (Exception)</c> in the
    /// method sees it.
    /// </summary>
    // StackTraceHidden: an exception's trace shows the method, not the builder that rethrows it.
    [DebuggerNonUserCode, StackTraceHidden]
    internal struct UnwindingResultBuilder<T>
    {
        ResultBuilder<T> _inner;

        public static UnwindingResultBuilder<T> Create() => default;

        public readonly Result<T> Task => _inner.Task;

        public readonly void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine => stateMachine.MoveNext();

        public readonly void SetStateMachine(IAsyncStateMachine stateMachine) { }

        public void SetResult(T value) => _inner.SetResult(value);

        public void SetException(Exception exception) => _inner.SetException(exception);

        public readonly void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
            => Sync.Unwind(ref awaiter, ref stateMachine);

        public readonly void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
            => Sync.Unwind(ref awaiter, ref stateMachine);
    }

    internal static class Sync
    {
        public const string RealAwait =
            "An async Result<T> method completes synchronously, so it can only await results and completed tasks; return ValueTask<Result<T>> to await real work.";

        public static TResult Abandon<TAwaiter, TResult>(ref TAwaiter awaiter) =>
            awaiter is IShortCircuitAwaiter shortCircuit && shortCircuit.TryGetFailure(out var error)
                ? Failure<TResult>.From(error)
                : throw new NotSupportedException(RealAwait);

        public static void Unwind<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
        {
            if (awaiter is not IShortCircuitAwaiter shortCircuit || !shortCircuit.TryGetFailure(out _)) throw new NotSupportedException(RealAwait);
            stateMachine.MoveNext();
        }
    }

    /// <summary>
    /// The method builder for <c>async ValueTask&lt;TResult&gt;</c>, where TResult is <c>Result&lt;T&gt;</c> or
    /// <c>Result&lt;T, TError&gt;</c>: real awaits suspend as in any async method (through
    /// <see cref="AsyncValueTaskMethodBuilder{TResult}"/>), and a failed result ends the method, abandoning it.
    /// </summary>
    [DebuggerNonUserCode]
    internal struct AsyncResultBuilder<TResult>
    {
        AsyncValueTaskMethodBuilder<TResult> _inner;

        public static AsyncResultBuilder<TResult> Create() => new() { _inner = AsyncValueTaskMethodBuilder<TResult>.Create() };

        public ValueTask<TResult> Task => _inner.Task;

        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine => _inner.Start(ref stateMachine);

        public void SetStateMachine(IAsyncStateMachine stateMachine) => _inner.SetStateMachine(stateMachine);

        public void SetResult(TResult result) => _inner.SetResult(result);

        public void SetException(Exception exception)
        {
            if (exception is AwaitedFailureException failed) _inner.SetResult(Failure<TResult>.From(failed.Error));
            else _inner.SetException(exception);
        }

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            if (awaiter is IShortCircuitAwaiter shortCircuit && shortCircuit.TryGetFailure(out var error)) _inner.SetResult(Failure<TResult>.From(error));
            else _inner.AwaitOnCompleted(ref awaiter, ref stateMachine);
        }

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            if (awaiter is IShortCircuitAwaiter shortCircuit && shortCircuit.TryGetFailure(out var error)) _inner.SetResult(Failure<TResult>.From(error));
            else _inner.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
        }
    }

    /// <summary><see cref="AsyncResultBuilder{TResult}"/> that unwinds on a failure, so <c>finally</c> and <c>using</c> run.</summary>
    [DebuggerNonUserCode]
    internal struct UnwindingAsyncResultBuilder<TResult>
    {
        AsyncResultBuilder<TResult> _inner;

        public static UnwindingAsyncResultBuilder<TResult> Create() => new() { _inner = AsyncResultBuilder<TResult>.Create() };

        public ValueTask<TResult> Task => _inner.Task;

        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine => _inner.Start(ref stateMachine);

        public void SetStateMachine(IAsyncStateMachine stateMachine) => _inner.SetStateMachine(stateMachine);

        public void SetResult(TResult result) => _inner.SetResult(result);

        public void SetException(Exception exception) => _inner.SetException(exception);

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            if (awaiter is IShortCircuitAwaiter shortCircuit && shortCircuit.TryGetFailure(out _)) stateMachine.MoveNext();
            else _inner.AwaitOnCompleted(ref awaiter, ref stateMachine);
        }

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            if (awaiter is IShortCircuitAwaiter shortCircuit && shortCircuit.TryGetFailure(out _)) stateMachine.MoveNext();
            else _inner.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
        }
    }
}

namespace Resultful.AwaitProbe.ShortCircuit
{
    /// <summary>Makes results awaitable. Imported per file, so only files that opt in can await a result.</summary>
    internal static class ResultAwaitExtensions
    {
        public static ResultAwaiter<T> GetAwaiter<T>(this Result<T> result) => new(result);

        public static ResultAwaiter<T, TError> GetAwaiter<T, TError>(this Result<T, TError> result) => new(result);

        /// <summary>A task of a result, awaited as real work and then as a result (Task's own GetAwaiter wins over an extension).</summary>
        public static TaskResultAwaitable<T> ShortCircuit<T>(this Task<Result<T>> task) => new(task);
    }

    internal readonly struct TaskResultAwaitable<T>(Task<Result<T>> task)
    {
        public TaskResultAwaiter<T> GetAwaiter() => new(task);
    }
}

namespace Resultful.AwaitProbe.Naive
{
    /// <summary>The first idea, kept to prove the hazard: an awaiter that never resumes on a failure.</summary>
    internal static class NaiveAwaitExtensions
    {
        public static NaiveAwaiter<T> GetAwaiter<T>(this Result<T> result) => new(result);
    }

    internal readonly struct NaiveAwaiter<T>(Result<T> result) : INotifyCompletion
    {
        public bool IsCompleted => result.IsSuccess;

        public T GetResult() => result.Value;

        // Never calls the continuation: a plain async Task awaiting a failure never completes.
        public void OnCompleted(Action continuation) { }
    }
}
