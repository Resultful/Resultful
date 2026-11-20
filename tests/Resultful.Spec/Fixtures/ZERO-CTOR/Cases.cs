// Decision 10 layer 2 (08b): `new Result<T>()` doesn't compile.
using Resultful;

public static class Cases
{
    static Result<int> A() => new Result<int>(); // expect: CS0619
    static Result<int, string> B() => new Result<int, string>(); // expect: CS0619
    static Result<int> D() => Result.Ok(1);
}
