using Resultful.Shop.Domain;

namespace Resultful.Shop.Api;

internal sealed partial class Orders
{
    // Use case 8: renaming a locked order fails with OrderLocked; samples/Resultful.Shop.Tests checks the type, not the text.
    public Result<Order, OrderError> Rename(int id, string name) =>
        Get(id).Map(order => order with { Name = name });
}
