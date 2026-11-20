// GEN-DIAG (13a): the edge cases the generator handles with no diagnostic compile clean: deep nesting, an interface Success,
// nested declarations in partial types, a valueless shape.
// expect-clean
// spec: uses generator
using Resultful;
using Resultful.CodeAnalysis;

[ClosedHierarchy] public abstract partial record AppError : Error;
[ClosedHierarchy] public abstract partial record OrderFault : AppError;
[ClosedHierarchy] public abstract partial record LockFault : OrderFault;
public sealed record LockedByUser(string By) : LockFault("locked");
public sealed record Missing() : OrderFault("missing");
public sealed record Declined() : AppError("declined");
public interface IOrder { int Id { get; } }

public static partial class Api
{
    [ResultShape(typeof(AppError), Success = typeof(IOrder))] public readonly partial struct GetResult;
    [ResultShape(typeof(AppError))] public readonly partial struct CancelResult;
}
