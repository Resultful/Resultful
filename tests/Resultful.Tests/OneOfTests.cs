using System;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
using NUnit.Framework;
using OneOf;
using OneOfNone = OneOf.Types.None;

namespace Resultful.Tests
{
    // Decision 1 (11): the core references nothing; Resultful.OneOf carries ToOneOf (and its Task forms), the way back
    // (ToResult, ToOption) and the None conversions; a step with no value comes back as a Result<Unit> (decision 14). The scenario is a checkout that calls an older service
    // still written against OneOf: its answers come in through the interop, run through a Result pipeline, and leave as a
    // OneOf for a caller that hasn't moved. The laws hold every conversion to a round trip in both directions.
    [TestFixture]
    public class OneOfTests
    {
        // ---- The domain ----

        sealed record OutOfStock(string Sku) : Error($"{Sku} is out of stock");
        sealed record CardDeclined(string Last4) : Error($"card {Last4} was declined");
        sealed record Item(string Sku, decimal Price, int InStock);
        sealed record Receipt(string Sku, int Quantity, decimal Total);

        // The legacy service, written against OneOf before 0.3.
        static class LegacyCatalog
        {
            static readonly Item[] Items = [new("apple", 0.5m, 10), new("pear", 0.75m, 0)];

            public static OneOf<Item, OneOfNone> Find(string sku)
                => Items.FirstOrDefault(i => i.Sku == sku) is { } item ? item : new OneOfNone();

            public static Task<OneOf<Unit, Error>> Charge(string card, decimal amount)
                => Task.FromResult<OneOf<Unit, Error>>(card.EndsWith("0000", StringComparison.Ordinal) ? new CardDeclined("0000") : Unit.Value);
        }

        // The new code: Option and Result inside, OneOf only at the edges.
        static async Task<Result<Receipt>> Checkout(string sku, int quantity, string card)
        {
            var found = LegacyCatalog.Find(sku).ToOption();
            var item = found.Match(i => i.Ok(), _ => Result.Fail<Item>($"no item {sku}"));
            var reserved = item.Bind(i => i.InStock >= quantity ? i.Ok() : new OutOfStock(i.Sku).Fail<Item>());
            if (!reserved.TryGetValue(out var ok)) return reserved.Error.Fail<Receipt>();
            var total = ok.Price * quantity;
            Result<Unit> charged = await LegacyCatalog.Charge(card, total).ToResult();
            return charged.Match(_ => new Receipt(sku, quantity, total).Ok(), e => e.Fail<Receipt>());
        }

        [Test]
        public async Task A_checkout_moves_through_the_interop_both_ways()
        {
            var bought = await Checkout("apple", 4, "4242").ToOneOf();
            Assert.That(bought.IsT0 ? bought.AsT0 : null, Is.EqualTo(new Receipt("apple", 4, 2m)));

            var missing = await Checkout("plum", 1, "4242").ToOneOf();
            Assert.That(missing.IsT1 ? missing.AsT1.Message : null, Is.EqualTo("no item plum"));

            var empty = await Checkout("pear", 1, "4242").ToOneOf();
            Assert.That(empty.IsT1 ? empty.AsT1 : null, Is.EqualTo(new OutOfStock("pear")));

            var declined = await Checkout("apple", 1, "0000").ToOneOf();
            Assert.That(declined.Match(r => r.Sku, e => e.Message), Is.EqualTo("card 0000 was declined"));
        }

        [Test]
        public void The_way_back_picks_the_most_specific_overload()
        {
            // OneOf<T, Error> becomes Result<T>, not Result<T, Error>; OneOf<T, None> becomes Option<T>, not core's
            // ToOption<T>(this T), which would give Option<OneOf<T, None>>.
            Result<int> list = OneOf<int, Error>.FromT0(1).ToResult();
            Result<int, string> typed = OneOf<int, string>.FromT1("e").ToResult();
            Result<Unit> voidList = OneOf<Unit, Error>.FromT0(Unit.Value).ToResult();
            Result<Unit, string> voidTyped = OneOf<Unit, string>.FromT1("e").ToResult();
            Option<int> option = OneOf<int, OneOfNone>.FromT0(1).ToOption();
            // The Task forms too: core's ToOption<T>(this Task<T>) would give Task<Option<OneOf<T, None>>>.
            Task<Option<int>> later = Task.FromResult(OneOf<int, OneOfNone>.FromT1(new OneOfNone())).ToOption();
            Task<Result<int>> laterList = Task.FromResult(OneOf<int, Error>.FromT0(1)).ToResult();
            Task<Result<Unit>> laterVoid = Task.FromResult(OneOf<Unit, Error>.FromT0(Unit.Value)).ToResult();
            Assert.That((later.Result.Or(0), laterList.Result.Value, laterVoid.Result.Match(_ => "ok", e => e.Message)), Is.EqualTo((0, 1, "ok")));
            Assert.That((list.Value, typed.Error, voidList.Match(_ => "ok", e => e.Message), voidTyped.Match(_ => "ok", e => e), option.Or(0)), Is.EqualTo((1, "e", "ok", "e", 1)));
        }

