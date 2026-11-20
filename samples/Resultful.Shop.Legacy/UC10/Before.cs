using Microsoft.AspNetCore.Http;

namespace Resultful.Shop.Legacy;

public sealed record LegacyOrder(int Id, string Name);

// Use case 10, before: errors are strings. samples/Resultful.Shop.Api/UC10 is the code it becomes.
public sealed class LegacyOrders
{
    private readonly Dictionary<int, LegacyOrder> _store = new() { [1] = new LegacyOrder(1, "teapot") };

    // 0.2 has no conversion from T to Result<T> (only Result<T, TError> has one), so the docs' `? o.Ok() : "…"` needs
    // Ok() here; see the PR body's spec findings.
    #region snippet: uc10-before
    public Result<LegacyOrder> Get(int id) =>
        _store.GetValueOrDefault(id) is { } o ? o.Ok() : $"order {id} was not found";

    public static IResult Respond(Result<LegacyOrder> result) =>
        result.Match(
            o => Results.Ok(o),
            errors => errors.First().Contains("not found", StringComparison.Ordinal) ? Results.NotFound() : Results.Problem());
    #endregion

    // The migration's hazards (docs/errors-in-practice.md, use case 10).
    // In 0.3 a bare string is no longer an error: CS0029 here.
    public static Result<int> Count() => "the count is unavailable";

    // In 0.3 this still compiles and silently means success; RF0004 flags it (16b).
    public static Result<string> Label() => "no label";

    // In 0.3 VoidResult is [Obsolete] (CS0618): Result<Unit> replaces it.
    public VoidResult Cancel(int id) => _store.Remove(id) ? Result.Ok() : $"order {id} was not found";
}
