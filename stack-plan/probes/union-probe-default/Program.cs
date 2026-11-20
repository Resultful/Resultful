using System.Runtime.CompilerServices;

public abstract record Error(string Message);
public sealed record OrderNotFound(int Id) : Error($"order {Id} was not found");
public sealed record OrderLocked(int Id, string By) : Error($"order {Id} locked by {By}");
public sealed record Order(int Id);

public interface IErrorUnion { Error Error { get; } }

// 1. union declaration of Error leaves, with a body and an interface
public union OrderError(OrderNotFound, OrderLocked) : IErrorUnion
{
    public Error Error => Value switch { Error e => e, _ => throw new InvalidOperationException() };
}

public static class P
{
    // 2. exhaustive switch over the union
    static string Describe(OrderError e) => e switch
    {
        OrderNotFound nf => $"nf {nf.Id}",
        OrderLocked l => $"locked {l.By}",
    };

    // 3. generic constraint usable?
    static string Msg<TError>(TError e) where TError : IErrorUnion => e.Error.Message;

    public static void Main()
    {
        OrderError a = new OrderNotFound(1);
        OrderError b = new OrderLocked(2, "alice");
        Console.WriteLine(Describe(a));
        Console.WriteLine(Describe(b));
        Console.WriteLine(Msg(b)); OrderResult r = (OrderError)new OrderLocked(3,"bob"); Console.WriteLine(N.Grouped(r));
        OrderError z = default;
        try { Console.WriteLine(Describe(z)); } catch (Exception ex) { Console.WriteLine("default: " + ex.GetType().Name); }
    }
}
