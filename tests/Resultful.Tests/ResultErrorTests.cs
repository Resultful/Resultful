using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
using NUnit.Framework;
using Resultful.LINQ;

namespace Resultful.Tests
{
    // Decision 14 (09): Result<T> holds one Error and stops at the first failure. The scenario is an order pipeline:
    // validation gathers its failures into one ValidationFailed, domain steps fail with their own records, a data-layer
    // timeout is wrapped as the Cause of a SaveFailed, the service adds context, and the edge matches on types. The laws
    // hold Map, Bind, MapError and WithContext on Result<T> to their definitions, with random Error leaves.
    [TestFixture]
    public class ResultErrorTests
    {
        // ---- The domain ----

        sealed record Required(string Field) : Error($"{Field} is required");
        sealed record TooMany(string Field, int Max) : Error($"{Field} is more than {Max}");
        sealed record UnknownSku(string Sku) : Error($"no product has SKU {Sku}");
        sealed record OutOfStock(string Sku, int Wanted, int Left) : Error($"{Wanted} of {Sku} wanted, {Left} left");
        sealed record PaymentDeclined(string Reason) : Error($"the payment was declined: {Reason}");
        sealed record DbTimeout(TimeSpan After) : Error($"the database timed out after {After.TotalSeconds}s");
        sealed record SaveFailed(int OrderId) : Error($"order {OrderId} was not saved");

        sealed record OrderRequest(string Customer, string Sku, int Quantity, string Card);
        sealed record ValidOrder(string Customer, string Sku, int Quantity, string Card);
        sealed record Reservation(string Sku, int Quantity, decimal Price);
        sealed record Receipt(string Card, decimal Amount);
        sealed record Order(int Id, string Customer, Reservation Reservation, Receipt Receipt);

        sealed class Shop
        {
            public Dictionary<string, (int Left, decimal Price)> Stock { get; } = new()
            {
                ["BOOK"] = (5, 12.50m),
                ["PEN"] = (0, 1.20m),
            };

            public bool DatabaseIsSlow { get; set; }
            public List<string> Log { get; } = [];
            public List<Order> Saved { get; } = [];

            // Independent checks that should all run: Validation gathers every failure (10), and ToResult carries them on
            // as one ValidationFailed<Error>.
            static Validation<string> Present(string field, string value) =>
                string.IsNullOrWhiteSpace(value) ? new Required(field) : value;

            static Validation<int> Quantity(int quantity) =>
                quantity <= 0 ? new Required("quantity") : quantity > 10 ? new TooMany("quantity", 10) : quantity;

            public static Result<ValidOrder> Validate(OrderRequest r) =>
                Validation.Combine(Present("customer", r.Customer), Present("sku", r.Sku), Quantity(r.Quantity), Present("card", r.Card),
                    (customer, sku, quantity, card) => new ValidOrder(customer, sku, quantity, card))
                .ToResult();

            // A step that converts both ways: `return reservation;` and `return new OutOfStock(…);`.
            public Result<Reservation> Reserve(string sku, int quantity)
            {
                if (!Stock.TryGetValue(sku, out var item)) return new UnknownSku(sku);
                if (item.Left < quantity) return new OutOfStock(sku, quantity, item.Left);
                Stock[sku] = (item.Left - quantity, item.Price);
                return new Reservation(sku, quantity, item.Price * quantity);
            }

            // A typed step: its signature names its one failure. AsResult widens it into the general chain.
            public static Result<Receipt, PaymentDeclined> Charge(string card, decimal amount)
                => card.StartsWith('4')
                    ? new Receipt(card, amount)
                    : new PaymentDeclined("card not accepted");

            // The data layer fails with its own error; the step wraps it, so the timeout isn't lost.
            Result<Order> Insert(Order order)
            {
                if (DatabaseIsSlow) return new DbTimeout(TimeSpan.FromSeconds(3));
                Saved.Add(order);
                return order;
            }

            public Result<Order> Save(Order o) =>
                Insert(o).MapError(e => new SaveFailed(o.Id) { Cause = e });

            public Result<Order> Place(int id, OrderRequest request) =>
                (from valid in Validate(request)
                 from reservation in Reserve(valid.Sku, valid.Quantity)
                 from receipt in Charge(valid.Card, reservation.Price).AsResult()
                 select new Order(id, valid.Customer, reservation, receipt))
                .Bind(Save)
                .TeeError(e => Log.Add(e.ToString()))
                .WithContext($"placing order {id}");

