// RF0003 (16b): Value read without checking IsSuccess.
// spec: uses analyzers
using Resultful;
using Resultful.CodeAnalysis;

public static class Cases
{
    static int Unchecked(Result<int> r) => r.Value; // expect: RF0003
    static int Checked(Result<int> r) => r.IsSuccess ? r.Value : 0;
    static int Pattern(Result<int> r) => r.TryGetValue(out var v) ? v : 0;
}
