// RF0001 (16a): a [MustObserve] value thrown away. Only an explicit `_ =` passes. Fix: discard.
// spec: uses analyzers
using Resultful;
using Resultful.CodeAnalysis;

public static class Cases
{
    static Result<int> Cancel(int id) => Result.Ok(id);

    static void Forgets() { Cancel(1); } // expect: RF0001
    static void Discards() { _ = Cancel(1); }
    static int Uses() => Cancel(1).ValueOr(0);
}
// expect-fix: RF0001