            public Task<Result<Order>> PlaceAsync(int id, OrderRequest request) =>
                Task.FromResult(Validate(request))
                    .BindAsync(async valid => { await Task.Yield(); return Reserve(valid.Sku, valid.Quantity).Map(r => (valid, r)); })
                    .Bind(x => Charge(x.valid.Card, x.r.Price).AsResult().Map(receipt => new Order(id, x.valid.Customer, x.r, receipt)))
                    .Bind(Save)
                    .MapError(e => e.WithContext($"placing order {id}"));
        }

        // The edge: an HTTP-like status and body from the error's type, never from its message text.
        static (int Status, string Body) Respond(Result<Order> result)
        {
            if (!result.TryGetError(out var error)) return (201, $"order {result.Value.Id}");
            if (error.TryFind<DbTimeout>(out var timeout)) return (503, $"retry after {timeout.After.TotalSeconds * 2}s");
            return error switch
            {
                ValidationFailed<Error> v => (400, string.Join("; ", v.Errors.Select(e => e.Message))),
                UnknownSku => (404, error.Message),
                OutOfStock o => (409, $"only {o.Left} left"),
                PaymentDeclined => (402, error.Message),
                _ => (500, "something went wrong"),
            };
        }

        static readonly OrderRequest Good = new("ada", "BOOK", 2, "4111");

        [Test]
        public void A_good_order_is_placed_and_saved()
        {
            var shop = new Shop();
            var result = shop.Place(1, Good);

            Assert.That(Respond(result), Is.EqualTo((201, "order 1")));
            Assert.That(result.Value.Receipt, Is.EqualTo(new Receipt("4111", 25.00m)));
            Assert.That(shop.Stock["BOOK"].Left, Is.EqualTo(3));
            Assert.That(shop.Saved, Has.Count.EqualTo(1));
            Assert.That(shop.Log, Is.Empty);
        }

        [Test]
        public void Validation_reports_every_failure_at_once_and_nothing_else_runs()
        {
            var shop = new Shop();
            var result = shop.Place(2, new OrderRequest("", "BOOK", 11, " "));

            Assert.That(Respond(result), Is.EqualTo((400, "customer is required; quantity is more than 10; card is required")));
            var failed = (ValidationFailed<Error>)result.Error;
            Assert.That(failed.Errors, Is.EqualTo(new Error[] { new Required("customer"), new TooMany("quantity", 10), new Required("card") }));
            Assert.That(failed.Context, Has.Count.EqualTo(1).And.Contains("placing order 2"));
            Assert.That(shop.Stock["BOOK"].Left, Is.EqualTo(5), "Reserve didn't run");
            Assert.That(shop.Log, Has.Count.EqualTo(1));
        }

        [Test]
        public void A_domain_failure_stops_the_chain_and_keeps_its_type_and_data()
        {
            var shop = new Shop();

            var unknown = shop.Place(3, Good with { Sku = "MUG" });
            Assert.That(Respond(unknown), Is.EqualTo((404, "no product has SKU MUG")));
            Assert.That(unknown.Error, Is.EqualTo(new UnknownSku("MUG") { Context = ["placing order 3"] }));

            var empty = shop.Place(4, Good with { Sku = "PEN" });
            Assert.That(Respond(empty), Is.EqualTo((409, "only 0 left")));

            var declined = shop.Place(5, Good with { Card = "5500" });
            Assert.That(Respond(declined), Is.EqualTo((402, "the payment was declined: card not accepted")));
            Assert.That(shop.Saved, Is.Empty);
        }

        [Test]
        public void A_wrapped_timeout_is_still_found_at_the_edge()
        {
            var shop = new Shop { DatabaseIsSlow = true };
            var result = shop.Place(6, Good);

            Assert.That(Respond(result), Is.EqualTo((503, "retry after 6s")));
            Assert.That(result.Error, Is.TypeOf<SaveFailed>());
            Assert.That(result.Error.Cause, Is.EqualTo(new DbTimeout(TimeSpan.FromSeconds(3))));
            Assert.That(string.Join(" > ", result.Error.Causes().Select(e => e.GetType().Name)), Is.EqualTo("SaveFailed > DbTimeout"));
            Assert.That(shop.Log.Single(), Is.EqualTo("SaveFailed: order 6 was not saved ---> DbTimeout: the database timed out after 3s"));
            Assert.That(result.Error.ToString(), Is.EqualTo("SaveFailed: order 6 was not saved (context: placing order 6) ---> DbTimeout: the database timed out after 3s"));
        }

