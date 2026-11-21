using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CsCheck;
using NUnit.Framework;

namespace Resultful.Tests
{
    // Decision 22 items 1 and 2 (24b): Failed on every result type, held to Match by laws; the zero value; the TError
    // conversion; and a handler written with early returns (a loop, a using, sync and async) that must agree with the
    // same handler written with Bind on random inputs. The compiler's view (nullable flow, CS0457) and RF0011 are in
    // Resultful.Analyzers.Tests' EarlyReturnTests.
    [TestFixture]
    public class EarlyReturnTests
    {
        static readonly Gen<Result<int, string>> Typed =
            Gen.Select(Gen.Bool, Gen.Int, Gen.String, (ok, v, e) => ok ? v.Ok<int, string>() : e.Fail<int, string>());

        // A reference-type value and a value-type error, the other way round.
        static readonly Gen<Result<string, int>> Flipped =
            Gen.Select(Gen.Bool, Gen.String, Gen.Int, (ok, v, e) => ok ? v.Ok<string, int>() : e.Fail<string, int>());

        static readonly Gen<Result<int>> Core =
            Gen.Select(Gen.Bool, Gen.Int, Gen.OneOf("a", "b", "c"), (ok, v, e) => ok ? v.Ok() : Error.From(e).Fail<int>());

        static readonly Gen<Validation<int, string>> Gathered =
            Gen.Select(Gen.Bool, Gen.Int, Gen.String.Array[1, 3], (ok, v, es) => ok ? Validation.Ok<int, string>(v) : Validation.Fail<int, string>(es));

        static readonly Gen<Validation<int>> Validated =
            Gen.Select(Gen.Bool, Gen.Int, Gen.OneOf("a", "b").Array[1, 3], (ok, v, es) => ok ? Validation.Ok(v) : Validation.Fail<int>(es.Select(Error.From)));

        // The law, for any result: Failed is IsFailure, its value is Match's value (default on a failure) and its error is
        // Match's error, the same instance (default on a success).
        static void Law<T, TError>(bool failed, bool isFailure, T value, TError error, (T Value, TError Error) matched)
        {
            Assert.That(failed, Is.EqualTo(isFailure));
            Assert.That(value, Is.EqualTo(matched.Value));
            if (matched.Error is null) Assert.That(error, Is.Null);
            else if (typeof(TError).IsValueType) Assert.That(error, Is.EqualTo(matched.Error));
            else Assert.That(error, Is.SameAs(matched.Error));
        }

        [Test]
        public void Failed_agrees_with_IsFailure_and_Match_on_every_result_type()
        {
            Typed.Sample(r => Law(r.Failed(out var v, out var e), r.IsFailure, v, e, r.Match(x => (x, (string?)null), x => (0, x))));
            Flipped.Sample(r => Law(r.Failed(out var v, out var e), r.IsFailure, v, e, r.Match(x => (x, 0), x => ((string?)null, x))));
            Core.Sample(r => Law(r.Failed(out var v, out var e), r.IsFailure, v, e, r.Match(x => (x, (Error?)null), x => (0, x))));
            Gathered.Sample(r => Law(r.Failed(out var v, out var e), r.IsFailure, v, e, r.Match(x => (x, (IReadOnlyList<string>?)null), x => (0, x))));
            Validated.Sample(r => Law(r.Failed(out var v, out var e), r.IsFailure, v, e, r.Match(x => (x, (IReadOnlyList<Error>?)null), x => (0, x))));
        }

        [Test]
        public void Failed_is_the_negation_of_TryGetValue_and_agrees_with_TryGetError()
        {
            Typed.Sample(r => r.Failed(out var v, out var e) == !r.TryGetValue(out var tv) && v == tv && r.TryGetError(out var te) == (e is not null) && e == te);
            Core.Sample(r => r.Failed(out var v, out var e) == !r.TryGetValue(out var tv) && v == tv && r.TryGetError(out var te) == (e is not null) && ReferenceEquals(e, te));
            Gathered.Sample(r => r.Failed(out _, out var e) == r.TryGetErrors(out var te) && ReferenceEquals(e, te));
            Validated.Sample(r => r.Failed(out _, out var e) == r.TryGetErrors(out var te) && ReferenceEquals(e, te));
        }

        static string Thrown(Action call)
        {
            try
            {
                call();
                return "no throw";
            }
            catch (InvalidOperationException e)
            {
                return e.Message;
            }
        }

