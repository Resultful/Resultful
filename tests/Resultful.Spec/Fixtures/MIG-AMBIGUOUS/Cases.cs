// Migration (09): Result<Error> converts from both its value and its error, so a bare Error is ambiguous (CS0457).
// Write Result.Ok<Error>(e) or Result.Fail<Error>(e). See RF0004 for Result<string>.
using Resultful;

public static class Cases
{
    static Result<Error> Ambiguous() => Error.From("x"); // expect: CS0457
    static Result<Error> Explicit() => Result.Fail<Error>(Error.From("x"));
}
