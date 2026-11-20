namespace Resultful.Shop.Domain;

// Use case 4: a database failure, explained on the way up.
#region snippet: uc04-error
public sealed record DbTimeout(TimeSpan After) : Error($"the database timed out after {After.TotalSeconds}s");
#endregion

// "Using it well": a layer that turns a failure into a different one wraps it with Cause.
public sealed record InvoiceFailed(int OrderId) : Error($"invoice for order {OrderId} failed");
