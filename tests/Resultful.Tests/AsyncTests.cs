using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using NUnit.Framework;

namespace Resultful.Tests
{
    // Stack PR 19 (M2 async). The scenario places an order through async steps that take a CancellationToken: a
    // customer lookup, a stream of order lines, a payment gateway that throws, and a caller that gives up. The laws hold
    // each async combinator (Task, ValueTask and token forms) to its sync form on random inputs. The rest pins the rules:
    // nothing in the core catches, a cancelled token cancels, Result.Try is the opt-in, and SwitchAsync is awaited.
    [TestFixture]
    public class AsyncTests
    {
        // ---- The scenario ----

        sealed record CustomerNotFound(int Id) : Error($"customer {Id} not found");
        sealed record OutOfStock(string Sku) : Error($"{Sku} is out of stock");
        sealed record PaymentDeclined(string Reason) : Error($"payment declined: {Reason}");

        sealed record Line(string Sku, int Quantity, decimal Price);
        sealed record Order(int CustomerId, IReadOnlyList<Line> Lines, decimal Total);

        sealed class Shop
        {
            public readonly List<string> Charged = [];
            public readonly List<string> Shipped = [];
            public CancellationToken GatewayToken;
            public Func<decimal, Task<string>> Gateway = total => Task.FromResult($"pay-{total}");

            public async Task<Result<int>> FindCustomerAsync(int id, CancellationToken ct)
            {
                await Task.Delay(1, ct);
                return id > 0 ? id : new CustomerNotFound(id);
            }

            public async IAsyncEnumerable<Result<Line>> ReadLines(IEnumerable<(string Sku, int Quantity)> basket, [EnumeratorCancellation] CancellationToken ct = default)
            {
                foreach (var (sku, quantity) in basket)
                {
                    await Task.Yield();
                    ct.ThrowIfCancellationRequested();
                    yield return sku.StartsWith('x') ? new OutOfStock(sku) : new Line(sku, quantity, 2.5m);
                }
            }

            // The gateway throws on a bad card; TryAsync turns that one exception into a failure, and nothing else.
            public Task<Result<string>> ChargeAsync(Order order, CancellationToken ct)
            {
                GatewayToken = ct;
                return Result.TryAsync(() => Gateway(order.Total), ex => new PaymentDeclined(ex.Message));
            }

            public async Task<Result<string>> PlaceAsync(int customerId, IEnumerable<(string, int)> basket, CancellationToken ct)
            {
                var customer = await FindCustomerAsync(customerId, ct);
                var order = await customer.BindAsync(
                    async (id, token) => (await ReadLines(basket, token).CollectAsync(token))
                        .Map(lines => new Order(id, lines, lines.Sum(l => l.Price * l.Quantity))),
                    ct);
                if (!order.TryGetValue(out var o))
                    return order.Error;

                var paid = (await ChargeAsync(o, ct)).Tee(Charged.Add);
                return await paid.TeeAsync(async (_, token) => { await Task.Delay(1, token); Shipped.Add($"order for {o.CustomerId}"); }, ct);
            }
        }

        [Test]
        public async Task An_order_is_found_collected_charged_and_shipped()
        {
            var shop = new Shop();
            using var cts = new CancellationTokenSource();
            var result = await shop.PlaceAsync(7, [("apple", 2), ("pear", 1)], cts.Token);

            Assert.That(result.Value, Is.EqualTo("pay-7.5"));
            Assert.That(shop.Charged, Is.EqualTo(new List<string> { "pay-7.5" }));
            Assert.That(shop.Shipped, Is.EqualTo(new List<string> { "order for 7" }));
            Assert.That(shop.GatewayToken, Is.EqualTo(cts.Token));
        }

        [Test]
        public async Task Domain_failures_stop_the_pipeline_as_errors()
        {
            var shop = new Shop();
            var noCustomer = await shop.PlaceAsync(0, [("apple", 1)], CancellationToken.None);
            var noStock = await shop.PlaceAsync(1, [("apple", 1), ("xbox", 1), ("ydisk", 1)], CancellationToken.None);

            Assert.That(noCustomer.Error, Is.EqualTo(new CustomerNotFound(0)));
            Assert.That(noStock.Error, Is.EqualTo(new OutOfStock("xbox")));
            Assert.That(shop.Charged, Is.Empty);
        }

