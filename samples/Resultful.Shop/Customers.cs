using Resultful.CodeAnalysis;

namespace Resultful.Shop;

// A result over a union of errors where one case is itself a union: CustomerError is CustomerNotFound or an
// AccountError, and AccountError is Suspended or Unverified. [ResultShape] turns that into CustomerResult, so a
// lookup returns any leaf, inner ones included, with no cast, and a switch over it is checked case by case.

public sealed record Customer(string Id, string Name);

public sealed record CustomerNotFound(string Id) : Error($"customer {Id} was not found");

public sealed record Suspended(string Id) : Error($"customer {Id} is suspended");

public sealed record Unverified(string Id) : Error($"customer {Id} has not verified their email");

public partial union AccountError(Suspended, Unverified);

public partial union CustomerError(CustomerNotFound, AccountError);

[ResultShape(typeof(CustomerError), Success = typeof(Customer))]
public readonly partial struct CustomerResult;

public static class Customers
{
    private static readonly Dictionary<string, (string Name, string State)> Known = new()
    {
        ["C-1"] = ("Ada", "active"),
        ["C-2"] = ("Bo", "suspended"),
        ["C-3"] = ("Cy", "unverified"),
    };

    public static CustomerResult Find(string id)
    {
        if (!Known.TryGetValue(id, out var known)) return new CustomerNotFound(id);
        if (known.State == "suspended") return new Suspended(id);
        if (known.State == "unverified") return new Unverified(id);
        return new Customer(id, known.Name);
    }

    /// <summary>A switch over the shape, then over the inner union: no discard arm at either level.</summary>
    public static string Describe(CustomerResult result) => result switch
    {
        Customer c => $"ok:{c.Name}",
        CustomerNotFound n => $"missing:{n.Id}",
        AccountError a => a switch
        {
            Suspended s => $"suspended:{s.Id}",
#if !BREAK_INNER
            Unverified u => $"unverified:{u.Id}",
#endif
        },
    };
}
