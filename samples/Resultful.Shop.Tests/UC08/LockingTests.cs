using NUnit.Framework;
using Resultful.Shop.Api;
using Resultful.Shop.Domain;

namespace Resultful.Shop.Tests;

public sealed class LockingTests
{
    private readonly Orders _orders = new();

    #region snippet: uc08-test
    [Test]
    public void A_locked_order_cannot_be_changed()
    {
        var result = _orders.Rename(42, "new name");

        Assert.That(result.TryGetError(out var error) && error is OrderLocked { Id: 42, By: "alice" });
    }
    #endregion
}
