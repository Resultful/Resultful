// spec: tfm net11.0
using Resultful;

// A flat arm for an inner leaf is CS8121. Its own file, so the cases still run.
public static class FlatArms
{
    static string Flat(PlaceOrderResult r) => r switch { Order => "201", OrderNotFound => "404", CardDeclined => "402", ProviderDown => "503" }; // expect: CS8121, CS8121, CS8509
}
