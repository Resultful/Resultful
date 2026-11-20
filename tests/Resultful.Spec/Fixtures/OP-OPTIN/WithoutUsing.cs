// Decision 9 (18): without `using Resultful.Operators;` there are no operators; `+` exists nowhere (DECISIONS-TAKEN).
using Resultful;

public static class WithoutUsing
{
    static Result<int> A(Result<int> a, Result<int> b) => a | b; // expect: CS0019
}
