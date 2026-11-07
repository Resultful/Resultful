using System.Text.Json.Nodes;
using AwesomeAssertions;
using NUnit.Framework;

namespace Matchmark.Testing.Tests;

/// <summary>The two invariant languages the engine reads: a CEL subset and IDL (inter-parameter dependencies).</summary>
[TestFixture]
public sealed class InvariantLanguageTests
{
    private static readonly JsonNode Order = JsonNode.Parse("""
        {"status":"pending","total":7.5,"items":[{"sku":"TEA-001","quantity":1,"unitPrice":2.5},{"sku":"MUG-002","quantity":2,"unitPrice":2.5}],"note":null}
        """)!;

    [TestCase("self.total == self.items.map(i, i.quantity * i.unitPrice).sum()", true)]
    [TestCase("self.total == 8", false)]
    [TestCase("self.status != 'shipped' || self.items.size() > 0", true)]
    [TestCase("self.items.all(i, i.quantity >= 1)", true)]
    [TestCase("self.items.exists(i, i.sku.startsWith('MUG'))", true)]
    [TestCase("self.items.exists_one(i, i.unitPrice == 2.5)", false)]
    [TestCase("self.items.filter(i, i.quantity > 1).size() == 1", true)]
    [TestCase("self.items.map(i, i.quantity).max() == 2 && self.items.map(i, i.quantity).min() == 1", true)]
    [TestCase("has(self.note) && !has(self.discount)", true)]
    [TestCase("self.status in ['pending', 'paid'] ? self.total > 0 : false", true)]
    [TestCase("self.items[1].sku.matches('^[A-Z]{3}-[0-9]{3}$') && self.items[0].sku.contains('-')", true)]
    [TestCase("size(self.items) == 2 && -self.total < 0 && 7 % 2 == 1 && 10 / 4 == 2.5", true)]
    public void CEL_evaluates_rules_over_self(string rule, bool holds)
        => Cel.Check(rule, Order).Should().Be((holds, (string?)null));

    [Test]
    public void CEL_reports_a_missing_field_or_bad_syntax_as_an_error()
    {
        Cel.Check("self.discount > 0", Order).Error.Should().NotBeNull();
        Cel.Check("self.total ==", Order).Error.Should().NotBeNull();
    }

    private const string Transition =
        "!has(oldSelf.status) || oldSelf.status == self.status || (oldSelf.status == 'pending' && self.status in ['paid', 'cancelled']) || (oldSelf.status == 'paid' && self.status == 'shipped')";

    [TestCase("pending", "pending", true)]
    [TestCase("pending", "paid", true)]
    [TestCase("paid", "shipped", true)]
    [TestCase("pending", "shipped", false)]
    [TestCase("shipped", "pending", false)]
    public void CEL_evaluates_transitions_over_self_and_oldSelf(string before, string after, bool holds)
        => Cel.Check(Transition, new JsonObject { ["status"] = after }, new JsonObject { ["status"] = before }, hasOld: true)
            .Should().Be((holds, (string?)null));

    [TestCase("IF cursor THEN limit;", "cursor,limit", true)]
    [TestCase("IF cursor THEN limit;", "cursor", false)]
    [TestCase("IF cursor THEN limit;", "", true)]
    [TestCase("Or(a, b);", "", false)]
    [TestCase("OnlyOne(a, b);", "a,b", false)]
    [TestCase("AllOrNone(a, b);", "a", false)]
    [TestCase("ZeroOrOne(a, b);", "", true)]
    [TestCase("IF type=='card' THEN card;", "type=card", false)]
    [TestCase("IF type=='card' THEN card;", "type=cash", true)]
    [TestCase("min <= max;", "min=1,max=5", true)]
    [TestCase("min <= max;", "min=6,max=5", false)]
    [TestCase("IF NOT a THEN b AND c;", "b,c", true)]
    public void IDL_decides_whether_a_dependency_holds(string dependency, string given, bool holds)
        => Idl.Parse(dependency).Holds(Values(given)).Should().Be(holds);

    [TestCase("IF cursor THEN limit;")]
    [TestCase("Or(a, b);")]
    [TestCase("OnlyOne(a, b);")]
    [TestCase("AllOrNone(a, b);")]
    [TestCase("IF type=='card' THEN card;")]
    [TestCase("min <= max;")]
    public void IDL_can_both_satisfy_and_break_a_dependency(string dependency)
    {
        var idl = Idl.Parse(dependency);
        string? Sample(string name) => name switch { "type" => "card", "min" or "max" => "3", _ => "x" };

        idl.Satisfy(Values(""), Sample).Should().NotBeNull().And.Subject.Should().Match<Dictionary<string, string>>(v => idl.Holds(v));
        idl.Break(Values(""), Sample).Should().NotBeNull().And.Subject.Should().Match<Dictionary<string, string>>(v => !idl.Holds(v));
    }

    private static Dictionary<string, string> Values(string given)
        => given.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=')).ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "x");
}
