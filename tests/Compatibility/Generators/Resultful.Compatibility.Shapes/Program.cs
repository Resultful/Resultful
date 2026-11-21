using System;
using System.Linq;
using Compatibility.Domain;
using Resultful;
using Resultful.CodeAnalysis;

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
            Console.WriteLine($"zero: {default(PlaceOrderResult).IsDefault}");
            Console.WriteLine($"union: {typeof(PlaceOrderResult).GetInterfaces().Any(i => i.Name == "IUnion")}");
#if NET11_0_OR_GREATER
            Console.WriteLine("switch: " + string.Join(",", Status(ok), Status(locked), Status(new OrderNotFound(3))));
            CheckoutResult declined = new CardDeclined("limit");
            Result<Order> checkout = declined;
            Console.WriteLine($"error union: {Checkout(declined)} {Checkout(new OrderNotFound(4))} {checkout.Error.GetType().Name} {string.Join("|", CheckoutError.Cases.Select(t => t.Name))}");
#endif
        }

        private static string Name(PlaceOrderResult r) =>
            r.Match(o => "order " + o.Id, l => "locked " + l.Id, nf => "missing " + nf.Id);

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
