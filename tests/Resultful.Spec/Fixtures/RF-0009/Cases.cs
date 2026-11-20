// Quality (16b): RF0009, Map whose selector returns a result (Result<Result<T>>). Fix: Bind.
// spec: uses analyzers
using Resultful;
using Resultful.CodeAnalysis;

public static class Cases
{
    static Result<int> Parse(string s) => int.TryParse(s, out var v) ? v : Error.From("nan");
    static Result<Result<int>> Nested(Result<string> r) => r.Map(Parse); // expect: RF0009
    static Result<int> Flat(Result<string> r) => r.Bind(Parse);
}
// expect-fix: RF0009
