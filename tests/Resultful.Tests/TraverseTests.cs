using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
using NUnit.Framework;
using Resultful.LINQ;

namespace Resultful.Tests
{
    // Decision 22 item 4. Traverse and Collect equal folding Bind over the list and stop at the first failure in input
    // order; TraverseAll and CollectAll equal Validation.Combine, every error in input order; the params-span Combine
    // equals the arity overloads. Sync only: async traversal is over IAsyncEnumerable (24d).
    [TestFixture]
    public class TraverseTests
    {
        sealed record Odd(int Value) : Error("odd value");
        sealed record Negative(int Value) : Error("negative value");

        static readonly Gen<List<int>> Lists = Gen.Int[-6, 20].List[0, 8];

        static Result<int> Half(int x) => x % 2 == 0 ? x / 2 : new Odd(x);
        static Result<int, string> HalfTyped(int x) => x % 2 == 0 ? x / 2 : $"odd {x}";
        static Validation<int> Check(int x) => Validation.Combine(
            x % 2 == 0 ? Validation.Ok(x) : new Odd(x),
            x >= 0 ? Validation.Ok(x) : new Negative(x),
            (a, _) => a / 2);
        static Validation<int, string> CheckTyped(int x) => Validation.Combine(
            x % 2 == 0 ? Validation.Ok<int, string>(x) : $"odd {x}",
            x >= 0 ? Validation.Ok<int, string>(x) : $"negative {x}",
            (a, _) => a / 2);

        // Results of lists compare by their items.
        static string Show<TError>(Result<IReadOnlyList<int>, TError> r) => r.Match(v => "ok " + string.Join(",", v), e => "fail " + e);
        static string Show(Result<IReadOnlyList<int>> r) => r.Match(v => "ok " + string.Join(",", v), e => "fail " + e);
        static string Show(Result<IEnumerable<int>> r) => r.Match(v => "ok " + string.Join(",", v), e => "fail " + e);
        static string Show<TError>(Result<IEnumerable<int>, TError> r) => r.Match(v => "ok " + string.Join(",", v), e => "fail " + e);
        static string Show<TError>(Validation<IReadOnlyList<int>, TError> v) => v.Match(x => "ok " + string.Join(",", x), es => "fail " + string.Join("; ", es));
        static string Show(Validation<IReadOnlyList<int>> v) => v.Match(x => "ok " + string.Join(",", x), es => "fail " + string.Join("; ", es));

        // ---- Laws ----

        [Test]
        public void Traverse_equals_folding_Bind_over_the_list()
        {
            Lists.Sample(xs =>
            {
                var fold = xs.Aggregate(Result.Ok<IEnumerable<int>>([]), (acc, x) => acc.Bind(list => Half(x).Map(v => list.Append(v))));
                var typedFold = xs.Aggregate(Result.Ok<IEnumerable<int>, string>([]), (acc, x) => acc.Bind(list => HalfTyped(x).Map(v => list.Append(v))));

                Assert.That(Show(xs.Traverse(Half)), Is.EqualTo(Show(fold)));
                Assert.That(Show(xs.Traverse(HalfTyped)), Is.EqualTo(Show(typedFold)));
                Assert.That(Show(xs.Select(Half).Collect()), Is.EqualTo(Show(fold)));
                Assert.That(Show(xs.Select(HalfTyped).Collect()), Is.EqualTo(Show(typedFold)));
            });
        }

        [Test]
        public void TraverseAll_equals_Validation_Combine_with_every_error_in_input_order()
        {
            Lists.Sample(xs =>
            {
                var checks = xs.Select(Check).ToArray();
                var typedChecks = xs.Select(CheckTyped).ToArray();
                var errors = xs.SelectMany(x => Check(x).Match(_ => [], es => es)).ToList();
                var model = errors.Count == 0 ? $"ok {string.Join(",", xs.Select(x => x / 2))}" : "fail " + string.Join("; ", errors);

                Assert.That(Show(xs.TraverseAll(Check)), Is.EqualTo(model));
                Assert.That(Show(Validation.Combine(checks)), Is.EqualTo(model));
                Assert.That(Show(xs.TraverseAll(CheckTyped)), Is.EqualTo(Show(Validation.Combine(typedChecks))));

                // A step that gives one Result per item gathers one error per failed item.
                var halves = xs.Select(Half).ToArray();
                var halfModel = halves.All(h => h.IsSuccess)
                    ? $"ok {string.Join(",", halves.Select(h => h.Value))}"
                    : "fail " + string.Join("; ", halves.Where(h => h.IsFailure).Select(h => h.Error));
                Assert.That(Show(xs.TraverseAll(Half)), Is.EqualTo(halfModel));
                Assert.That(Show(halves.CollectAll()), Is.EqualTo(halfModel));
                Assert.That(Show(xs.TraverseAll(HalfTyped)), Is.EqualTo(Show(xs.Select(HalfTyped).CollectAll())));
                Assert.That(xs.TraverseAll(HalfTyped).IsSuccess, Is.EqualTo(xs.All(x => x % 2 == 0)));
            });
        }

