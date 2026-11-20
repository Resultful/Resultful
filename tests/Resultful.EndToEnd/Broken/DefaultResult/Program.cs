using Resultful;
using Resultful.Shop.Domain;

namespace Resultful.EndToEnd.Broken;

// A result made with default is neither a success nor a failure (decision 10).
public static class DefaultResult
{
    public static Result<Order, OrderError> Get(int id) => id > 0 ? new OrderNotFound(id) : default(Result<Order, OrderError>);
}