        static readonly string[] ZeroMessages =
        [
            "This Result is the zero value (default): it is neither a success nor a failure. Create results with Result.Ok or Result.Fail.",
            "This Result is the zero value (default): it is neither a success nor a failure. Create results with Result.Ok or Result.Fail.",
            "This Validation is the zero value (default): it is neither a success nor a failure. Create results with Result.Ok or Result.Fail.",
            "This Validation is the zero value (default): it is neither a success nor a failure. Create results with Result.Ok or Result.Fail.",
        ];

        static readonly string[] Outcomes = ["EmptyOrder", "BadQuantity", "OutOfStock", "OverLimit", "A,C=9"];

        // Decision 10: the zero value throws exactly what TryGetValue throws.
        [Test]
        public void A_zero_value_throws_what_TryGetValue_throws()
        {
            var cases = new (string Failed, string TryGetValue)[]
            {
                (Thrown(() => default(Result<int, string>).Failed(out _, out _)), Thrown(() => default(Result<int, string>).TryGetValue(out _))),
                (Thrown(() => default(Result<int>).Failed(out _, out _)), Thrown(() => default(Result<int>).TryGetValue(out _))),
                (Thrown(() => default(Validation<int, string>).Failed(out _, out _)), Thrown(() => default(Validation<int, string>).TryGetValue(out _))),
                (Thrown(() => default(Validation<int>).Failed(out _, out _)), Thrown(() => default(Validation<int>).TryGetValue(out _))),
            };
            Assert.That(cases.Select(c => c.Failed), Is.EqualTo(cases.Select(c => c.TryGetValue)));
            Assert.That(cases.Select(c => c.Failed), Is.EqualTo(ZeroMessages));
        }

        // Decision 22 item 2: TError converts to a failure of Result<T, TError> as it does to Validation<T, TError>, and
        // both reject null.
        [Test]
        public void An_error_converts_to_a_failure_and_null_throws_as_for_Validation()
        {
            Gen.String.Sample(e =>
            {
                Result<int, string> result = e;
                Validation<int, string> validation = e;
                return result.Equals(e.Fail<int, string>()) && result.Error == e && validation.Errors.SequenceEqual([e]);
            });
            string missing = null!;
            Assert.That(() => { Result<int, string> r = missing; }, Throws.ArgumentNullException);
            Assert.That(() => { Validation<int, string> v = missing; }, Throws.ArgumentNullException);
        }

        // The snippet in the README's "Early return" section, compiled and run.
        sealed record Order(int Id, string Sku);
        sealed record NotFound() : Error("Order not found.");
        sealed record Receipt(int OrderId);

        static Result<Order, Error> FindOrder(int id) => id > 0 ? new Order(id, "A") : new NotFound();

        static Result<Receipt, Error> Ship(int id)
        {
            if (FindOrder(id).Failed(out var order, out var error))
                return error;

            return new Receipt(order.Id);
        }

        [Test]
        public void The_README_snippet_returns_the_error_or_the_value()
        {
            Assert.That(Ship(7).Value, Is.EqualTo(new Receipt(7)));
            Assert.That(Ship(0).Error, Is.InstanceOf<NotFound>());
        }

        // Each line of the README's snippet (its comments aside) is in this file, so the README can't drift from the code above.
        [Test]
        public void The_README_snippet_is_the_code_above()
        {
            var root = typeof(EarlyReturnTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == "RepoRootDir").Value!;
            var readme = File.ReadAllText(Path.Combine(root, "README.md")).Replace("\r\n", "\n", StringComparison.Ordinal);
            var section = readme.IndexOf("### Early return", StringComparison.Ordinal);
            Assert.That(section, Is.GreaterThan(0), "the README has an Early return section");
            var start = readme.IndexOf("```cs\n", section, StringComparison.Ordinal) + "```cs\n".Length;
            var snippet = readme[start..readme.IndexOf("```", start, StringComparison.Ordinal)];
            var here = File.ReadAllText(Path.Combine(root, "tests", "Resultful.Tests", "EarlyReturnTests.cs"));
            var lines = snippet.Split('\n')
                .Select(l => (l.Contains("//", StringComparison.Ordinal) ? l[..l.IndexOf("//", StringComparison.Ordinal)] : l).Trim())
                .Where(l => l.Length > 0)
                .ToList();
            Assert.That(lines, Has.Count.GreaterThan(3));
            Assert.That(lines.Where(l => !here.Contains(l, StringComparison.Ordinal)), Is.Empty);
        }

        // ---- Scenario: placing an order, written twice ----

