using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

// Signatures copied from tests/Resultful.TargetApi/Resultful; only the members 12b uses. Bodies are the smallest that
// keep the target's comments true (zero value, structural equality, Context copied on add).
namespace Resultful
{
    internal static class ObsoleteMessages
    {
        public const string Default = "A result has no default value: use Result.Ok or Result.Fail.";
    }

    public abstract record Error
    {
        private readonly IReadOnlyList<string> _context = Array.Empty<string>();

        protected Error(string message) => Message = message;

        public string Message { get; }

        public Error? Cause { get; init; }

        public IReadOnlyList<string> Context { get => _context; init => _context = value is null ? Array.Empty<string>() : value.ToArray(); }

        public static Error From(string message) => new MessageError(message);

        public virtual IEnumerable<Error> Inner() => Array.Empty<Error>();

        public IEnumerable<Error> Causes()
        {
            yield return this;
            if (Cause is not null) foreach (var c in Cause.Causes()) yield return c;
            foreach (var inner in Inner()) foreach (var c in inner.Causes()) yield return c;
        }

        public bool TryFind<TError>([NotNullWhen(true)] out TError? found) where TError : Error
        {
            found = Causes().OfType<TError>().FirstOrDefault();
            return found is not null;
        }

        public virtual bool Equals(Error? other) =>
            other is not null && EqualityContract == other.EqualityContract && Message == other.Message
            && Equals(Cause, other.Cause) && Context.SequenceEqual(other.Context);

        public override int GetHashCode() => HashCode.Combine(EqualityContract, Message, Cause, Context.Count);

        public sealed override string ToString() =>
            GetType().Name + ": " + Message + (Context.Count > 0 ? " [" + string.Join(" / ", Context) + "]" : "")
            + (Cause is null ? "" : " <- " + Cause);
    }

    public sealed record MessageError : Error
    {
        public MessageError(string message) : base(message) { }
    }

    public sealed record ValidationFailed<TError> : Error where TError : Error
    {
        public ValidationFailed(IReadOnlyList<TError> errors) : base(Describe(errors)) => Errors = errors.ToArray();

        public IReadOnlyList<TError> Errors { get; }

        public override IEnumerable<Error> Inner() => Errors;

        public bool Equals(ValidationFailed<TError>? other) => base.Equals(other) && Errors.SequenceEqual(other!.Errors);

        public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Errors.Count);