        [Test]
        public void The_zero_value_throws_and_an_empty_option_is_None()
        {
            Assert.That(() => default(Result<int, string>).ToOneOf(), Throws.InvalidOperationException.With.Message.Contains("zero value (default)"));
            Assert.That(() => default(Result<int>).ToOneOf(), Throws.InvalidOperationException.With.Message.Contains("zero value (default)"));
            Assert.That(() => default(Result<Unit, string>).ToOneOf(), Throws.InvalidOperationException.With.Message.Contains("zero value (default)"));
            Assert.That(() => default(Result<Unit>).ToOneOf(), Throws.InvalidOperationException.With.Message.Contains("zero value (default)"));
#pragma warning disable CS0618 // VoidResult is obsolete (decision 14); its ToOneOf still behaves as in 0.2.
            Assert.That(() => default(VoidResult<string>).ToOneOf(), Throws.InvalidOperationException.With.Message.Contains("zero value (default)"));
            Assert.That(() => default(VoidResult).ToOneOf(), Throws.InvalidOperationException.With.Message.Contains("zero value (default)"));
#pragma warning restore CS0618
            Assert.That(() => Task.FromResult(default(Result<int>)).ToOneOf(), Throws.InvalidOperationException);
            Assert.That(default(Option<int>).ToOneOf().IsT1, Is.True);
            Assert.That(() => ((Task<Result<int>>)null!).ToOneOf(), Throws.ArgumentNullException);
            Assert.That(() => OneOf<int, Error>.FromT1(null!).ToResult(), Throws.ArgumentNullException);
        }

        [Test]
        public void The_core_references_nothing()
        {
            Assert.That(typeof(Result).Assembly.GetReferencedAssemblies().Select(a => a.Name), Has.None.EqualTo("OneOf"));
            Assert.That(typeof(Option<int>).GetMethod("Match")!.GetParameters()[1].ParameterType.GetGenericArguments()[0], Is.EqualTo(typeof(None)));
            Assert.That(default(None).ToOneOf().ToResultful(), Is.EqualTo(default(None)));
            Assert.That(new OneOfNone().ToResultful().ToOneOf(), Is.EqualTo(new OneOfNone()));
            Assert.That((default(None) == new None(), default(None).GetHashCode(), default(None).ToString()), Is.EqualTo((true, 0, "None")));
        }

        // ---- Laws: a round trip is the identity, both ways ----

        static readonly Gen<int?> Values = Gen.Int[-3, 3].Select(v => v == 3 ? (int?)null : v);
        static readonly Gen<string> Messages = Gen.OneOf(Gen.Const("a"), Gen.Const("b"), Gen.Const("İ"));
        static readonly Gen<Error> Errors = Gen.OneOf(
            Messages.Select(m => Error.From(m)),
            Messages.Select(m => (Error)new OutOfStock(m)),
            Messages.Select(m => Error.From(m).WithContext("ctx")));

        [Test]
        public void Result_with_a_typed_error_round_trips()
        {
            Gen.Select(Gen.Bool, Values, Messages).Sample((ok, v, e) =>
            {
                var r = ok ? v.Ok<int?, string>() : e.Fail<int?, string>();
                var u = ok ? OneOf<int?, string>.FromT0(v) : OneOf<int?, string>.FromT1(e);
                Assert.That(r.ToOneOf(), Is.EqualTo(u));
                Assert.That(r.ToOneOf().ToResult(), Is.EqualTo(r));
                Assert.That(u.ToResult().ToOneOf(), Is.EqualTo(u));
                Assert.That(Task.FromResult(r).ToOneOf().Result, Is.EqualTo(u));
                Assert.That(Task.FromResult(u).ToResult().Result, Is.EqualTo(r));
            });
        }

