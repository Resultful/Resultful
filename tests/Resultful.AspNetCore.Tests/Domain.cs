using Resultful.CodeAnalysis;

namespace Resultful.AspNetCore.Tests;

public sealed record Order(int Id, string Name);

[ClosedHierarchy]
public abstract record OrderError : Error
{
    private protected OrderError(string message) : base(message) { }
}

public sealed record OrderNotFound(int Id) : OrderError($"order {Id} was not found");
public sealed record OrderLocked(int Id, string By) : OrderError($"order {Id} is locked by {By}");
public sealed record OrderCancelled(int Id) : OrderError($"order {Id} was cancelled");

public sealed record SaveFailed(int Id) : Error($"order {Id} was not saved");
public sealed record DbTimeout(string Query) : Error($"timed out running {Query}");

public sealed record Required(string Field) : Error($"{Field} is required");
public sealed record TooShort(string Field, int Min) : Error($"{Field} needs at least {Min} characters");

// An error that holds others without being a ValidationFailed: the walk reaches them through Inner().
public sealed record BatchFailed(IReadOnlyList<Error> Items) : Error("the batch failed")
{
    public override IEnumerable<Error> Inner() => Items;
}

// Decision 17's shape, written by hand: an error union is a struct that implements IErrorUnion.
public readonly struct PaymentError(Error error) : IErrorUnion
{
    public Error Error { get; } = error;
}

public sealed record CardDeclined() : Error("The card was declined.");
public sealed record FraudSuspected(int RiskScore) : Error($"payment blocked, risk score {RiskScore}");
