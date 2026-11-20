// RF0005 (16b, info): a Match whose handlers could be a switch expression.
// spec: uses analyzers
using Resultful;
using Resultful.CodeAnalysis;

public sealed record OrderLocked(int Id) : Error("locked");

public static class Cases
{
    static int Status(Error e) => e is OrderLocked ? 409 : 500;
    static int Map(Result<int> r) => r.Match(v => 200, e => Status(e)); // expect: RF0005
}
