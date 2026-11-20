// spec: uses generator
using Resultful;

// On .NET 11 the per-leaf TryGetValue(out Leaf) overloads make `out var` ambiguous: name the type (CS0121).
public static class Ambiguous
{
#if NET11_0_OR_GREATER
    static bool Untyped(PlaceOrderResult r) => r.TryGetValue(out var x); // expect(net11.0): CS0121
#endif
}
