using Resultful;
using Resultful.CodeAnalysis;
using Resultful.Spec.Support;

namespace Twin
{
    [ClosedHierarchy]
    public abstract record OrderError : Error { private protected OrderError(string m) : base(m) { } }
    public sealed record OrderNotFound(int Id) : OrderError($"order {Id} was not found");
    public sealed record OrderLocked(int Id, string By) : OrderError($"order {Id} is locked by {By}");

    public static class HierarchySide
    {
        public static Result<global::Order, OrderError> Get(int id) => id switch
        {
            1 => new global::Order(1),
            2 => new OrderLocked(2, "ann"),
            _ => new OrderNotFound(id),
        };

        public static string Describe(int id) => Get(id).Match(o => "ok " + o.Id, e => e switch
        {
            OrderNotFound nf => "404 " + nf.Id,
            OrderLocked l => "409 " + l.By,
            _ => throw new System.InvalidOperationException(),
        });

        public static Result<global::Order> Widened(int id) => Get(id).AsResult();
    }
}

public static class Cases
{
    [SpecCase]
    public static void The_union_and_its_twin_give_the_same_output()
    {
        foreach (var id in new[] { 1, 2, 3 })
        {
            Check.Equal(Twin.HierarchySide.Describe(id), UnionSide.Describe(id), "describe " + id);
            Check.Equal(Twin.HierarchySide.Widened(id).Match(o => o.Id.ToString(), e => e.Message), UnionSide.Widened(id).Match(o => o.Id.ToString(), e => e.Message), "widened " + id);
        }
    }

    [SpecCase]
    public static void Widening_a_union_reaches_the_leaf()
    {
        Check.True(UnionSide.Widened(2).TryGetError(out var e) && e is OrderLocked { By: "ann" }, "the leaf");
    }
}
