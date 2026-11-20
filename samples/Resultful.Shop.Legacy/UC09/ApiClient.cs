// Use case 9: an HTTP client library with its own result type. It doesn't reference Resultful; its own internal
// copy of the attribute, matched by full name, is enough for Resultful.Analyzers to warn its callers.
#region snippet: uc09-attribute
// inside the library: its own copy, matched by name
namespace Resultful.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class | AttributeTargets.Method)]
    internal sealed class MustObserveAttribute : Attribute;
}
#endregion

namespace Resultful.Shop.Legacy
{
    using Resultful.CodeAnalysis;

    #region snippet: uc09-type
    [MustObserve]
    public readonly struct ApiResponse<T>
    #endregion
        : IEquatable<ApiResponse<T>>
    {
        public ApiResponse(int status, T? body)
        {
            Status = status;
            Body = body;
        }

        public int Status { get; }
        public T? Body { get; }
        public bool IsSuccess => Status is >= 200 and < 300;

        public bool Equals(ApiResponse<T> other) => Status == other.Status && EqualityComparer<T?>.Default.Equals(Body, other.Body);
        public override bool Equals(object? obj) => obj is ApiResponse<T> other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Status, Body);
        public static bool operator ==(ApiResponse<T> left, ApiResponse<T> right) => left.Equals(right);
        public static bool operator !=(ApiResponse<T> left, ApiResponse<T> right) => !left.Equals(right);
    }

    public sealed record Receipt(string Id);

    public sealed class ShopClient
    {
        // A caller that writes `client.Post(order);` and drops the response gets RF0001 (16a, fixture RF-OWN-TYPE).
        public ApiResponse<Receipt> Post(object order) =>
            order is null ? new ApiResponse<Receipt>(400, null) : new ApiResponse<Receipt>(201, new Receipt("r-1"));
    }
}
