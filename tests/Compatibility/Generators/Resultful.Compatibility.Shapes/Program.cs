using System;
using System.Linq;
using System.Threading.Tasks;
using Compatibility.Domain;
using Resultful;
using Resultful.CodeAnalysis;
using Resultful.LINQ;

namespace Compatibility.Shapes
{
    [ResultShape(typeof(OrderError), Success = typeof(Order))]
    public readonly partial struct PlaceOrderResult;

#if NET11_0_OR_GREATER
    [ResultShape(typeof(CheckoutError), Success = typeof(Order))]
    public readonly partial struct CheckoutResult;
#endif

    public static class Program
    {
        // One line per check; Resultful.Compatibility.Tests compares the output.
        public static void Main()
        {
            PlaceOrderResult ok = new Order(1);
            PlaceOrderResult locked = new OrderLocked(2);
            Console.WriteLine("match: " + string.Join(", ", Name(ok), Name(locked), Name(new OrderNotFound(3))));
            Result<Order> wide = locked.WithContext("placing");
            Console.WriteLine($"widen: {wide.Error is OrderLocked} {string.Join("|", wide.Error.Context)}");
            Console.WriteLine("cases: " + string.Join("|", OrderError.Cases.Select(t => t.Name)));
#pragma warning disable RF0002 // the zero value is the point of this check
            Console.WriteLine($"zero: {default(PlaceOrderResult).IsDefault}");
#pragma warning restore RF0002
            Console.WriteLine($"closed: {Code(new OrderLocked(2))},{Code(new OrderNotFound(3))},{Code(null)},{Ship(new Pending())}");
            // Decision 22, written as C# 12 needs it (net8.0 ignores OverloadResolutionPriority): the type argument named.
            Result<int, OrderError> ensured = from o in ok from _ in Result.Ensure<OrderError>(o.Id > 0, new OrderNotFound(o.Id)) select o.Id * 10;
            Result<int, OrderError> refused = from o in ok from _ in Result.Ensure<OrderError>(o.Id > 5, new OrderNotFound(o.Id)) select o.Id;
            var later = (from o in ok from n in Later(o) select n + 1).GetAwaiter().GetResult();
            var stopped = (from o in locked from n in Later(o) select n).GetAwaiter().GetResult();
            var id = 1;
            Console.WriteLine($"query: {Show(ensured)} {Show(refused)} {Show(later)} {Show(stopped)} {Result.Ensure(id > 5).Match(_ => "ok", e => e.Message)}");
            Console.WriteLine($"union: {typeof(PlaceOrderResult).GetInterfaces().Any(i => i.Name == "IUnion")}");
#if DROP_RESULT
            // Built with DROP_RESULT, a result thrown away must fail the build with RF0001.
            locked.WithContext("dropped");
#endif
#if NET11_0_OR_GREATER
            Console.WriteLine("switch: " + string.Join(",", Status(ok), Status(locked), Status(new OrderNotFound(3))));
            CheckoutResult declined = new CardDeclined("limit");
            Result<Order> checkout = declined;
            Console.WriteLine($"error union: {Checkout(declined)} {Checkout(new OrderNotFound(4))} {checkout.Error.GetType().Name} {string.Join("|", CheckoutError.Cases.Select(t => t.Name))}");
#endif
        }

        private static string Show(Result<int, OrderError> r) => r.Match(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture), e => e.GetType().Name);

        private static async Task<Result<int, OrderError>> Later(Order o)
        {
            await Task.Yield();
            return o.Id + 100;
        }

        private static string Name(PlaceOrderResult r) =>
            r.Match((Order o) => "order " + o.Id, (OrderLocked l) => "locked " + l.Id, (OrderNotFound nf) => "missing " + nf.Id);

        // RFSP0001, on every leg: a closed hierarchy from another assembly, switched with no discard. A nullable input
        // needs its null arm. Built with DROP_CLOSED_ARM, it must fail with CS8509.
        private static int Code(OrderError? e) => e switch
        {
            OrderLocked => 409,
#if !DROP_CLOSED_ARM
            OrderNotFound => 404,
#endif
            null => 0,
        };

        // A base listed in .editorconfig instead of carrying [ClosedHierarchy].
        private static int Ship(Shipment s) => s switch
        {
            Shipped => 1,
            Pending => 2,
        };

#if NET11_0_OR_GREATER
        // Exhaustive with no discard: built with DROP_ARM, it must fail with CS8509.
        private static int Status(PlaceOrderResult r) => r switch
        {
            Order => 201,
            OrderLocked => 409,
#if !DROP_ARM
            OrderNotFound => 404,
#endif
        };

        private static string Checkout(CheckoutResult r) => r switch
        {
            Order => "paid",
            OrderNotFound => "missing",
            PaymentError p => p switch
            {
                CardDeclined d => "declined " + d.Reason,
                CardExpired => "expired",
            },
        };
#endif
    }
}
