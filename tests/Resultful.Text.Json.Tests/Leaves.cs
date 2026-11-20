using System.Globalization;
using System.Text.Json;
using Resultful.CodeAnalysis;

namespace Resultful.Text.Json.Tests;

public sealed record Order(int Id, string Name);

public sealed record OrderNotFound(int Id) : Error($"order {Id} was not found");

public sealed record OrderLocked(int Id, string By) : Error($"order {Id} is locked by {By}");

public sealed record InvalidItem(decimal Price) : Error("invalid item");

// A closed hierarchy (decision 15), written by hand as Resultful.Generators would leave it.
[ClosedHierarchy]
public abstract record PaymentError : Error
{
    private protected PaymentError(string message) : base(message)
    {
    }

    public static IReadOnlyList<Type> Cases { get; } = [typeof(CardDeclined), typeof(CardExpired)];
}

public sealed record CardDeclined(string Reason) : PaymentError($"card declined: {Reason}");

public sealed record CardExpired(DateOnly On) : PaymentError("card expired");

public static class Wire
{
    public static readonly Type[] AllLeaves = [typeof(OrderNotFound), typeof(OrderLocked), typeof(InvalidItem), typeof(CardDeclined), typeof(CardExpired)];

    /// <summary>Web defaults (camelCase leaf properties), every converter, every leaf registered.</summary>
    public static JsonSerializerOptions Web() => Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    /// <summary>No naming policy: leaf properties keep their C# names; the wire keys stay lowercase.</summary>
    public static JsonSerializerOptions Plain() => Configure(new JsonSerializerOptions());

    private static JsonSerializerOptions Configure(JsonSerializerOptions options) =>
        options.AddResultful().AddErrorCases<Error>(AllLeaves).AddErrorCases<PaymentError>(PaymentError.Cases);

    public static void InCulture(string culture, Action action)
    {
        var saved = CultureInfo.CurrentCulture;
        var savedUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture.Length == 0 ? CultureInfo.InvariantCulture : new CultureInfo(culture);
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
            CultureInfo.CurrentUICulture = savedUi;
        }
    }
}