        [Test]
        public void The_span_Combine_equals_the_arity_overloads()
        {
            var checks = Gen.Int[-3, 6].Select(Check);
            Gen.Select(checks, checks, checks).Sample((a, b, c) =>
            {
                var arity = Validation.Combine(a, b, c, (x, y, z) => (IReadOnlyList<int>)[x, y, z]);
                Assert.That(Show(Validation.Combine(a, b, c)), Is.EqualTo(Show(arity)));
                Assert.That(Show(Validation.Combine([a, b])), Is.EqualTo(Show(Validation.Combine(a, b).Map(t => (IReadOnlyList<int>)[t.Item1, t.Item2]))));
            });
            var typed = Gen.Int[-3, 6].Select(CheckTyped);
            Gen.Select(typed, typed, typed).Sample((a, b, c) =>
            {
                var arity = Validation.Combine(a, b, c, (x, y, z) => (IReadOnlyList<int>)[x, y, z]);
                Assert.That(Show(Validation.Combine(a, b, c)), Is.EqualTo(Show(arity)));
            });
        }

        [Test]
        public void Two_validations_still_combine_into_a_tuple_and_none_into_an_empty_list()
        {
            Validation<(int, int)> pair = Validation.Combine(Validation.Ok(1), Validation.Ok(2));
            Validation<(int, int), string> typedPair = Validation.Combine(Validation.Ok<int, string>(1), Validation.Ok<int, string>(2));
            Validation<IReadOnlyList<int>> list = Validation.Combine([Validation.Ok(1), Validation.Ok(2)]);

            Assert.That(pair.Value, Is.EqualTo((1, 2)));
            Assert.That(typedPair.Value, Is.EqualTo((1, 2)));
            Assert.That(list.Value, Is.EqualTo(new List<int> { 1, 2 }));
            Assert.That(Validation.Combine<int>().Value, Is.Empty);
            Assert.That(Validation.Combine<int, string>().Value, Is.Empty);
            Assert.That(() => Validation.Combine(Validation.Ok(1), default, Validation.Ok(3)), Throws.InvalidOperationException);
            Assert.That(() => Validation.Combine(Validation.Ok<int, string>(1), default, Validation.Ok<int, string>(3)), Throws.InvalidOperationException);
        }

        // ---- First failure: nothing after it ----

        sealed class Counted(IEnumerable<int> items) : IEnumerable<int>
        {
            public int Read;

