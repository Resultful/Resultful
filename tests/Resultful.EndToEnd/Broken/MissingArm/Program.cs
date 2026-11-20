using Resultful.Shop.Domain;

namespace Resultful.EndToEnd.Broken;

// A switch over a closed hierarchy that forgets a case. The suppressor (RFSP0001) hides CS8509 only when every
// case is matched, so this one stays an error.
public static class MissingArm
{
    public static int Status(OrderError error) => error switch
    {
        OrderNotFound => 404,
    };
}
