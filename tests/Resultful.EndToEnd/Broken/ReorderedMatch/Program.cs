using Resultful.Shop.Domain;

namespace Resultful.EndToEnd.Broken;

// Typed handlers in an order that isn't the generated one (OrderLocked, then OrderNotFound): the compiler rejects
// the call instead of swapping the two arms.
public static class ReorderedMatch
{
    public static int Status(OrderError error) => error.Match((OrderNotFound _) => 404, (OrderLocked _) => 409);
}
