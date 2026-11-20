// spec: uses generator
using Resultful;

// A different error hierarchy doesn't convert in without a mapping. Its own file, so the cases still run.
public static class NoMapping
{
    static PlaceOrderResult FromOtherHierarchy(PaymentResult p) => p; // expect: CS0029
}