        static string Describe(IReadOnlyList<TError> errors)
        {
            ArgumentNullException.ThrowIfNull(errors);
            if (errors.Count == 0) throw new ArgumentException("A validation failure has at least one error.", nameof(errors));
            return $"{errors.Count} validation error(s)";
        }
    }

    public static class ErrorExtensions
    {
        public static TError WithContext<TError>(this TError error, string message) where TError : Error =>
            (TError)(error with { Context = error.Context.Append(message).ToArray() });
    }

    public interface IErrorUnion
    {
        Error Error { get; }
    }

    public class Unit
    {
        private Unit() { }
        public static Unit Value { get; } = new();
    }

    public readonly partial struct Result<T, TError> : IEquatable<Result<T, TError>>
    {
        private readonly byte _state; // 0 zero, 1 ok, 2 fail
        private readonly T _value;
        private readonly TError _error;

        [Obsolete(ObsoleteMessages.Default, error: true)]
        public Result() => throw new InvalidOperationException(ObsoleteMessages.Default);

        internal Result(byte state, T value, TError error) { _state = state; _value = value; _error = error; }

        public bool IsDefault => _state == 0;
        public bool IsSuccess => Guard() == 1;
        public bool IsFailure => Guard() == 2;
        public T Value => Guard() == 1 ? _value : throw new InvalidOperationException("The result is a failure.");
        public TError Error => Guard() == 2 ? _error : throw new InvalidOperationException("The result is a success.");

        public bool TryGetValue([MaybeNullWhen(false)] out T value) { value = _value; return Guard() == 1; }
        public bool TryGetError([MaybeNullWhen(false)] out TError error) { error = _error; return Guard() == 2; }

        private byte Guard() => _state != 0 ? _state : throw new InvalidOperationException("<default Result>: a result has no default value.");

        public static implicit operator Result<T, TError>(TError value) => new(2, default!, value);
        public static implicit operator Result<T, TError>(T value) => new(1, value, default!);

        public bool Equals(Result<T, TError> other) => _state == other._state
            && (_state != 1 || EqualityComparer<T>.Default.Equals(_value, other._value))
            && (_state != 2 || EqualityComparer<TError>.Default.Equals(_error, other._error));
        public override bool Equals(object? obj) => obj is Result<T, TError> other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(_state, _state == 1 ? _value : default, _state == 2 ? _error : default);
        public override string ToString() => _state switch { 0 => "<default Result>", 1 => $"Ok({_value})", _ => $"Fail({_error})" };
        public static bool operator ==(Result<T, TError> left, Result<T, TError> right) => left.Equals(right);
        public static bool operator !=(Result<T, TError> left, Result<T, TError> right) => !left.Equals(right);
    }

    public readonly partial struct Result<T> : IEquatable<Result<T>>
    {
        private readonly byte _state;
        private readonly T _value;
        private readonly Error? _error;

        [Obsolete(ObsoleteMessages.Default, error: true)]
        public Result() => throw new InvalidOperationException(ObsoleteMessages.Default);

        internal Result(byte state, T value, Error? error) { _state = state; _value = value; _error = error; }

        public bool IsDefault => _state == 0;
        public bool IsSuccess => Guard() == 1;
        public bool IsFailure => Guard() == 2;
        public T Value => Guard() == 1 ? _value : throw new InvalidOperationException("The result is a failure.");
        public Error Error => Guard() == 2 ? _error! : throw new InvalidOperationException("The result is a success.");

        public bool TryGetValue([MaybeNullWhen(false)] out T value) { value = _value; return Guard() == 1; }
        public bool TryGetError([MaybeNullWhen(false)] out Error error) { error = _error; return Guard() == 2; }

        private byte Guard() => _state != 0 ? _state : throw new InvalidOperationException("<default Result>: a result has no default value.");

        public static implicit operator Result<T>(T value) => new(1, value, null);
        public static implicit operator Result<T>(Error error) => new(2, default!, error ?? throw new ArgumentNullException(nameof(error)));

        public bool Equals(Result<T> other) => _state == other._state
            && (_state != 1 || EqualityComparer<T>.Default.Equals(_value, other._value))
            && (_state != 2 || Equals(_error, other._error));
        public override bool Equals(object? obj) => obj is Result<T> other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(_state, _state == 1 ? _value : default, _error);
        public override string ToString() => _state switch { 0 => "<default Result>", 1 => $"Ok({_value})", _ => $"Fail({_error})" };
        public static bool operator ==(Result<T> left, Result<T> right) => left.Equals(right);
        public static bool operator !=(Result<T> left, Result<T> right) => !left.Equals(right);
    }

    public static partial class Result
    {
        public static Result<T, TError> Fail<T, TError>(this TError value) => new(2, default!, value);
        public static Result<T, TError> Ok<T, TError>(this T value) => new(1, value, default!);
        public static Result<T> Fail<T>(this Error error) => new(2, default!, error ?? throw new ArgumentNullException(nameof(error)));
        public static Result<T> Ok<T>(this T value) => new(1, value, null);
        public static Result<Unit> Ok() => new(1, Unit.Value, null);
        public static Result<Unit> Fail(string message) => Fail<Unit>(Error.From(message));
        public static Result<T> Fail<T>(string message) => Fail<T>(Error.From(message));
    }

    public readonly struct Validation<T, TError> : IEquatable<Validation<T, TError>>
    {
        private readonly byte _state;
        private readonly T _value;
        private readonly IReadOnlyList<TError>? _errors;

        [Obsolete(ObsoleteMessages.Default, error: true)]
        public Validation() => throw new InvalidOperationException(ObsoleteMessages.Default);

        internal Validation(byte state, T value, IReadOnlyList<TError>? errors) { _state = state; _value = value; _errors = errors; }

        public bool IsDefault => _state == 0;
        [MemberNotNullWhen(true, nameof(Value))]
        [MemberNotNullWhen(false, nameof(Errors))]
        public bool IsSuccess => Guard() == 1;
        [MemberNotNullWhen(false, nameof(Value))]
        [MemberNotNullWhen(true, nameof(Errors))]
        public bool IsFailure => Guard() == 2;
        public T Value => Guard() == 1 ? _value : throw new InvalidOperationException("The validation is a failure.");
        public IReadOnlyList<TError> Errors => Guard() == 2 ? _errors! : throw new InvalidOperationException("The validation is a success.");
        public bool TryGetValue([MaybeNullWhen(false)] out T value) { value = _value; return Guard() == 1; }
        public bool TryGetErrors([MaybeNullWhen(false)] out IReadOnlyList<TError> errors) { errors = _errors; return Guard() == 2; }

        private byte Guard() => _state != 0 ? _state : throw new InvalidOperationException("<default Validation>: a validation has no default value.");

        public bool Equals(Validation<T, TError> other) => _state == other._state
            && (_state != 1 || EqualityComparer<T>.Default.Equals(_value, other._value))
            && (_state != 2 || _errors!.SequenceEqual(other._errors!));
        public override bool Equals(object? obj) => obj is Validation<T, TError> other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(_state, _state == 1 ? _value : default, _errors?.Count);
        public override string ToString() => _state switch { 0 => "<default Validation>", 1 => $"Ok({_value})", _ => $"Fail({string.Join(", ", _errors!)})" };
        public static bool operator ==(Validation<T, TError> left, Validation<T, TError> right) => left.Equals(right);
        public static bool operator !=(Validation<T, TError> left, Validation<T, TError> right) => !left.Equals(right);
    }

    public readonly struct Validation<T> : IEquatable<Validation<T>>
    {
        private readonly byte _state;
        private readonly T _value;
        private readonly IReadOnlyList<Error>? _errors;

        [Obsolete(ObsoleteMessages.Default, error: true)]
        public Validation() => throw new InvalidOperationException(ObsoleteMessages.Default);

        internal Validation(byte state, T value, IReadOnlyList<Error>? errors) { _state = state; _value = value; _errors = errors; }

        public bool IsDefault => _state == 0;
        [MemberNotNullWhen(true, nameof(Value))]
        [MemberNotNullWhen(false, nameof(Errors))]
        public bool IsSuccess => Guard() == 1;
        [MemberNotNullWhen(false, nameof(Value))]
        [MemberNotNullWhen(true, nameof(Errors))]
        public bool IsFailure => Guard() == 2;
        public T Value => Guard() == 1 ? _value : throw new InvalidOperationException("The validation is a failure.");
        public IReadOnlyList<Error> Errors => Guard() == 2 ? _errors! : throw new InvalidOperationException("The validation is a success.");
        public bool TryGetValue([MaybeNullWhen(false)] out T value) { value = _value; return Guard() == 1; }
        public bool TryGetErrors([MaybeNullWhen(false)] out IReadOnlyList<Error> errors) { errors = _errors; return Guard() == 2; }

        private byte Guard() => _state != 0 ? _state : throw new InvalidOperationException("<default Validation>: a validation has no default value.");

        public bool Equals(Validation<T> other) => _state == other._state
            && (_state != 1 || EqualityComparer<T>.Default.Equals(_value, other._value))
            && (_state != 2 || _errors!.SequenceEqual(other._errors!));
        public override bool Equals(object? obj) => obj is Validation<T> other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(_state, _state == 1 ? _value : default, _errors?.Count);
        public override string ToString() => _state switch { 0 => "<default Validation>", 1 => $"Ok({_value})", _ => $"Fail({string.Join(", ", _errors!)})" };
        public static bool operator ==(Validation<T> left, Validation<T> right) => left.Equals(right);
        public static bool operator !=(Validation<T> left, Validation<T> right) => !left.Equals(right);
    }

    public static class Validation
    {
        public static Validation<T, TError> Ok<T, TError>(T value) => new(1, value, null);
        public static Validation<T, TError> Fail<T, TError>(TError error) => new(2, default!, new[] { error });
        public static Validation<T, TError> Fail<T, TError>(IEnumerable<TError> errors) => new(2, default!, NonEmpty(errors));
        public static Validation<T> Ok<T>(T value) => new(1, value, null);
        public static Validation<T> Fail<T>(Error error) => new(2, default!, new[] { error ?? throw new ArgumentNullException(nameof(error)) });
        public static Validation<T> Fail<T>(IEnumerable<Error> errors) => new(2, default!, NonEmpty(errors));

        static TItem[] NonEmpty<TItem>(IEnumerable<TItem> items)
        {
            ArgumentNullException.ThrowIfNull(items);
            var array = items.ToArray();
            return array.Length > 0 ? array : throw new ArgumentException("A validation failure has at least one error.", nameof(items));
        }
    }
}

namespace Resultful.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ClosedHierarchyAttribute : Attribute
    {
    }
}
