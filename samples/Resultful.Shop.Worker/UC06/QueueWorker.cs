using System.Threading.Channels;
using Resultful.Shop.Domain.Fulfilment;

namespace Resultful.Shop.Worker;

internal abstract record Disposition
{
    public static Disposition Ack { get; } = new Acked();
    public static Disposition Retry(TimeSpan after) => new Retried(after);
    public static Disposition DeadLetter(string reason) => new DeadLettered(reason);

    private sealed record Acked : Disposition;
    private sealed record Retried(TimeSpan After) : Disposition;
    private sealed record DeadLettered(string Reason) : Disposition;
}

// Fulfilment fails by order id: 2 out of stock, 3 warehouse offline, 4 unknown, 5 bad address, 6 a wrapped failure.
internal sealed class Fulfilment
{
    public Task<Result<Unit, FulfilmentError>> Start(int orderId) => Task.FromResult<Result<Unit, FulfilmentError>>(orderId switch
    {
        2 => new StockUnavailable("SKU-1"),
        3 => new WarehouseOffline(),
        4 => new OrderNotFound(orderId),
        5 => new AddressInvalid(orderId),
        _ => Unit.Value,
    });
}

internal sealed class OrderPlacedHandler
{
    private readonly Fulfilment _fulfilment = new();

    #region snippet: uc06-handle
    public async Task<Disposition> Handle(OrderPlaced message) =>
        (await _fulfilment.Start(message.OrderId)).Match(
            _ => Disposition.Ack,
            error => error switch                       // FulfilmentError is [ClosedHierarchy]
            {
                StockUnavailable => Disposition.Retry(TimeSpan.FromMinutes(5)),
                WarehouseOffline => Disposition.Retry(TimeSpan.FromSeconds(30)),
                OrderNotFound => Disposition.DeadLetter("unknown order"),
                AddressInvalid => Disposition.DeadLetter("bad address"),
            });
    #endregion

    // When the failure arrives wrapped (an infrastructure error whose Cause is a FulfilmentError), the generated
    // TryMap walks the causes. Typed handlers (RF0008): reordering the cases can't silently swap two decisions.
    public static Disposition HandleWrapped(Error error) =>
        FulfilmentError.TryMap(
            error,
            (AddressInvalid _) => Disposition.DeadLetter("bad address"),
            (OrderNotFound _) => Disposition.DeadLetter("unknown order"),
            (StockUnavailable _) => Disposition.Retry(TimeSpan.FromMinutes(5)),
            (WarehouseOffline _) => Disposition.Retry(TimeSpan.FromSeconds(30)),
            out var disposition)
            ? disposition
            : Disposition.DeadLetter(error.Message);
}

internal static class QueueWorker
{
    public static async Task RunAsync(TextWriter output)
    {
        var queue = Channel.CreateUnbounded<OrderPlaced>();
        for (var id = 1; id <= 5; id++)
        {
            await queue.Writer.WriteAsync(new OrderPlaced(id)).ConfigureAwait(false);
        }

        queue.Writer.Complete();

        var handler = new OrderPlacedHandler();
        await foreach (var message in queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            var disposition = await handler.Handle(message).ConfigureAwait(false);
            await output.WriteLineAsync($"{message.OrderId}: {disposition}").ConfigureAwait(false);
        }

        var wrapped = Error.From("fulfilment call failed") with { Cause = new WarehouseOffline() };
        await output.WriteLineAsync($"wrapped: {OrderPlacedHandler.HandleWrapped(wrapped)}").ConfigureAwait(false);
    }
}
