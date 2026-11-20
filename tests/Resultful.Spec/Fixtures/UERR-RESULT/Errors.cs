// spec: tfm net11.0
using Resultful;

// A bare leaf doesn't chain leaf -> union -> result (CS0029, documented). Its own file, so the cases still run.
public static class BareLeaf
{
    static Result<Order, OrderError> Bare() => new OrderNotFound(1); // expect: CS0029
}