        [Test]
        public async Task A_throwing_gateway_is_a_failure_only_because_TryAsync_opts_in()
        {
            var shop = new Shop { Gateway = async _ => { await Task.Yield(); throw new IOException("card expired"); } };
            var result = await shop.PlaceAsync(3, [("apple", 1)], CancellationToken.None);

            Assert.That(result.Error, Is.EqualTo(new PaymentDeclined("card expired")));
            Assert.That(shop.Shipped, Is.Empty);
        }

        [Test]
        public void A_bug_in_a_step_is_not_swallowed()
        {
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await new Result<int>[] { 1 }.ToAsync()
                .CollectAsync(CancellationToken.None).Map<IReadOnlyList<int>, int>(xs => xs[5]));
        }

        [Test]
        public void A_caller_that_gives_up_cancels_the_order_before_payment()
        {
            var shop = new Shop();
            using var cts = new CancellationTokenSource();
            shop.Gateway = _ => throw new AssertionException("the gateway must not be called");
            var basket = Enumerable.Range(0, 1000).Select(i => ($"sku{i}", 1)).Select(x => { if (x.Item1 == "sku3") cts.Cancel(); return x; });

            Assert.That(async () => await shop.PlaceAsync(5, basket, cts.Token), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(shop.Charged, Is.Empty);
            Assert.That(shop.Shipped, Is.Empty);
        }

        [Test]
        public void Cancellation_in_the_gateway_propagates_through_TryAsync()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var shop = new Shop { Gateway = async _ => { await Task.Delay(1000, cts.Token); return "never"; } };

            Assert.That(async () => await shop.PlaceAsync(5, [("apple", 1)], CancellationToken.None), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(shop.Charged, Is.Empty);
        }

        // ---- Laws: each async form equals its sync form ----

        sealed record Leaf(int Code) : Error($"leaf {Code}");

        static readonly Gen<Result<int>> Results = Gen.Frequency(
            (1, Gen.Int[-50, 50].Select(v => v.Ok())),
            (1, Gen.Int[0, 3].Select(c => new Leaf(c).Fail<int>())));

        static readonly Gen<Result<int, string>> Typed = Gen.Frequency(
            (1, Gen.Int[-50, 50].Select(v => v.Ok<int, string>())),
            (1, Gen.OneOf("a", "b", "c").Select(e => e.Fail<int, string>())));

        static Result<int> Step(int x, int k) => (x + k) % 3 == 0 ? new Leaf(k & 3) : x * 2 + k;
        static Result<int, string> TypedStep(int x, int k) => (x + k) % 3 == 0 ? $"e{k}" : x * 2 + k;

        static async ValueTask<T> Later<T>(T value)
        {
            await Task.Yield();
            return value;
        }

        [Test]
        public async Task Token_forms_equal_their_sync_forms()
        {
            await Gen.Select(Results, Gen.Int[-9, 9]).SampleAsync(async t =>
            {
                var (r, k) = t;
                var seen = new List<int>();
                Assert.That(await r.MapAsync((x, _) => Task.FromResult(x + k), CancellationToken.None), Is.EqualTo(r.Map(x => x + k)));
                Assert.That(await r.BindAsync((x, _) => Task.FromResult(Step(x, k)), CancellationToken.None), Is.EqualTo(r.Bind(x => Step(x, k))));
                Assert.That(await r.TeeAsync((x, _) => { seen.Add(x); return Task.CompletedTask; }, CancellationToken.None), Is.EqualTo(r));
                Assert.That(seen, Is.EqualTo(r.IsSuccess ? new[] { r.Value } : Array.Empty<int>()));
            });
            await Gen.Select(Typed, Gen.Int[-9, 9]).SampleAsync(async t =>
            {
                var (r, k) = t;
                Assert.That(await r.MapAsync(async (x, _) => { await Task.Yield(); return x + k; }, CancellationToken.None), Is.EqualTo(r.Map(x => x + k)));
                Assert.That(await r.BindAsync((x, _) => Task.FromResult(TypedStep(x, k)), CancellationToken.None), Is.EqualTo(r.Bind(x => TypedStep(x, k))));
                Assert.That(await r.TeeAsync((_, _) => Task.CompletedTask, CancellationToken.None), Is.EqualTo(r));
            });
        }

        [Test]
        public async Task Task_receivers_equal_their_sync_forms()
        {
            await Gen.Select(Results, Gen.Int[-9, 9]).SampleAsync(async t =>
            {
                var (r, k) = t;
                Assert.That(await Task.FromResult(r).Map(x => x + k), Is.EqualTo(r.Map(x => x + k)));
                Assert.That(await Task.FromResult(r).Bind(x => Step(x, k)), Is.EqualTo(r.Bind(x => Step(x, k))));
                Assert.That(await Task.FromResult(r).BindAsync(x => Task.FromResult(Step(x, k))), Is.EqualTo(r.Bind(x => Step(x, k))));
                Assert.That(await Task.FromResult(r).MapError(e => e.WithContext("k")), Is.EqualTo(r.MapError(e => e.WithContext("k"))));
                Assert.That(await Task.FromResult(r).Match(x => x, e => -1), Is.EqualTo(r.Match(x => x, e => -1)));
            });
            await Gen.Select(Typed, Gen.Int[-9, 9]).SampleAsync(async t =>
            {
                var (r, k) = t;
                Assert.That(await Task.FromResult(r).Map(x => x + k), Is.EqualTo(r.Map(x => x + k)));
                Assert.That(await Task.FromResult(r).BindAsync(x => Task.FromResult(TypedStep(x, k))), Is.EqualTo(r.Bind(x => TypedStep(x, k))));
                Assert.That(await Task.FromResult(r).MapError(e => e.Length + k), Is.EqualTo(r.MapError(e => e.Length + k)));
            });
        }

        [Test]
        public async Task ValueTask_receivers_equal_their_sync_forms_completed_or_not()
        {
            await Gen.Select(Results, Gen.Int[-9, 9], Gen.Bool).SampleAsync(async t =>
            {
                var (r, k, later) = t;
                ValueTask<Result<int>> V() => later ? Later(r) : new ValueTask<Result<int>>(r);
                var tees = 0;
                Assert.That(await V().Map(x => x + k), Is.EqualTo(r.Map(x => x + k)));
                Assert.That(await V().Bind(x => Step(x, k)), Is.EqualTo(r.Bind(x => Step(x, k))));
                Assert.That(await V().BindAsync(x => Later(Step(x, k))), Is.EqualTo(r.Bind(x => Step(x, k))));
                Assert.That(await V().MapError(e => e.WithContext("k")), Is.EqualTo(r.MapError(e => e.WithContext("k"))));
                Assert.That(await V().Match(x => x, e => -1), Is.EqualTo(r.Match(x => x, e => -1)));
                Assert.That(await V().Tee(_ => tees++), Is.EqualTo(r));
                Assert.That(tees, Is.EqualTo(r.IsSuccess ? 1 : 0));
            });
            await Gen.Select(Typed, Gen.Int[-9, 9], Gen.Bool).SampleAsync(async t =>
            {
                var (r, k, later) = t;
                ValueTask<Result<int, string>> V() => later ? Later(r) : new ValueTask<Result<int, string>>(r);
                var tees = 0;
                Assert.That(await V().Map(x => x + k), Is.EqualTo(r.Map(x => x + k)));
                Assert.That(await V().Bind(x => TypedStep(x, k)), Is.EqualTo(r.Bind(x => TypedStep(x, k))));
                Assert.That(await V().BindAsync(x => Later(TypedStep(x, k))), Is.EqualTo(r.Bind(x => TypedStep(x, k))));
                Assert.That(await V().MapError(e => e.Length + k), Is.EqualTo(r.MapError(e => e.Length + k)));
                Assert.That(await V().Match(x => x.ToString(), e => e), Is.EqualTo(r.Match(x => x.ToString(), e => e)));
                Assert.That(await V().Tee(_ => tees++), Is.EqualTo(r));
                Assert.That(tees, Is.EqualTo(r.IsSuccess ? 1 : 0));
            });
        }

        [Test]
        public async Task CollectAsync_and_PartitionAsync_equal_the_list_model()
        {
            await Results.List[0, 8].SampleAsync(async rs =>
            {
                var collected = await rs.ToAsync().CollectAsync(CancellationToken.None);
                if (rs.All(r => r.IsSuccess))
                    Assert.That(collected.Value, Is.EqualTo(rs.Select(r => r.Value)));
                else
                    Assert.That(collected.Error, Is.SameAs(rs.First(r => r.IsFailure).Error));

                var (values, errors) = await rs.ToAsync().PartitionAsync(CancellationToken.None);
                Assert.That(values, Is.EqualTo(rs.Where(r => r.IsSuccess).Select(r => r.Value)));
                Assert.That(errors, Is.EqualTo(rs.Where(r => r.IsFailure).Select(r => r.Error)));
            });
            await Typed.List[0, 8].SampleAsync(async rs =>
            {
                var collected = await rs.ToAsync().CollectAsync(CancellationToken.None);
                Assert.That(collected.IsSuccess, Is.EqualTo(rs.All(r => r.IsSuccess)));
                if (collected.IsSuccess)
                    Assert.That(collected.Value, Is.EqualTo(rs.Select(r => r.Value)));
                else
                    Assert.That(collected.Error, Is.EqualTo(rs.First(r => r.IsFailure).Error));

                var (values, errors) = await rs.ToAsync().PartitionAsync(CancellationToken.None);
                Assert.That(values, Is.EqualTo(rs.Where(r => r.IsSuccess).Select(r => r.Value)));
                Assert.That(errors, Is.EqualTo(rs.Where(r => r.IsFailure).Select(r => r.Error)));
            });
        }

        [Test]
        public async Task SwitchAsync_calls_what_Switch_calls()
        {
            await Gen.Select(Results, Typed).SampleAsync(async t =>
            {
                var (r, typed) = t;
                var sync = new List<string>();
                var async = new List<string>();
                r.Switch(v => sync.Add($"ok {v}"), e => sync.Add(e.Message));
                await r.SwitchAsync(async v => { await Task.Yield(); async.Add($"ok {v}"); }, async e => { await Task.Yield(); async.Add(e.Message); });
                typed.Switch(v => sync.Add($"ok {v}"), e => sync.Add(e));
                await typed.SwitchAsync(async v => { await Task.Yield(); async.Add($"ok {v}"); }, async e => { await Task.Yield(); async.Add(e); });
                Assert.That(async, Is.EqualTo(sync));
            });
        }

        [Test]
        public void Try_equals_Ok_or_the_mapped_failure()
        {
            Gen.Select(Gen.Int[-50, 50], Gen.Bool).Sample((x, throws) =>
            {
                var expected = throws ? new Leaf(x).Fail<int>() : x.Ok();
                Assert.That(Result.Try(() => throws ? throw new FormatException($"{x}") : x, ex => new Leaf(int.Parse(ex.Message))), Is.EqualTo(expected));
                Assert.That(Result.TryAsync(async () => { await Task.Yield(); return throws ? throw new FormatException($"{x}") : x; }, ex => new Leaf(int.Parse(ex.Message))).Result, Is.EqualTo(expected));
            });
        }

        // ---- Exceptions and cancellation propagate ----

        sealed class Boom() : Exception("boom");

        static Task<int> Explode(int _, CancellationToken __) => throw new Boom();

        [Test]
        public void A_throwing_token_step_faults_the_task_instead_of_throwing_from_the_call()
        {
            var mapped = Result.Ok(1).MapAsync(Explode, CancellationToken.None);
            var bound = 1.Ok<int, string>().BindAsync<int>((_, _) => throw new Boom(), CancellationToken.None);
            var teed = Result.Ok(1).TeeAsync((_, _) => throw new Boom(), CancellationToken.None);

            Assert.That(new Task[] { mapped, bound, teed }.Select(t => t.IsFaulted), Is.All.True);
            Assert.ThrowsAsync<Boom>(() => mapped);
            Assert.ThrowsAsync<Boom>(() => bound);
            Assert.ThrowsAsync<Boom>(() => teed);
        }

        [Test]
        public void A_throwing_step_throws_from_every_receiver()
        {
            Assert.ThrowsAsync<Boom>(() => Task.FromResult(Result.Ok(1)).Map<int, int>(_ => throw new Boom()));
            Assert.ThrowsAsync<Boom>(async () => await new ValueTask<Result<int>>(Result.Ok(1)).Map<int, int>(_ => throw new Boom()));
            Assert.ThrowsAsync<Boom>(async () => await Later(1.Ok<int, string>()).BindAsync<int, int, string>(_ => throw new Boom()));
            Assert.ThrowsAsync<Boom>(async () => await Later("e".Fail<int, string>()).MapError<int, string, int>(_ => throw new Boom()));
            Assert.ThrowsAsync<Boom>(async () => await Throwing().CollectAsync(CancellationToken.None));
            Assert.ThrowsAsync<Boom>(async () => await Throwing().PartitionAsync(CancellationToken.None));
        }

        static async IAsyncEnumerable<Result<int>> Throwing()
        {
            yield return 1;
            await Task.Yield();
            throw new Boom();
        }

        [Test]
        public void A_cancelled_token_cancels_a_success_without_calling_the_step()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var calls = 0;
            var mapped = Result.Ok(1).MapAsync((x, _) => { calls++; return Task.FromResult(x); }, cts.Token);
            var bound = 1.Ok<int, string>().BindAsync((x, _) => { calls++; return Task.FromResult(x.Ok<int, string>()); }, cts.Token);
            var teed = Result.Ok(1).TeeAsync((_, _) => { calls++; return Task.CompletedTask; }, cts.Token);

            Assert.That(new Task[] { mapped, bound, teed }.Select(t => t.IsCanceled), Is.All.True);
            Assert.That(calls, Is.Zero);
            Assert.That(async () => await mapped, Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public async Task A_failure_runs_no_step_so_the_token_is_not_read()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var failed = new Leaf(1).Fail<int>();

            Assert.That(await failed.MapAsync(Explode, cts.Token), Is.EqualTo(failed));
            Assert.That(await failed.TeeAsync((_, _) => throw new Boom(), cts.Token), Is.EqualTo(failed));
        }

        [Test]
        public async Task The_token_reaches_the_step_and_cancels_it_midway()
        {
            using var cts = new CancellationTokenSource();
            CancellationToken seen = default;
            await Result.Ok(1).TeeAsync((_, ct) => { seen = ct; return Task.CompletedTask; }, cts.Token);
            Assert.That(seen, Is.EqualTo(cts.Token));

            var slow = "x".Ok<string, string>().MapAsync(async (x, ct) => { await Task.Delay(Timeout.Infinite, ct); return x; }, cts.Token);
            Assert.That(slow.IsCompleted, Is.False);
            await cts.CancelAsync();
            Assert.That(async () => await slow, Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public void A_cancelled_token_stops_CollectAsync_and_PartitionAsync_even_when_the_stream_ignores_it()
        {
            using var cts = new CancellationTokenSource();
            async IAsyncEnumerable<Result<int>> Ignoring()
            {
                for (var i = 0; ; i++)
                {
                    if (i == 3) cts.Cancel();
                    await Task.Yield();
                    yield return i;
                }
            }

            Assert.That(async () => await Ignoring().CollectAsync(cts.Token), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(async () => await Ignoring().PartitionAsync(cts.Token), Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public void Try_captures_exceptions_but_never_cancellation()
        {
            Assert.That(Result.Try<int>(() => throw new IOException("disk"), ex => new Leaf(ex.Message.Length)).Error, Is.EqualTo(new Leaf(4)));
            Assert.Throws<OperationCanceledException>(() => Result.Try<int>(() => throw new OperationCanceledException(), _ => new Leaf(0)));
            Assert.ThrowsAsync<OperationCanceledException>(() => Result.TryAsync<int>(() => throw new OperationCanceledException(), _ => new Leaf(0)));
            Assert.That(async () => await Result.TryAsync(() => Task.FromCanceled<int>(new CancellationToken(true)), _ => new Leaf(0)), Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public async Task TryAsync_captures_a_synchronous_throw_and_a_faulted_task()
        {
            Assert.That((await Result.TryAsync<int>(() => throw new IOException("sync"), ex => new Leaf(ex.Message.Length))).Error, Is.EqualTo(new Leaf(4)));
            Assert.That((await Result.TryAsync(() => Task.FromException<int>(new IOException("fault")), ex => new Leaf(ex.Message.Length))).Error, Is.EqualTo(new Leaf(5)));
            Assert.That((await Result.TryAsync(() => Task.FromResult(9), _ => new Leaf(0))).Value, Is.EqualTo(9));
        }

        [Test]
        public void A_synchronous_throw_faults_the_task_in_token_and_non_token_forms_alike()
        {
            static Task<Result<int>> BoomStep(int _) => throw new Boom();
            static Task<Result<int, string>> TypedBoom(int _) => throw new Boom();
            // Each token form and the non-token form beside it: neither throws at the call, both give a faulted task.
            var forms = new (string Name, Func<Task> Call)[]
            {
                ("BindAsync with a token", () => Result.Ok(1).BindAsync<int>((_, _) => throw new Boom(), CancellationToken.None)),
                ("BindAsync", () => Result.Ok(1).BindAsync(BoomStep)),
                ("typed BindAsync with a token", () => 1.Ok<int, string>().BindAsync<int>((_, _) => throw new Boom(), CancellationToken.None)),
                ("typed BindAsync", () => 1.Ok<int, string>().BindAsync(TypedBoom)),
                ("MapAsync with a token", () => Result.Ok(1).MapAsync(Explode, CancellationToken.None)),
                ("MatchAsync", () => Result.Ok(1).MatchAsync<int>(_ => throw new Boom(), _ => Task.FromResult(0))),
                ("typed MatchAsync", () => "e".Fail<int, string>().MatchAsync<int>(_ => Task.FromResult(0), _ => throw new Boom())),
                ("TeeAsync with a token", () => Result.Ok(1).TeeAsync((_, _) => throw new Boom(), CancellationToken.None)),
                ("ReturnOrValueAsync", () => new Leaf(1).Fail<int>().ReturnOrValueAsync(_ => throw new Boom())),
                ("typed ReturnOrValueAsync", () => "e".Fail<int, string>().ReturnOrValueAsync(_ => throw new Boom())),
                ("Option.SwitchAsync", () => 1.Some().SwitchAsync(_ => throw new Boom(), _ => Task.CompletedTask)),
                ("Option.MatchAsync", () => Option<int>.None.MatchAsync<int>(_ => Task.FromResult(0), _ => throw new Boom())),
            };
            foreach (var (name, call) in forms)
            {
                Task task = null!;
                Assert.DoesNotThrow(() => task = call(), name);
                Assert.That(task.IsFaulted, Is.True, name);
                Assert.That(task.Exception!.InnerException, Is.InstanceOf<Boom>(), name);
            }
        }

        [Test]
#pragma warning disable CA2201 // the test throws a runtime-reserved exception on purpose
        public void Try_never_catches_fatal_exceptions_or_cancellations_wrapped_in_an_AggregateException()
        {
            Assert.Throws<OutOfMemoryException>(() => Result.Try<int>(() => throw new OutOfMemoryException(), _ => new Leaf(0)));
            Assert.Throws<AggregateException>(() => Result.Try<int>(() => throw new AggregateException(new OperationCanceledException(), new TaskCanceledException()), _ => new Leaf(0)));
            Assert.ThrowsAsync<OutOfMemoryException>(() => Result.TryAsync<int>(() => throw new OutOfMemoryException(), _ => new Leaf(0)));
            Assert.That(Result.Try<int>(() => throw new AggregateException(new OperationCanceledException(), new IOException()), _ => new Leaf(2)).Error, Is.EqualTo(new Leaf(2)));
            Assert.That(Result.Try<int>(() => throw new AggregateException(), _ => new Leaf(3)).Error, Is.EqualTo(new Leaf(3)));
        }
#pragma warning restore CA2201

        [Test]
        public async Task TryAsync_with_a_token_passes_it_cancels_and_captures()
        {
            using var cts = new CancellationTokenSource();
            Assert.That((await Result.TryAsync(ct => Task.FromResult(ct == cts.Token ? 1 : 0), _ => new Leaf(0), cts.Token)).Value, Is.EqualTo(1));
            Assert.That((await Result.TryAsync<int>(_ => throw new IOException("disk"), ex => new Leaf(ex.Message.Length), cts.Token)).Error, Is.EqualTo(new Leaf(4)));
            await cts.CancelAsync();
            var calls = 0;
            var cancelled = Result.TryAsync(_ => { calls++; return Task.FromResult(1); }, _ => new Leaf(0), cts.Token);
            Assert.That(async () => await cancelled, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void Null_arguments_to_the_ValueTask_and_stream_helpers_throw_at_the_call()
        {
            var v = new ValueTask<Result<int>>(Result.Ok(1));
            var typed = new ValueTask<Result<int, string>>(1.Ok<int, string>());
            Assert.That(() => v.Map<int, int>(null!), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("mapFunc"));
            Assert.That(() => v.BindAsync<int, int>(null!), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("bindFunc"));
            Assert.That(() => v.Match<int, int>(x => x, null!), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("errorFunc"));
            Assert.That(() => typed.Tee(null!), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("action"));
            Assert.That(() => typed.MapError<int, string, int>(null!), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("errorMapFunc"));
            Assert.That(() => ((IAsyncEnumerable<Result<int>>)null!).CollectAsync(CancellationToken.None), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("values"));
            Assert.That(() => ((IAsyncEnumerable<Result<int, string>>)null!).PartitionAsync(CancellationToken.None), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("values"));
        }

        // ---- SwitchAsync returns a Task that awaits the callback ----

        [Test]
        public async Task SwitchAsync_completes_only_when_the_callback_does()
        {
            var done = new List<string>();
            var gate = new TaskCompletionSource();
            var pending = Result.Ok(1).SwitchAsync(async _ => { await gate.Task; done.Add("result"); }, _ => Task.CompletedTask);
            Assert.That(pending.IsCompleted, Is.False);
            gate.SetResult();
            await pending;

            await 1.Ok<int, string>().SwitchAsync(async _ => { await Task.Delay(5); done.Add("typed"); }, _ => Task.CompletedTask);
#pragma warning disable CS0618 // the void results keep the same SwitchAsync until they are removed
            await ((VoidResult)Result.Ok()).SwitchAsync(async _ => { await Task.Delay(5); done.Add("void"); }, _ => Task.CompletedTask);
            await ((VoidResult<string>)Result.Ok<string>()).SwitchAsync(async _ => { await Task.Delay(5); done.Add("void typed"); }, _ => Task.CompletedTask);
#pragma warning restore CS0618
            Assert.That(done, Is.EqualTo(new List<string> { "result", "typed", "void", "void typed" }));
        }

        [Test]
        public void A_throwing_SwitchAsync_callback_faults_the_task()
        {
            var sync = "bad".Fail<int, string>().SwitchAsync(_ => Task.CompletedTask, e => throw new InvalidOperationException(e));
            var async = new Leaf(1).Fail<int>().SwitchAsync(_ => Task.CompletedTask, async e => { await Task.Yield(); throw new InvalidOperationException(e.Message); });

            Assert.That(sync.IsFaulted, Is.True);
            Assert.ThrowsAsync<InvalidOperationException>(() => sync);
            Assert.ThrowsAsync<InvalidOperationException>(() => async);
        }
    }

    static class AsyncSequence
    {
        public static async IAsyncEnumerable<T> ToAsync<T>(this IEnumerable<T> items)
        {
            foreach (var item in items)
            {
                await Task.Yield();
                yield return item;
            }
        }
    }
}
