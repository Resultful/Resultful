// Decision 15 (16a): rules read attributes by full name, so a library's own internal copy drives them.
// spec: uses analyzers

namespace Resultful.CodeAnalysis
{
    [System.AttributeUsage(System.AttributeTargets.Struct | System.AttributeTargets.Class | System.AttributeTargets.Method)]
    internal sealed class MustObserveAttribute : System.Attribute;
}

[Resultful.CodeAnalysis.MustObserve]
public readonly struct ApiResponse<T> { public T Body { get; init; } }

public static class Client
{
    public static ApiResponse<string> Post() => new() { Body = "ok" };
    static void Forgets() { Post(); } // expect: RF0001
}
