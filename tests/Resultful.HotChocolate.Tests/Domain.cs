using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Resultful.CodeAnalysis;

namespace Resultful.HotChocolate.Tests;

// The errors and shapes the tests resolve, written as an app writes them: Resultful.Generators adds each hierarchy's
// constructor and Cases, and each shape's members.

public sealed record User(int Id, string Name);

[ClosedHierarchy]
public abstract partial record UserError : Error;

public sealed record UserNotFound(int Id) : UserError($"User {Id} was not found.");

public sealed record UserSuspended(int Id, string Reason) : UserError($"User {Id} is suspended.");

[ResultShape(typeof(UserError), Success = typeof(User))]
public readonly partial struct FindUserResult;

// Gathers every failure: a failed check is one ValidationError listing them all.
[ResultShape(typeof(UserError), Success = typeof(User), ManyErrors = true)]
public readonly partial struct CheckUsersResult;

// A hierarchy nested in another: GraphQL shows its leaves next to the outer ones, while C# can still match the middle.
[ClosedHierarchy]
public abstract partial record CheckoutError : Error;

public sealed record CartEmpty() : CheckoutError("The cart is empty.");

[ClosedHierarchy]
public abstract partial record PaymentError : CheckoutError;

public sealed record CardDeclined(string Reason) : PaymentError("The card was declined.");

public sealed record CardExpired(int Month, int Year) : PaymentError("The card has expired.");

// Seven leaves: more than FieldResult's five.
[ClosedHierarchy]
public abstract partial record ShippingError : Error;

public sealed record AddressMissing() : ShippingError("The address is missing.");

public sealed record PostcodeInvalid(string Postcode) : ShippingError("The postcode is invalid.");

public sealed record CountryBlocked(string Country) : ShippingError("The country can't be shipped to.");

public sealed record CarrierDown(string Carrier) : ShippingError("The carrier is down.");

public sealed record ParcelTooHeavy(decimal Kilograms) : ShippingError("The parcel is too heavy.");

public sealed record ParcelTooLarge(int Centimetres) : ShippingError("The parcel is too large.");

public sealed record HolidayClosure(string Holiday) : ShippingError("The warehouse is closed for a holiday.");

// Validation errors and an error no field declares.
public sealed record Required(string Field) : Error($"{Field} is required.");

public sealed record TooLong(string Field, int Max) : Error($"{Field} is longer than {Max} characters.");

public sealed record DatabaseDown(string Server) : Error("The database is down.");

// Holds personal data in its own property; its Message doesn't.
public sealed record AccountLocked(string Email) : Error("The account is locked.");

// The order scenario.
public sealed record Order(int Id, string Sku, int Quantity);

[ClosedHierarchy]
public abstract partial record OrderError : Error;

public sealed record OutOfStock(string Sku) : OrderError($"{Sku} is out of stock.");

public sealed record CustomerBlocked(int CustomerId) : OrderError("The customer can't order.");

public static class Sentinel
{
    /// <summary>Must never appear in generic output: messages, extensions, top-level errors.</summary>
    public const string Email = "sentinel-7f3a@example.invalid";
}

/// <summary>An <see cref="IHostEnvironment"/> with only a name, for Development and Production.</summary>
public sealed class TestEnvironment(string name) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = name;

    public string ApplicationName { get; set; } = "Resultful.HotChocolate.Tests";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
