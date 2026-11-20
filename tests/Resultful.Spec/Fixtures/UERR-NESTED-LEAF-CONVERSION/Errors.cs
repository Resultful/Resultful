// spec: tfm net11.0
using Resultful;

// A bare Result<T, OrderError> still needs a cast. Its own file, so the cases still run.
public static class BareLeaf
{
    static Result<Order, OrderError> Core() => new CardDeclined("x"); // expect: CS0029
}