        [Test]
        public void Result_with_an_Error_round_trips()
        {
            Gen.Select(Gen.Bool, Values, Errors).Sample((ok, v, e) =>
            {
                var r = ok ? v.Ok() : e.Fail<int?>();
                var u = ok ? OneOf<int?, Error>.FromT0(v) : OneOf<int?, Error>.FromT1(e);
                Assert.That(r.ToOneOf(), Is.EqualTo(u));
                Assert.That(r.ToOneOf().ToResult(), Is.EqualTo(r));
                Assert.That(u.ToResult().ToOneOf(), Is.EqualTo(u));
                Assert.That(Task.FromResult(r).ToOneOf().Result, Is.EqualTo(u));
                Assert.That(Task.FromResult(u).ToResult().Result, Is.EqualTo(r));
            });
        }

        [Test]
        public void Unit_results_round_trip()
        {
            Gen.Select(Gen.Bool, Messages, Errors).Sample((ok, m, e) =>
            {
                var typed = ok ? Result.Ok<string>() : m.Fail();
                var typedUnion = ok ? OneOf<Unit, string>.FromT0(Unit.Value) : OneOf<Unit, string>.FromT1(m);
                Assert.That(typed.ToOneOf(), Is.EqualTo(typedUnion));
                Assert.That(typed.ToOneOf().ToResult(), Is.EqualTo(typed));
                Assert.That(Task.FromResult(typedUnion).ToResult().Result, Is.EqualTo(typed));

                var list = ok ? Result.Ok() : Result.Fail(e);
                var listUnion = ok ? OneOf<Unit, Error>.FromT0(Unit.Value) : OneOf<Unit, Error>.FromT1(e);
                Assert.That(list.ToOneOf(), Is.EqualTo(listUnion));
                Assert.That(list.ToOneOf().ToResult(), Is.EqualTo(list));
                Assert.That(Task.FromResult(listUnion).ToResult().Result, Is.EqualTo(list));
            });
        }

#pragma warning disable CS0618 // VoidResult is obsolete (decision 14); its OneOf conversions still behave as in 0.2.
        [Test]
        public void VoidResults_round_trip()
        {
            Gen.Select(Gen.Bool, Messages, Errors).Sample((ok, m, e) =>
            {
                VoidResult<string> typed = ok ? Result.Ok<string>() : m.Fail();
                var typedUnion = ok ? OneOf<Unit, string>.FromT0(Unit.Value) : OneOf<Unit, string>.FromT1(m);
                Assert.That(typed.ToOneOf(), Is.EqualTo(typedUnion));
                Assert.That(typed.ToOneOf().ToVoidResult(), Is.EqualTo(typed));
                Assert.That(typedUnion.ToVoidResult().ToOneOf(), Is.EqualTo(typedUnion));
                Assert.That(Task.FromResult(typed).ToOneOf().Result, Is.EqualTo(typedUnion));
                Assert.That(Task.FromResult(typedUnion).ToVoidResult().Result, Is.EqualTo(typed));

                VoidResult list = ok ? Result.Ok() : Result.Fail(e);
                var listUnion = ok ? OneOf<Unit, Error>.FromT0(Unit.Value) : OneOf<Unit, Error>.FromT1(e);
                Assert.That(list.ToOneOf(), Is.EqualTo(listUnion));
                Assert.That(list.ToOneOf().ToVoidResult(), Is.EqualTo(list));
                Assert.That(listUnion.ToVoidResult().ToOneOf(), Is.EqualTo(listUnion));
                Assert.That(Task.FromResult(list).ToOneOf().Result, Is.EqualTo(listUnion));
                Assert.That(Task.FromResult(listUnion).ToVoidResult().Result, Is.EqualTo(list));
            });
        }
#pragma warning restore CS0618

        [Test]
        public void Option_round_trips()
        {
            Gen.Select(Gen.Int[0, 2], Values).Sample((state, v) =>
            {
                // State 0 is the zero value, which is None (decision 10).
                var o = state switch { 0 => default, 1 => v.Some(), _ => Option<int?>.None };
                var u = state == 1 ? OneOf<int?, OneOfNone>.FromT0(v) : OneOf<int?, OneOfNone>.FromT1(new OneOfNone());
                Assert.That(o.ToOneOf(), Is.EqualTo(u));
                Assert.That(o.ToOneOf().ToOption(), Is.EqualTo(o));
                Assert.That(u.ToOption().ToOneOf(), Is.EqualTo(u));
                Assert.That(Task.FromResult(o).ToOneOf().Result, Is.EqualTo(u));
                Assert.That(Task.FromResult(u).ToOption().Result, Is.EqualTo(o));
            });
        }
    }
}