        abstract record ShopError(string Message) : Error(Message);
        sealed record EmptyOrder() : ShopError("The order has no lines.");
        sealed record BadQuantity(int Line) : ShopError("A quantity must be positive.");
        sealed record OutOfStock(string Sku) : ShopError("Not enough stock.");
        sealed record OverLimit() : ShopError("The order is over the limit.");
        sealed record Placed(string Reserved, int Total);

        // Stock is reserved line by line, so a handler that runs a step it shouldn't leaves a different stock behind.
        sealed class Shop(Dictionary<string, int> stock, int limit)
        {
            static readonly Dictionary<string, int> Prices = new() { ["A"] = 2, ["B"] = 3, ["C"] = 5 };

            public Dictionary<string, int> Stock { get; } = new(stock);

            public int Open { get; private set; }

            sealed class Transaction(Shop shop) : IDisposable
            {
                public void Dispose() => shop.Open--;
            }

            Transaction Begin()
            {
                Open++;
                return new Transaction(this);
            }

            static Validation<IReadOnlyList<(string Sku, int Qty)>, ShopError> Check(IReadOnlyList<(string Sku, int Qty)> lines)
            {
                if (lines.Count == 0) return new EmptyOrder();
                var bad = lines.Select((l, i) => (l.Qty, i)).Where(l => l.Qty <= 0).Select(l => (ShopError)new BadQuantity(l.i)).ToList();
                return bad.Count == 0 ? Validation.Ok<IReadOnlyList<(string, int)>, ShopError>(lines) : Validation.Fail<IReadOnlyList<(string, int)>, ShopError>(bad);
            }

            Result<int, ShopError> Reserve((string Sku, int Qty) line)
            {
                if (!Stock.TryGetValue(line.Sku, out var left) || left < line.Qty) return new OutOfStock(line.Sku);
                Stock[line.Sku] = left - line.Qty;
                return Prices[line.Sku] * line.Qty;
            }

            async Task<Result<int, ShopError>> ReserveAsync((string Sku, int Qty) line)
            {
                await Task.Yield();
                return Reserve(line);
            }

            public Result<Placed, ShopError> PlaceEarly(IReadOnlyList<(string Sku, int Qty)> lines)
            {
                using var transaction = Begin();
                if (Check(lines).Failed(out var valid, out var errors))
                    return errors[0];

                var total = 0;
                var reserved = new List<string>();
                foreach (var line in valid)
                {
                    if (Reserve(line).Failed(out var cost, out var error))
                        return error;
                    total += cost;
                    reserved.Add(line.Sku);
                }

                if (total > limit) return new OverLimit();
                return new Placed(string.Join(",", reserved), total);
            }

            public async Task<Result<Placed, ShopError>> PlaceEarlyAsync(IReadOnlyList<(string Sku, int Qty)> lines)
            {
                using var transaction = Begin();
                if (Check(lines).Failed(out var valid, out var errors))
                    return errors[0];

                var total = 0;
                var reserved = new List<string>();
                foreach (var line in valid)
                {
                    if ((await ReserveAsync(line)).Failed(out var cost, out var error))
                        return error;
                    total += cost;
                    reserved.Add(line.Sku);
                }

                if (total > limit) return new OverLimit();
                return new Placed(string.Join(",", reserved), total);
            }

            static (int Total, string Reserved) Add((int Total, string Reserved) so, string sku, int cost)
                => (so.Total + cost, so.Reserved.Length == 0 ? sku : so.Reserved + "," + sku);

            Result<Placed, ShopError> Finish((int Total, string Reserved) so)
                => so.Total > limit ? Result.Fail<Placed, ShopError>(new OverLimit()) : Result.Ok<Placed, ShopError>(new Placed(so.Reserved, so.Total));

            public Result<Placed, ShopError> PlaceBind(IReadOnlyList<(string Sku, int Qty)> lines)
            {
                using var transaction = Begin();
                return Check(lines).ToResult(es => es[0])
                    .Bind(valid => valid.Aggregate(
                        Result.Ok<(int Total, string Reserved), ShopError>((0, "")),
                        (so, line) => so.Bind(s => Reserve(line).Map(cost => Add(s, line.Sku, cost)))))
                    .Bind(Finish);
            }

            public async Task<Result<Placed, ShopError>> PlaceBindAsync(IReadOnlyList<(string Sku, int Qty)> lines)
            {
                using var transaction = Begin();
                var checkedLines = Check(lines).ToResult(es => es[0]);
                var reserved = await checkedLines.BindAsync(valid => valid.Aggregate(
                    Task.FromResult(Result.Ok<(int Total, string Reserved), ShopError>((0, ""))),
                    async (so, line) => await (await so).BindAsync(s => ReserveAsync(line).Map(cost => Add(s, line.Sku, cost)))));
                return reserved.Bind(Finish);
            }
        }