        [Test]
        public async Task The_async_pipeline_gives_the_same_outcomes()
        {
            foreach (var (request, slow) in new[] { (Good, false), (Good with { Quantity = 0 }, false), (Good with { Sku = "PEN" }, false), (Good with { Card = "5" }, false), (Good, true) })
            {
                var (sync, async) = (new Shop { DatabaseIsSlow = slow }, new Shop { DatabaseIsSlow = slow });
                var expected = sync.Place(7, request);
                var actual = await async.PlaceAsync(7, request);
                Assert.That(Respond(actual), Is.EqualTo(Respond(expected)), request.ToString());
                Assert.That(actual.Equals(expected), Is.True, "same value, or an equal error with the same context");
            }
        }

        // The decision 14 examples, as written in docs/error-shape.md and docs/errors-in-practice.md: MapError to any
        // Error keeps the result a Result<T>.
        [Test]
        public void MapError_to_any_Error_keeps_a_Result_of_T()
        {
            Result<int> failed = new DbTimeout(TimeSpan.FromSeconds(1));

            Result<int> noted = failed.MapError(e => e.WithContext("loading order 1"));
            Result<int> wrapped = failed.MapError(e => new SaveFailed(1) { Cause = e });
            Assert.That(noted.Error, Is.TypeOf<DbTimeout>());
            Assert.That(noted.Error.Context, Has.Count.EqualTo(1).And.Contains("loading order 1"));
            Assert.That(wrapped.Error.TryFind<DbTimeout>(out _), Is.True);

            // Leaving Error behind is Map2's job.
            Result<int, string> text = failed.Map2(e => e.Message, v => v);
            Assert.That(text.Error, Is.EqualTo("the database timed out after 1s"));
        }

        [Test]
        public void Nothing_converts_a_string_to_an_error()
        {
            Result<string> message = "message";
            Assert.That(message.IsSuccess, Is.True, "decision 14: a bare string is a value now");
            Assert.That(message.Value, Is.EqualTo("message"));

            Assert.That(Result.Fail<string>("message").Error, Is.EqualTo(Error.From("message")), "a message error is explicit");
            Result<object> leaf = new Required("x");
            Assert.That(leaf.IsFailure, Is.True, "an Error converts to a failure, even into Result<object>");
        }

        // ---- The laws ----

        sealed record Leaf(int Code) : Error($"leaf {Code}");

        static readonly Gen<string[]> Notes = Gen.OneOf("a", "b").Array[0, 2];

        // Random leaves: a MessageError or a derived record, with or without notes. Small alphabets, so equal errors meet.
        static readonly Gen<Error> Errors = Gen.Select(Gen.Int[0, 2], Gen.Int[0, 3], Notes,
            (kind, code, notes) => (kind == 0 ? Error.From($"m{code}") : kind == 1 ? (Error)new Leaf(code) : new Required($"f{code}")) with { Context = notes });

        static readonly Gen<Result<int>> Results =
            Gen.Frequency((1, Gen.Int[-5, 5].Select(v => v.Ok())), (1, Errors.Select(e => e.Fail<int>())));

        static readonly Func<int, int>[] Maps = [x => x + 1, x => x * 2, x => -x];

        // Steps that fail for some inputs, each with its own error.
        static readonly Gen<Func<int, Result<int>>> Steps = Gen.Select(Gen.Int[0, 2], Errors, (k, error) =>
            (Func<int, Result<int>>)(x => x % 3 == k ? error : (x + k).Ok()));

        static readonly Gen<Func<Error, Error>> ErrorMaps = Gen.OneOf<Func<Error, Error>>(
            Gen.Const<Func<Error, Error>>(e => e.WithContext("mapped")),
            Gen.Const<Func<Error, Error>>(e => new SaveFailed(1) { Cause = e }),
            Errors.Select(other => (Func<Error, Error>)(_ => other)));

        [Test]
        public void Map_keeps_identity_and_composes()
        {
            Results.Sample(r => Assert.That(r.Map(x => x), Is.EqualTo(r)));
            Gen.Select(Results, Gen.Int[0, 2], Gen.Int[0, 2]).Sample((r, f, g) =>
                Assert.That(r.Map(Maps[f]).Map(Maps[g]), Is.EqualTo(r.Map(x => Maps[g](Maps[f](x))))));
        }

        [Test]
        public void Bind_is_a_monad()
        {
            Gen.Select(Gen.Int[-5, 5], Steps).Sample((x, f) => Assert.That(x.Ok().Bind(f), Is.EqualTo(f(x)))); // left identity
            Results.Sample(r => Assert.That(r.Bind(x => x.Ok()), Is.EqualTo(r))); // right identity
            Gen.Select(Results, Steps, Steps).Sample((r, f, g) =>
                Assert.That(r.Bind(f).Bind(g), Is.EqualTo(r.Bind(x => f(x).Bind(g))))); // associativity
        }

