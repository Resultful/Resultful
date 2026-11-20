using System.Collections.Generic;
using Resultful;

// Decision 3's correctness matrix, produced in a second assembly.
public static class Producers
{
    public static Result<string, string> SameTypesOk() => Result.Ok<string, string>("value");
    public static Result<string, string> SameTypesFail() => Result.Fail<string, string>("error");
    public static Result<List<string>> ListSuccess() => Result.Ok(new List<string> { "a" });
    public static Result<string?, string> NullSuccess() => Result.Ok<string?, string>(null);
    public static Result<int, string> Zero() => default;
}