        static readonly Gen<(string Sku, int Qty)[]> Lines = Gen.Select(Gen.OneOf("A", "B", "C", "Z"), Gen.Int[-1, 4]).Array[0, 5];
        static readonly Gen<Dictionary<string, int>> Stocks = Gen.Int[0, 6].Array[3].Select(s => new Dictionary<string, int> { ["A"] = s[0], ["B"] = s[1], ["C"] = s[2] });

        [Test]
        public void A_handler_with_early_returns_agrees_with_the_same_handler_written_with_Bind()
        {
            Gen.Select(Lines, Stocks, Gen.Int[0, 40]).Sample((lines, stock, limit) =>
            {
                Shop early = new(stock, limit), bound = new(stock, limit);
                var a = early.PlaceEarly(lines);
                var b = bound.PlaceBind(lines);
                Assert.That(a, Is.EqualTo(b));
                Assert.That(early.Stock, Is.EqualTo(bound.Stock), "the same steps ran");
                Assert.That((early.Open, bound.Open), Is.EqualTo((0, 0)), "the using ran on every exit");
            });
        }

        [Test]
        public async Task The_async_handlers_agree_with_each_other_and_with_the_sync_one()
        {
            await Gen.Select(Lines, Stocks, Gen.Int[0, 40]).SampleAsync(async t =>
            {
                var (lines, stock, limit) = t;
                Shop early = new(stock, limit), bound = new(stock, limit), sync = new(stock, limit);
                var a = await early.PlaceEarlyAsync(lines);
                var b = await bound.PlaceBindAsync(lines);
                Assert.That(a, Is.EqualTo(b));
                Assert.That(a, Is.EqualTo(sync.PlaceEarly(lines)));
                Assert.That(early.Stock, Is.EqualTo(bound.Stock));
                Assert.That((early.Open, bound.Open), Is.EqualTo((0, 0)));
            });
        }

        // Every case comes up, so the agreement above isn't only about one path.
        [Test]
        public void The_scenario_reaches_every_outcome()
        {
            var stock = new Dictionary<string, int> { ["A"] = 2, ["B"] = 0, ["C"] = 9 };
            string Outcome((string, int)[] lines, int limit)
                => new Shop(stock, limit).PlaceEarly(lines).Match(p => $"{p.Reserved}={p.Total}", e => e.GetType().Name);
            Assert.That(
                new[] { Outcome([], 9), Outcome([("A", 0)], 9), Outcome([("A", 1), ("B", 1)], 9), Outcome([("C", 3)], 9), Outcome([("A", 2), ("C", 1)], 9) },
                Is.EqualTo(Outcomes));
        }

#if NET11_0_OR_GREATER
        // On net11.0 an error union is a TError like any other: Failed gives the union, which holds its case.
        public sealed record Missing(int Id) : Error("Missing.");
        public sealed record Locked(int Id) : Error("Locked.");
        public union LookupError(Missing, Locked);

        [Test]
        public void An_error_union_behaves_as_the_other_named_members_do()
        {
            var unions = Gen.Select(Gen.Int[0, 2], Gen.Int, (c, v) => c switch
            {
                0 => v.Ok<int, LookupError>(),
                1 => new LookupError(new Missing(v)).Fail<int, LookupError>(),
                _ => new LookupError(new Locked(v)).Fail<int, LookupError>(),
            });
            unions.Sample(r =>
            {
                var failed = r.Failed(out var v, out var e);
                Assert.That(failed, Is.EqualTo(r.IsFailure));
                Assert.That(v, Is.EqualTo(r.Match(x => x, _ => 0)));
                Assert.That(e.Value, Is.EqualTo(r.Match(_ => (object?)null, x => x.Value)));
                Assert.That(failed, Is.EqualTo(r.TryGetError(out var te)));
                Assert.That(e.Value, Is.EqualTo(te.Value));
            });

            static Result<int, LookupError> Next(Result<int, LookupError> r)
            {
                if (r.Failed(out var v, out var e)) return e;
                return v + 1;
            }

            Assert.That(Next(1.Ok<int, LookupError>()).Value, Is.EqualTo(2));
            Assert.That(Next(new LookupError(new Locked(3)).Fail<int, LookupError>()).Error.Value, Is.EqualTo(new Locked(3)));
            Assert.That(Thrown(() => default(Result<int, LookupError>).Failed(out _, out _)), Is.EqualTo(Thrown(() => default(Result<int, LookupError>).TryGetValue(out _))));
        }
#endif
    }
}
