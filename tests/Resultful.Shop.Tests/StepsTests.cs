using CsCheck;

namespace Resultful.Shop.Tests;

// Proposed decision 24: steps as an iterator over CustomerResult, read three ways so they can be compared: the
// generated drivers (CustomerResult.Yield), the core's generic shape drivers (type arguments spelled out), and the
// core's drivers over Result<Customer, CustomerError> (no shape).
public sealed class StepsTests
{
    // Every delegate must be an active customer; the owner comes back only if nothing failed.
    static IEnumerable<CustomerResult> Owner(string id, IEnumerable<string> delegates)
    {
        foreach (var d in delegates)
        {
            var found = Customers.Find(d);
            if (!found.TryGetValue(out Customer? _)) yield return found;
        }
        yield return Customers.Find(id);
    }

    // The same steps over the core type. A leaf can't be yielded here on net11.0: `yield return new CustomerNotFound(id)`
    // is CS0029 (union leaf to union to Result would be two user-defined conversions); on net10.0 it compiles.
    static IEnumerable<Result<Customer, CustomerError>> OwnerCore(string id, IEnumerable<string> delegates)
    {
        foreach (var d in delegates)
        {
            var found = Customers.Find(d).AsResult();
            if (!found.TryGetValue(out Customer? _)) yield return found;
        }
        yield return Customers.Find(id).AsResult();
    }

    // Fail fast through dependent steps; the log shows how far it ran and that finally ran.
    static IEnumerable<CustomerResult> Transfer(string from, string to, List<string> log)
    {
        try
        {
            var source = Customers.Find(from);
            log.Add("source");
            if (!source.TryGetValue(out Customer? _)) { yield return source; yield break; }
            var target = Customers.Find(to);
            log.Add("target");
            if (!target.TryGetValue(out Customer? t)) { yield return target; yield break; }
            if (t.Name.Length > 10) yield return new Unverified(t.Id); // an inner leaf, no wrapping
            yield return t;
        }
        finally
        {
            log.Add("finally");
        }
    }

    static string Text(CustomerResult r) => Customers.Describe(r);

    static string TextAll(Validation<Customer, CustomerError> v) =>
        v.TryGetValue(out var c) ? Text(c) : string.Join(",", v.Errors.Select(e => Text(e)));

    [Fact]
    public void Yield_gathers_every_error_in_order_and_the_three_forms_agree()
    {
        string[] delegates = ["C-2", "C-9", "C-1", "C-3"];
        var generated = CustomerResult.Yield(Owner("C-1", delegates));
        var genericShape = Result.Yield<CustomerResult, Customer, CustomerError>(Owner("C-1", delegates));
        var core = Result.Yield(OwnerCore("C-1", delegates));

        Assert.Equal("suspended:C-2,missing:C-9,unverified:C-3", TextAll(generated));
        Assert.Equal(TextAll(generated), TextAll(genericShape));
        Assert.Equal(TextAll(generated), TextAll(core));
    }

    [Fact]
    public void Yield_returns_the_success_when_it_is_the_only_step_left()
    {
        Assert.Equal("ok:Ada", TextAll(CustomerResult.Yield(Owner("C-1", ["C-1"]))));
        Assert.Equal("missing:C-9", TextAll(CustomerResult.Yield(Owner("C-9", []))));
    }

    [Fact]
    public void YieldFirst_stops_at_the_first_error_and_runs_finally()
    {
        var log = new List<string>();
        Assert.Equal("suspended:C-2", Text(CustomerResult.YieldFirst(Transfer("C-2", "C-1", log))));
        Assert.Equal(["source", "finally"], log);

        log.Clear();
        Assert.Equal("ok:Ada", Text(CustomerResult.YieldFirst(Transfer("C-1", "C-1", log))));
        Assert.Equal(["source", "target", "finally"], log);
    }

    [Fact]
    public void Success_counting_and_the_zero_value_throw()
    {
        static IEnumerable<CustomerResult> None() { yield break; }
        static IEnumerable<CustomerResult> Two() { yield return new Customer("C-1", "Ada"); yield return new Customer("C-1", "Ada"); }
        static IEnumerable<CustomerResult> Zero() { yield return default; }

        Assert.Throws<InvalidOperationException>(() => CustomerResult.Yield(None()));
        Assert.Throws<InvalidOperationException>(() => CustomerResult.Yield(Two()));
        Assert.Throws<InvalidOperationException>(() => CustomerResult.YieldFirst(None()));
        Assert.Throws<InvalidOperationException>(() => CustomerResult.YieldFirst(Two()));
        Assert.Throws<InvalidOperationException>(() => CustomerResult.Yield(Zero()));
        Assert.Throws<InvalidOperationException>(() => CustomerResult.YieldFirst(Zero()));
    }

    [Fact]
    public void Steps_are_lazy_and_a_second_read_repeats_them()
    {
        var log = new List<string>();
        var steps = Transfer("C-1", "C-1", log);
        Assert.Empty(log);
        CustomerResult.YieldFirst(steps);
        CustomerResult.YieldFirst(steps);
        Assert.Equal(6, log.Count);
    }

    static readonly Gen<string> Id = Gen.OneOf(Gen.Const("C-1"), Gen.Const("C-2"), Gen.Const("C-3"), Gen.Const("C-9"));

    [Fact]
    public void Law_Yield_is_the_failures_in_order_else_the_owner() =>
        Gen.Select(Id, Id.Array[0, 6]).Sample((owner, delegates) =>
        {
            var failures = delegates.Append(owner).Select(Customers.Find).Where(r => !r.TryGetValue(out Customer? _)).Select(Text).ToList();
            var expected = failures.Count > 0 ? string.Join(",", failures) : Text(Customers.Find(owner));
            return TextAll(CustomerResult.Yield(Owner(owner, delegates))) == expected
                && TextAll(Result.Yield(OwnerCore(owner, delegates))) == expected;
        });

    [Fact]
    public void Law_YieldFirst_is_the_first_failure_else_the_owner() =>
        Gen.Select(Id, Id.Array[0, 6]).Sample((owner, delegates) =>
        {
            var steps = delegates.Append(owner).Select(Customers.Find).ToList();
            var expected = Text(steps.FirstOrDefault(r => !r.TryGetValue(out Customer? _), Customers.Find(owner)));
            return Text(CustomerResult.YieldFirst(Owner(owner, delegates))) == expected
                && Text((CustomerResult)Result.YieldFirst(OwnerCore(owner, delegates))) == expected;
        });
}
