using Resultful;
using Resultful.CodeAnalysis;

[ClosedHierarchy]
public abstract record OrderError : Error
{
    private protected OrderError(string message) : base(message) { }
}
public sealed record OrderLocked() : OrderError("locked");
