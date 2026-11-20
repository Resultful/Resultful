using System.Runtime.CompilerServices;

public union OrderResult(Order, OrderError);

public static class N
{
    public static string Grouped(OrderResult r) => r switch
    {
        Order o => $"ok {o.Id}",
        OrderError e => e switch { OrderNotFound nf => $"nf {nf.Id}", OrderLocked l => $"locked {l.By}" },
    };
    public static string MissingArm(OrderError e) => e switch
    {
        OrderNotFound nf => "nf",
    };
}
