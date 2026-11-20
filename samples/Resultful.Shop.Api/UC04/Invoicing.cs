using Resultful.AspNetCore;
using Resultful.Shop.Domain;

namespace Resultful.Shop.Api;

// A database that times out for order 42 after 30 seconds.
internal sealed class SlowDb
{
    public bool TryQuery(int id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out OrderRow? row, out TimeSpan elapsed)
    {
        elapsed = id == 42 ? TimeSpan.FromSeconds(30) : TimeSpan.FromMilliseconds(3);
        row = id == 42 ? null : new OrderRow(id, "teapot", 25m, "4242");
        return row is not null;
    }
}

internal sealed record Retry(TimeSpan? Delay)
{
    public static Retry Never { get; } = new((TimeSpan?)null);
    public static Retry After(TimeSpan delay) => new(delay);
}

internal sealed partial class Invoicing(ILogger<Invoicing> log)
{
    private readonly SlowDb _db = new();
    private readonly ILogger _log = log;

    private static Order ToOrder(OrderRow row) => new(row.Id, row.Name, row.Total, row.Card);

    #region snippet: uc04-layers
    // data layer
    public Result<OrderRow> Find(int id) =>
        _db.TryQuery(id, out var row, out var elapsed) ? row : new DbTimeout(elapsed);

    // service layer
    public Result<Order> LoadForInvoice(int id) =>
        Find(id)
            .Map(ToOrder)
            .MapError(e => e.WithContext($"loading order {id} for invoicing"));
    #endregion

    // edge: a background invoicing job
    public Retry RunJob(int id)
    {
        #region snippet: uc04-edge
        var result = LoadForInvoice(id);
        if (result.TryGetError(out var error))
        {
            _log.LogWarning("{Error} ({Context})", error.Message, string.Join(" → ", error.Context));
            return error is DbTimeout timeout ? Retry.After(timeout.After * 2) : Retry.Never;
        }
        #endregion

        return Retry.Never;
    }

    // "Using it well": a layer that turns the failure into a different one wraps it with Cause, and the edge finds the
    // timeout with TryFind, which walks the Cause chain. The lambda says it returns Error: without that, MapError's
    // generic overload wins and the result becomes Result<Order, InvoiceFailed> (a spec finding in the PR body).
    public Result<Order> Issue(int id) =>
        LoadForInvoice(id).MapError(Error (e) => new InvoiceFailed(id) { Cause = e });

    public Retry RunIssueJob(int id) =>
        Issue(id).TryGetError(out var error) && error.TryFind<DbTimeout>(out var timeout)
            ? Retry.After(timeout.After * 2)
            : Retry.Never;
}

internal static class InvoiceEndpoints
{
    // DbTimeout isn't mapped: a 500 with a fixed title and no Message outside Development.
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/invoices/{id}", (int id, Invoicing invoicing) => invoicing.LoadForInvoice(id).ToHttpResult());
        app.MapPost("/invoices/{id}/run", (int id, Invoicing invoicing) => Results.Ok(invoicing.RunJob(id)));
        app.MapPost("/invoices/{id}/issue", (int id, Invoicing invoicing) => Results.Ok(invoicing.RunIssueJob(id)));
    }
}
