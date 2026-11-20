// RF0002 (16a): a result created with default, as an expression or a literal.
// spec: uses analyzers
using Resultful;
using Resultful.CodeAnalysis;

public static class Cases
{
    static Result<int> A() => default(Result<int>); // expect: RF0002
    static Result<int, string> B() => default; // expect: RF0002
    static Option<int> C() => default;
}
