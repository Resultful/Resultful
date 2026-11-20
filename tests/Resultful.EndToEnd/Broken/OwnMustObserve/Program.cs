using Resultful.Shop.Legacy;

namespace Resultful.EndToEnd.Broken;

// Use case 9: the library's own [MustObserve] reaches its callers without a Resultful reference.
public static class OwnMustObserve
{
    public static void Send(ShopClient client, object order) => client.Post(order);
}
