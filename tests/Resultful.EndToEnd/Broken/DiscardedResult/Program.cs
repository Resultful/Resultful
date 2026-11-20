using Resultful;
using Resultful.Shop.Domain;

namespace Resultful.EndToEnd.Broken;

// Use case 7 before the fix: the result of Cancel is thrown away.
public static class DiscardedResult
{
    private static Result<Unit, OrderError> Cancel(int id) => new OrderNotFound(id);

    #region snippet: uc07-broken
    public static void CancelAll(IEnumerable<int> ids)
    {
        foreach (var id in ids)
            Cancel(id);
    }
    #endregion
}