        [Test]
        public void A_failure_passes_through_Map_and_Bind_as_the_same_error()
        {
            Gen.Select(Errors, Gen.Int[0, 2], Steps).Sample((error, f, step) =>
            {
                var result = error.Fail<int>().Map(Maps[f]).Bind(step).Then(step).Check(_ => Result.Ok());
                Assert.That(result.Error, Is.SameAs(error));
            });
        }

        [Test]
        public void MapError_keeps_identity_composes_and_leaves_a_success_alone()
        {
            Results.Sample(r => Assert.That(r.MapError(e => e), Is.EqualTo(r)));
            Gen.Select(Results, ErrorMaps, ErrorMaps).Sample((r, f, g) =>
                Assert.That(r.MapError(f).MapError(g), Is.EqualTo(r.MapError(e => g(f(e))))));
            Gen.Select(Gen.Int, ErrorMaps).Sample((v, f) => Assert.That(v.Ok().MapError(f), Is.EqualTo(v.Ok())));
        }

        [Test]
        public void WithContext_through_a_pipeline_keeps_the_leaf()
        {
            Gen.Select(Errors, Notes, Gen.Int[0, 2]).Sample((leaf, notes, f) =>
            {
                var result = notes.Aggregate(leaf.Fail<int>(), (r, note) => r.WithContext(note).Map(Maps[f]).Bind(x => x.Ok()));
                var error = result.Error;

                Assert.That(error.GetType(), Is.EqualTo(leaf.GetType()), "the type a switch matches on");
                Assert.That(error.Message, Is.EqualTo(leaf.Message));
                Assert.That(error.Context, Is.EqualTo(leaf.Context.Concat(notes).ToArray()), "innermost first");
                Assert.That(error, Is.EqualTo(notes.Aggregate(leaf, (e, note) => e.WithContext(note))), "the same as WithContext on the error");
                Assert.That(result, Is.EqualTo(notes.Aggregate(leaf.Fail<int>(), (r, note) => r.MapError(e => e.WithContext(note)))));
            });
        }

        [Test]
        public void A_wrapper_keeps_the_leaf_findable()
        {
            Gen.Int[0, 3].Sample(code =>
            {
                var wrapped = new Leaf(code).Fail<int>()
                    .MapError(e => new SaveFailed(code) { Cause = e })
                    .WithContext("outer");
                Assert.That(wrapped.Error, Is.TypeOf<SaveFailed>());
                Assert.That(wrapped.Error.TryFind<Leaf>(out var found) && found == new Leaf(code), Is.True);
            });
        }

        [Test]
        public void Equality_follows_the_value_or_the_error()
        {
            Gen.Select(Results, Results).Sample((a, b) =>
            {
                var expected = a.IsSuccess == b.IsSuccess && (a.IsSuccess ? a.Value == b.Value : a.Error == b.Error);
                Assert.That(a.Equals(b), Is.EqualTo(expected));
                if (expected) Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            });
        }

        [Test]
        public void AsResult_and_the_conversions_keep_the_case()
        {
            Gen.Select(Gen.Bool, Gen.Int, Gen.Int[0, 3]).Sample((ok, v, code) =>
            {
                var typed = ok ? v.Ok<int, Leaf>() : new Leaf(code).Fail<int, Leaf>();
                var widened = typed.AsResult();
                Assert.That(widened.IsSuccess, Is.EqualTo(ok));
                Assert.That(ok ? widened.Value : (object)widened.Error, Is.EqualTo(ok ? (object)v : new Leaf(code)));

                Result<Unit> asUnit = widened.Map(_ => Unit.Value);
                Result<Unit> fromErrorUnit = new Leaf(code);
                Assert.That(fromErrorUnit.Error, Is.EqualTo(new Leaf(code)), "an Error converts to a failed Result<Unit>");
                Assert.That(asUnit.Match(_ => (Error?)null, e => e), Is.EqualTo(ok ? null : new Leaf(code)), "Result<Unit> holds the same Error");

#pragma warning disable CS0618 // VoidResult is obsolete (decision 14); it still holds and converts the same Error.
                VoidResult asVoid = widened.Map(_ => Unit.Value);
                VoidResult fromError = new Leaf(code);
                Assert.That(fromError.Match(_ => (Error?)null, e => e), Is.EqualTo(new Leaf(code)), "an Error converts to a failed VoidResult");
                Assert.That(asVoid.Match(_ => (Error?)null, e => e), Is.EqualTo(ok ? null : new Leaf(code)), "VoidResult holds the same Error");
#pragma warning restore CS0618
            });
        }
    }
}
