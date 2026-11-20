// Decision 14 (06, 09): no implicit conversion from string to an error; Result<int> F() => "bad" stops compiling.
using Resultful;

public static class Cases
{
    static Error Bad() => "bad"; // expect: CS0029
    static Result<int> F() => "bad"; // expect: CS0029
    static Result<int> G() => Error.From("bad");
    static Result<int> H() => Result.Fail<int>("bad");
}
