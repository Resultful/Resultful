using System.Reflection;

public abstract record Error(string Message);
public sealed record OrderNotFound(int Id) : Error("nf");
public sealed record OrderLocked(int Id) : Error("locked");
public sealed record Order(int Id);
public union OrderError(OrderNotFound, OrderLocked);

public readonly struct Res<T, TError>
{
    public readonly bool Ok; public readonly T? Value; public readonly TError? Err;
    Res(bool ok, T? v, TError? e) { Ok = ok; Value = v; Err = e; }
    public static Res<T, TError> Fail(TError e) => IsEmpty(e) ? throw new InvalidOperationException("empty") : new(false, default, e);
    public static implicit operator Res<T, TError>(T v) => new(true, v, default);
    public static implicit operator Res<T, TError>(TError e) => Fail(e);
    static readonly bool IsUnion = typeof(System.Runtime.CompilerServices.IUnion).IsAssignableFrom(typeof(TError)) && typeof(TError).IsValueType;
    static bool IsEmpty(TError e) => IsUnion
        ? (System.Runtime.CompilerServices.Unsafe.SizeOf<TError>() == System.IntPtr.Size
            ? System.Runtime.CompilerServices.Unsafe.As<TError, object?>(ref e) is null
            : e is System.Runtime.CompilerServices.IUnion { Value: null })
        : e is null;
}

public static class ErrExt { public static string Describe<TError>(this TError e) where TError : Error => "error:" + e.Message; }
public static class UnionExt { public static string Describe<TError>(this TError e) where TError : struct, System.Runtime.CompilerServices.IUnion => "union:" + ((Error)e.Value!).Message; }

public static class P
{
    static Res<Order, OrderError> ViaUnion() => (OrderError)new OrderNotFound(1);

    public static void Main()
    {
        foreach (var m in typeof(OrderError).GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)) Console.WriteLine("member " + m);
        Console.WriteLine("ifaces " + string.Join(",", typeof(OrderError).GetInterfaces().Select(i => i.FullName)));
        Console.WriteLine(new OrderNotFound(1).Describe());
        OrderError u = new OrderLocked(2);
        Console.WriteLine(u.Describe());
        Console.WriteLine("via union ok=" + ViaUnion().Ok);
        try { Res<Order, OrderError>.Fail(default); Console.WriteLine("default accepted"); } catch (InvalidOperationException) { Console.WriteLine("default rejected"); }
        for (int i = 0; i < 1000; i++) Res<Order, OrderError>.Fail(u);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) Res<Order, OrderError>.Fail(u);
        Console.WriteLine("alloc per Fail: " + (GC.GetAllocatedBytesForCurrentThread() - before) / 1000.0);
    }
}