            public IEnumerator<int> GetEnumerator()
            {
                foreach (var item in items)
                {
                    Read++;
                    yield return item;
                }
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        [Test]
        public void Traverse_reads_no_item_and_calls_nothing_after_the_first_failure()
        {
            var calls = 0;
            var items = new Counted([2, 4, 5, 6, 7]);
            Result<int> F(int x) { calls++; return Half(x); }

            Assert.That(items.Traverse(F).Error, Is.EqualTo(new Odd(5)));
            Assert.That((calls, items.Read), Is.EqualTo((3, 3)));

            var typedItems = new Counted([1, 2]);
            Assert.That(typedItems.Traverse(HalfTyped).Error, Is.EqualTo("odd 1"));
            Assert.That(typedItems.Read, Is.EqualTo(1));

            var results = new[] { 1.Ok(), new Odd(3).Fail<int>(), default };
            Assert.That(results.Collect().Error, Is.EqualTo(new Odd(3)));
            Assert.That(new[] { 2.Ok(), new Odd(7).Fail<int>(), new Odd(1).Fail<int>() }.Collect().Error, Is.EqualTo(new Odd(7)), "the first in input order");
        }

        [Test]
        public void TraverseAll_calls_every_item_even_after_failures()
        {
            var calls = 0;
            Result<int> F(int x) { calls++; return Half(x); }

            var all = Enumerable.Range(1, 3).TraverseAll(F);

            Assert.That(calls, Is.EqualTo(3));
            Assert.That(all.Errors, Is.EqualTo(new Error[] { new Odd(1), new Odd(3) }));
        }

        [Test]
        public void Zero_values_and_null_arguments_throw()
        {
            int[] one = [1];
            Assert.That(() => one.Traverse(_ => default(Result<int>)), Throws.InvalidOperationException);
            Assert.That(() => one.Traverse(_ => default(Result<int, string>)), Throws.InvalidOperationException);
            Assert.That(() => one.TraverseAll(_ => default(Result<int>)), Throws.InvalidOperationException);
            Assert.That(() => one.TraverseAll(_ => default(Validation<int, string>)), Throws.InvalidOperationException);
            Assert.That(() => new[] { default(Result<int>) }.CollectAll(), Throws.InvalidOperationException);
            Assert.That(() => new[] { default(Result<int, string>) }.Collect(), Throws.InvalidOperationException);

            var calls = new (string Param, TestDelegate Call)[]
            {
                ("source", () => ((IEnumerable<int>)null!).Traverse(Half)),
                ("func", () => one.Traverse((Func<int, Result<int>>)null!)),
                ("source", () => ((IEnumerable<int>)null!).Traverse(HalfTyped)),
                ("func", () => one.Traverse((Func<int, Result<int, string>>)null!)),
                ("results", () => ((IEnumerable<Result<int>>)null!).Collect()),
                ("results", () => ((IEnumerable<Result<int, string>>)null!).Collect()),
                ("source", () => ((IEnumerable<int>)null!).TraverseAll(Half)),
                ("func", () => one.TraverseAll((Func<int, Result<int>>)null!)),
                ("source", () => ((IEnumerable<int>)null!).TraverseAll(Check)),
                ("func", () => one.TraverseAll((Func<int, Validation<int>>)null!)),
                ("source", () => ((IEnumerable<int>)null!).TraverseAll(HalfTyped)),
                ("func", () => one.TraverseAll((Func<int, Result<int, string>>)null!)),
                ("source", () => ((IEnumerable<int>)null!).TraverseAll(CheckTyped)),
                ("func", () => one.TraverseAll((Func<int, Validation<int, string>>)null!)),
                ("results", () => ((IEnumerable<Result<int>>)null!).CollectAll()),
                ("results", () => ((IEnumerable<Result<int, string>>)null!).CollectAll()),
            };
            Assert.That(calls.Select(c => Assert.Throws<ArgumentNullException>(c.Call)!.ParamName), Is.EqualTo(calls.Select(c => c.Param)));

        }

        // ---- Scenario: an import of order lines ----

        sealed record LineDto(string Sku, int Quantity);
        sealed record Line(string Sku, int Quantity, decimal Price);
        sealed record UnknownSku(string Sku) : Error("unknown product");
        sealed record BadQuantity(string Sku) : Error("quantity must be positive");

        static readonly Dictionary<string, decimal> Catalogue = new() { ["apple"] = 0.5m, ["pear"] = 0.75m, ["plum"] = 0.2m };

        // Every line is checked and every problem reported; then prices are looked up, stopping at the first unknown
        // product, and the order is saved through an async step in a query.
        static Task<Result<decimal>> Import(IEnumerable<LineDto> dtos, List<string> saved) =>
            from lines in dtos.TraverseAll(d => d.Quantity > 0 ? Validation.Ok(d) : new BadQuantity(d.Sku)).ToResult()
            from priced in lines.Traverse(Price)
            from id in SaveAsync(priced, saved)
            select priced.Sum(l => l.Price * l.Quantity);

        static Result<Line> Price(LineDto dto) =>
            Catalogue.TryGetValue(dto.Sku, out var price) ? new Line(dto.Sku, dto.Quantity, price) : new UnknownSku(dto.Sku);

        static async Task<Result<int>> SaveAsync(IReadOnlyList<Line> lines, List<string> saved)
        {
            await Task.Yield();
            saved.Add(string.Join(",", lines.Select(l => l.Sku)));
            return saved.Count;
        }

        [Test]
        public async Task An_import_checks_every_line_prices_them_and_saves_once()
        {
            var saved = new List<string>();
            var total = await Import([new("apple", 4), new("pear", 2), new("plum", 5)], saved);
            var invalid = await Import([new("apple", 0), new("kiwi", 1), new("pear", -1)], saved);
            var unknown = await Import([new("apple", 1), new("kiwi", 1), new("fig", 1)], saved);

            Assert.That(total.Value, Is.EqualTo(4.5m));
            Assert.That(invalid.Error.TryFind<ValidationFailed<Error>>(out var failed), Is.True);
            Assert.That(failed!.Errors, Is.EqualTo(new Error[] { new BadQuantity("apple"), new BadQuantity("pear") }));
            Assert.That(unknown.Error, Is.EqualTo(new UnknownSku("kiwi")));
            Assert.That(unknown.Error.ToString(), Does.Not.Contain("kiwi"), "the message names no product");
            Assert.That(saved, Is.EqualTo(new List<string> { "apple,pear,plum" }));
        }
    }
}
