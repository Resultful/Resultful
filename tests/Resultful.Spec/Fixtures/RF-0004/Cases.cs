// RF0004 (16b): a bare string becomes the success of a Result<string> (in 0.2 it was a failure).
// spec: uses analyzers
using Resultful;
using Resultful.CodeAnalysis;

public static class Cases
{
    static Result<string> Name() => "bob"; // expect: RF0004
    static Result<string> Explicit() => Result.Ok("bob");
    static Result<int> NotAString() => 5;
}
