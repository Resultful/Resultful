using Resultful.CodeAnalysis;

// Leaves whose simple names collide with Leaves.cs on purpose: the same wire name from another type.
namespace Resultful.Text.Json.Tests.Other
{
    public sealed record OrderNotFound(string Key) : Error("other");
}

#if NET11_0_OR_GREATER
// UERR-JSON-FLAT's [ClosedHierarchy] twin of the OrderError union.
namespace Resultful.Text.Json.Tests.Twin
{
    [ClosedHierarchy]
    public abstract record OrderError : Error
    {
        private protected OrderError(string m) : base(m)
        {
        }
    }

    public sealed record OrderLocked(int Id, string By) : OrderError($"order {Id} is locked by {By}");
}
#endif
