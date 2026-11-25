using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CsCheck;
using NUnit.Framework;

namespace Resultful.Tests
{
    // Stack PR 24d (decision 22, item 5): the operators over IAsyncEnumerable<Result<…>>. The laws hold each operator to
    // the same operation over the materialised list. The rest pins the rules: laziness (pulls are counted), disposal
    // (the producer's finally runs), cancellation (the token reaches the producer, OperationCanceledException surfaces),
    // concurrency (the most in flight, the order), and exceptions that stay exceptions. The scenario is a queue worker.
    [TestFixture]
    public class AsyncStreamTests
    {
        sealed record Leaf(int Code) : Error($"leaf {Code}");
        sealed class Boom() : Exception("boom");

        static readonly Gen<Result<int>> Results = Gen.Frequency(
            (2, Gen.Int[-50, 50].Select(v => v.Ok())),
            (1, Gen.Int[0, 3].Select(c => new Leaf(c).Fail<int>())));

        static readonly Gen<Result<int, string>> Typed = Gen.Frequency(
            (2, Gen.Int[-50, 50].Select(v => v.Ok<int, string>())),
            (1, Gen.OneOf("a", "b", "c").Select(e => e.Fail<int, string>())));

        static Result<int> Step(int x) => x % 3 == 0 ? new Leaf(x & 3) : x * 2;
        static Result<int, string> TypedStep(int x) => x % 3 == 0 ? $"e{x}" : x * 2;

        // A result as a comparable value: Error records compare by value, lists by their items.
        static (bool, string) Show<T>(Result<IReadOnlyList<T>> r) => r.Match(xs => (true, string.Join(",", xs)), e => (false, e.ToString()));
        static (bool, string) Show<T>(Result<IReadOnlyList<T>, string> r) => r.Match(xs => (true, string.Join(",", xs)), e => (false, e));
        static (bool, string) Show<T>(Validation<IReadOnlyList<T>> v) => v.TryGetValue(out var xs) ? (true, string.Join(",", xs)) : (false, string.Join("|", v.Errors));
        static (bool, string) Show<T>(Validation<IReadOnlyList<T>, string> v) => v.TryGetValue(out var xs) ? (true, string.Join(",", xs)) : (false, string.Join("|", v.Errors));

        // The list models: first failure (Sequence as a fold of Bind) and accumulation (a fold of Validation.Combine).
        static Result<IReadOnlyList<int>> Sequence(IEnumerable<Result<int>> rs) =>
            rs.Aggregate(Result.Ok<IReadOnlyList<int>>([]), (acc, r) => acc.Bind(xs => r.Map(x => (IReadOnlyList<int>)[.. xs, x])));
        static Result<IReadOnlyList<int>, string> Sequence(IEnumerable<Result<int, string>> rs) =>
            rs.Aggregate(((IReadOnlyList<int>)[]).Ok<IReadOnlyList<int>, string>(), (acc, r) => acc.Bind(xs => r.Map(x => (IReadOnlyList<int>)[.. xs, x])));
        static Validation<IReadOnlyList<int>> Accumulate(IEnumerable<Result<int>> rs) =>
            rs.Aggregate(Validation.Ok<IReadOnlyList<int>>([]), (acc, r) => Validation.Combine(acc, (Validation<int>)r, (xs, x) => (IReadOnlyList<int>)[.. xs, x]));
        static Validation<IReadOnlyList<int>, string> Accumulate(IEnumerable<Result<int, string>> rs) =>
            rs.Aggregate(Validation.Ok<IReadOnlyList<int>, string>([]), (acc, r) => Validation.Combine(acc, (Validation<int, string>)r, (xs, x) => (IReadOnlyList<int>)[.. xs, x]));

        static async ValueTask<T> Later<T>(T value)
        {
            await Task.Yield();
            return value;
        }

        /// <summary>A producer that counts what it hands out, records the token it was given, and whether its finally ran.</summary>
        sealed class Producer<T>(IEnumerable<T> items, Func<int, CancellationToken, Task>? beforeItem = null)
        {
            public int Pulls;
            public bool Completed;
            public bool FinallyRan;
            public CancellationToken Token;

            public async IAsyncEnumerable<T> Read([EnumeratorCancellation] CancellationToken ct = default)
            {
                Token = ct;
                try
                {
                    var i = 0;
                    foreach (var item in items)
                    {
                        await (beforeItem?.Invoke(i++, ct) ?? Task.Delay(0, ct));
                        ct.ThrowIfCancellationRequested();
                        Pulls++;
                        yield return item;
                    }

                    Completed = true;
                }
                finally
                {
                    FinallyRan = true;
                }
            }
        }

        static async IAsyncEnumerable<Result<int>> ThrowsAfter(int count, Exception e)
        {
            for (var i = 0; i < count; i++)
            {
                await Task.Yield();
                yield return i;
            }

            throw e;
        }

        static async Task<List<T>> Drain<T>(IAsyncEnumerable<T> s, CancellationToken ct = default)
        {
            var list = new List<T>();
            await foreach (var x in s.WithCancellation(ct)) list.Add(x);
            return list;
        }

        // ---- Laws: each operator equals its list model ----

        [Test]
        public async Task SelectResultAsync_equals_Map_and_Bind_over_the_list()
        {
            await Results.List[0, 8].SampleAsync(async rs =>
            {
                Assert.That(await Drain(rs.ToAsync().SelectResultAsync(x => x + 1)), Is.EqualTo(rs.Select(r => r.Map(x => x + 1))));
                Assert.That(await Drain(rs.ToAsync().SelectResultAsync(Step)), Is.EqualTo(rs.Select(r => r.Bind(Step))));
                Assert.That(await Drain(rs.ToAsync().SelectResultAsync((x, _) => Later(x + 1))), Is.EqualTo(rs.Select(r => r.Map(x => x + 1))));
                Assert.That(await Drain(rs.ToAsync().SelectResultAsync((x, _) => Later(Step(x)))), Is.EqualTo(rs.Select(r => r.Bind(Step))));
            });
            await Typed.List[0, 8].SampleAsync(async rs =>
            {
                Assert.That(await Drain(rs.ToAsync().SelectResultAsync(x => x + 1)), Is.EqualTo(rs.Select(r => r.Map(x => x + 1))));
                Assert.That(await Drain(rs.ToAsync().SelectResultAsync(TypedStep)), Is.EqualTo(rs.Select(r => r.Bind(TypedStep))));
                Assert.That(await Drain(rs.ToAsync().SelectResultAsync((x, _) => Later(x + 1))), Is.EqualTo(rs.Select(r => r.Map(x => x + 1))));
                Assert.That(await Drain(rs.ToAsync().SelectResultAsync((x, _) => Later(TypedStep(x)))), Is.EqualTo(rs.Select(r => r.Bind(TypedStep))));
            });
        }

        [Test]
        public async Task Successes_Failures_and_PartitionAsync_partition_exactly()
        {
            await Results.List[0, 10].SampleAsync(async rs =>
            {
                var successes = await Drain(rs.ToAsync().Successes());
                var failures = await Drain(rs.ToAsync().Failures());
                var (values, errors) = await rs.ToAsync().PartitionAsync(CancellationToken.None);
                Assert.That(successes, Is.EqualTo(rs.Where(r => r.IsSuccess).Select(r => r.Value)));
                Assert.That(failures, Is.EqualTo(rs.Where(r => r.IsFailure).Select(r => r.Error)));
                Assert.That((values, errors), Is.EqualTo((successes, failures)));
                Assert.That(values.Count + errors.Count, Is.EqualTo(rs.Count));
            });
            await Typed.List[0, 10].SampleAsync(async rs =>
            {
                var successes = await Drain(rs.ToAsync().Successes());
                var failures = await Drain(rs.ToAsync().Failures());
                var (values, errors) = await rs.ToAsync().PartitionAsync(CancellationToken.None);
                Assert.That(successes, Is.EqualTo(rs.Where(r => r.IsSuccess).Select(r => r.Value)));
                Assert.That(failures, Is.EqualTo(rs.Where(r => r.IsFailure).Select(r => r.Error)));
                Assert.That((values, errors), Is.EqualTo((successes, failures)));
                Assert.That(values.Count + errors.Count, Is.EqualTo(rs.Count));
            });
        }

        [Test]
        public async Task CollectAsync_equals_Sequence_and_CollectAllAsync_equals_accumulation_over_the_list()
        {
            await Results.List[0, 8].SampleAsync(async rs =>
            {
                Assert.That(Show(await rs.ToAsync().CollectAsync(CancellationToken.None)), Is.EqualTo(Show(Sequence(rs))));
                Assert.That(Show(await rs.ToAsync().CollectAllAsync(CancellationToken.None)), Is.EqualTo(Show(Accumulate(rs))));
            });
            await Typed.List[0, 8].SampleAsync(async rs =>
            {
                Assert.That(Show(await rs.ToAsync().CollectAsync(CancellationToken.None)), Is.EqualTo(Show(Sequence(rs))));
                Assert.That(Show(await rs.ToAsync().CollectAllAsync(CancellationToken.None)), Is.EqualTo(Show(Accumulate(rs))));
            });
        }

        [Test]
        public async Task TraverseAllAsync_equals_accumulation_over_the_mapped_list_at_any_concurrency()
        {
            await Gen.Select(Gen.Int[-20, 20].List[0, 10], Gen.Int[1, 4]).SampleAsync(async (xs, max) =>
            {
                var all = await xs.ToAsync().TraverseAllAsync((x, _) => Later(Step(x)), max, preserveOrder: true, CancellationToken.None);
                Assert.That(Show(all), Is.EqualTo(Show(Accumulate(xs.Select(Step)))));
                var typed = await xs.ToAsync().TraverseAllAsync((x, _) => Later(TypedStep(x)), max, preserveOrder: true, CancellationToken.None);
                Assert.That(Show(typed), Is.EqualTo(Show(Accumulate(xs.Select(TypedStep)))));

                // Unordered: the same values and errors, as multisets.
                var unordered = await xs.ToAsync().TraverseAllAsync((x, _) => Later(Step(x)), max, preserveOrder: false, CancellationToken.None);
                var model = Accumulate(xs.Select(Step));
                if (model.TryGetValue(out var expected))
                    Assert.That(unordered.Value, Is.EquivalentTo(expected));
                else
                    Assert.That(unordered.Errors, Is.EquivalentTo(model.Errors));
            });
        }

        [Test]
        public async Task TraverseAsync_equals_Sequence_over_the_list_at_any_concurrency_and_timing()
        {
            // Each item takes a random time, so calls finish out of order; the answer is still the sequential one: the
            // values in order, or the earliest item's failure. preserveOrder orders only the values of a success.
            var items = Gen.Select(Gen.Int[-20, 20], Gen.Int[0, 3]).List[0, 10];
            await Gen.Select(items, Gen.Int[1, 4], Gen.Bool).SampleAsync(async (xs, max, order) =>
            {
                var values = xs.Select(t => t.Item1).ToList();
                var traversed = await xs.ToAsync().TraverseAsync(async (t, ct) => { await Task.Delay(t.Item2, ct); return Step(t.Item1); }, max, order, CancellationToken.None);
                var typed = await xs.ToAsync().TraverseAsync(async (t, ct) => { await Task.Delay(t.Item2, ct); return TypedStep(t.Item1); }, max, order, CancellationToken.None);
                var model = Sequence(values.Select(Step));
                var typedModel = Sequence(values.Select(TypedStep));
                if (order || !model.IsSuccess)
                    Assert.That(Show(traversed), Is.EqualTo(Show(model)));
                else
                    Assert.That(traversed.Value, Is.EquivalentTo(model.Value));
                if (order || !typedModel.IsSuccess)
                    Assert.That(Show(typed), Is.EqualTo(Show(typedModel)));
                else
                    Assert.That(typed.Value, Is.EquivalentTo(typedModel.Value));
            }, iter: 100);
        }

        // ---- Laziness, disposal and the selector ----

        [Test]
        public async Task SelectResultAsync_pulls_one_item_per_item_and_breaking_disposes_the_source()
        {
            var producer = new Producer<Result<int>>(Enumerable.Range(1, 100).Select(i => i.Ok()));
            var seen = new List<int>();
            await foreach (var r in producer.Read().SelectResultAsync(x => x * 10))
            {
                seen.Add(r.Value);
                Assert.That(producer.Pulls, Is.EqualTo(seen.Count));
                if (seen.Count == 3) break;
            }

            Assert.That(seen, Is.EqualTo(L(10, 20, 30)));
            Assert.That((producer.Pulls, producer.Completed, producer.FinallyRan), Is.EqualTo((3, false, true)));
        }

        [Test]
        public async Task The_selector_is_never_called_for_a_failure()
        {
            var calls = new List<int>();
            Result<int>[] items = [1, new Leaf(2), 3, new Leaf(4)];
            var mapped = await Drain(items.ToAsync().SelectResultAsync(async (x, _) => { calls.Add(x); await Task.Yield(); return x; }));
            var bound = await Drain(items.ToAsync().SelectResultAsync(x => { calls.Add(x); return x.Ok(); }));

            Assert.That(calls, Is.EqualTo(L(1, 3, 1, 3)));
            Assert.That(mapped[1].Error, Is.SameAs(items[1].Error));
            Assert.That(bound[3].Error, Is.SameAs(items[3].Error));
        }

        [Test]
        public async Task A_bind_lambda_picks_the_bind_overload()
        {
            // The map overload would give Result<Result<int>>; the more specific bind overload wins.
            IAsyncEnumerable<Result<int>> bound = L(3.Ok(), 4.Ok()).ToAsync().SelectResultAsync(x => x > 3 ? x.Ok() : new Leaf(x).Fail<int>());
            IAsyncEnumerable<Result<int, string>> typed = L(3.Ok<int, string>()).ToAsync().SelectResultAsync(async (x, ct) => { await Task.Delay(1, ct); return "no".Fail<int, string>(); });

            Assert.That(await Drain(bound), Is.EqualTo(L(new Leaf(3).Fail<int>(), 4.Ok())));
            Assert.That(await Drain(typed), Is.EqualTo(L("no".Fail<int, string>())));
        }

        [Test]
        public async Task CollectAsync_stops_pulling_at_the_first_failure_and_disposes_the_source()
        {
            var producer = new Producer<Result<int>>(Enumerable.Range(0, 100).Select(i => i == 4 ? new Leaf(i) : i.Ok()));
            var collected = await producer.Read().CollectAsync(CancellationToken.None);
            Assert.That(collected.Error, Is.EqualTo(new Leaf(4)));
            Assert.That((producer.Pulls, producer.Completed, producer.FinallyRan), Is.EqualTo((5, false, true)));

            var typed = new Producer<Result<int, string>>(Enumerable.Range(0, 100).Select(i => i == 2 ? "stop".Fail<int, string>() : i.Ok<int, string>()));
            Assert.That((await typed.Read().CollectAsync(CancellationToken.None)).Error, Is.EqualTo("stop"));
            Assert.That((typed.Pulls, typed.FinallyRan), Is.EqualTo((3, true)));
        }

        [Test]
        public async Task CollectAllAsync_reads_every_item_and_keeps_every_failure_in_order()
        {
            var producer = new Producer<Result<int>>([1, new Leaf(2), 3, new Leaf(4)]);
            var all = await producer.Read().CollectAllAsync(CancellationToken.None);
            Assert.That(all.Errors, Is.EqualTo(L<Error>(new Leaf(2), new Leaf(4))));
            Assert.That((producer.Pulls, producer.Completed), Is.EqualTo((4, true)));

            var ok = await L(1.Ok<int, string>(), 2.Ok<int, string>()).ToAsync().CollectAllAsync(CancellationToken.None);
            Assert.That(ok.Value, Is.EqualTo(L(1, 2)));
            var empty = await Array.Empty<Result<int>>().ToAsync().CollectAllAsync(CancellationToken.None);
            Assert.That(empty.Value, Is.Empty);
        }

        [Test]
        public async Task Successes_and_Failures_are_lazy()
        {
            var producer = new Producer<Result<int>>([new Leaf(1), new Leaf(2), 3, 4, 5]);
            await foreach (var x in producer.Read().Successes())
            {
                Assert.That(x, Is.EqualTo(3));
                break;
            }

            Assert.That((producer.Pulls, producer.FinallyRan), Is.EqualTo((3, true)));

            var typed = new Producer<Result<int, string>>([1.Ok<int, string>(), "a".Fail<int, string>(), 3.Ok<int, string>()]);
            await foreach (var e in typed.Read().Failures())
            {
                Assert.That(e, Is.EqualTo("a"));
                break;
            }

            Assert.That((typed.Pulls, typed.FinallyRan), Is.EqualTo((2, true)));
        }

        // ---- Cancellation ----

        [Test]
        public async Task The_enumerator_token_reaches_the_source_and_the_selector()
        {
            using var cts = new CancellationTokenSource();
            var producer = new Producer<Result<int>>([1, 2]);
            var seen = new List<CancellationToken>();
            await Drain(producer.Read().SelectResultAsync((x, ct) => { seen.Add(ct); return new ValueTask<int>(x); }), cts.Token);

            Assert.That(producer.Token, Is.EqualTo(cts.Token));
            Assert.That(seen, Is.EqualTo(L(cts.Token, cts.Token)));

            var successes = new Producer<Result<int>>([1]);
            await Drain(successes.Read().Successes(), cts.Token);
            Assert.That(successes.Token, Is.EqualTo(cts.Token));

            var failures = new Producer<Result<int, string>>(["a".Fail<int, string>()]);
            await Drain(failures.Read().Failures(), cts.Token);
            Assert.That(failures.Token, Is.EqualTo(cts.Token));
        }

        [Test]
        public async Task Cancelling_mid_stream_surfaces_OperationCanceledException_and_disposes_the_source()
        {
            using var cts = new CancellationTokenSource();
            var producer = new Producer<Result<int>>(Enumerable.Range(0, 100).Select(i => i.Ok()));
            var count = 0;
            Assert.That(async () =>
            {
                await foreach (var _ in producer.Read().SelectResultAsync(x => x).WithCancellation(cts.Token))
                    if (++count == 2) await cts.CancelAsync();
            }, Throws.InstanceOf<OperationCanceledException>());

            Assert.That((producer.Pulls, producer.FinallyRan), Is.EqualTo((2, true)));
        }

        [Test]
        public void A_cancelled_token_stops_every_operator_even_when_the_stream_ignores_it()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var ignoring = new[] { 1.Ok(), 2.Ok() };
            var typed = new[] { 1.Ok<int, string>() };

            Assert.That(async () => await Drain(ignoring.ToAsync().SelectResultAsync(x => x), cts.Token), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(async () => await Drain(typed.ToAsync().SelectResultAsync(x => x.Ok<int, string>()), cts.Token), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(async () => await Drain(ignoring.ToAsync().Successes(), cts.Token), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(async () => await Drain(typed.ToAsync().Failures(), cts.Token), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(async () => await ignoring.ToAsync().CollectAllAsync(cts.Token), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(async () => await typed.ToAsync().CollectAllAsync(cts.Token), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(async () => await L(1).ToAsync().TraverseAsync((x, _) => new ValueTask<Result<int>>(x), 2, true, cts.Token), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(async () => await L(1).ToAsync().TraverseAllAsync((x, _) => new ValueTask<Result<int, string>>(x), 2, true, cts.Token), Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public async Task Cancelling_a_traversal_cancels_the_work_in_flight_and_the_source()
        {
            using var cts = new CancellationTokenSource();
            var producer = new Producer<int>(Enumerable.Range(0, 100));
            var started = 0;
            var cancelled = 0;
            var traversal = producer.Read().TraverseAsync(async (x, ct) =>
            {
                Interlocked.Increment(ref started);
                try { await Task.Delay(Timeout.Infinite, ct); }
                catch (OperationCanceledException) { Interlocked.Increment(ref cancelled); throw; }
                return x.Ok();
            }, 3, true, cts.Token).AsTask();

            while (Volatile.Read(ref started) < 3) await Task.Delay(1);
            await cts.CancelAsync();

            var oce = Assert.CatchAsync<OperationCanceledException>(async () => await traversal);
            Assert.That(oce!.CancellationToken, Is.EqualTo(cts.Token), "the caller's token, not the linked one");
            Assert.That((started, cancelled, producer.Pulls, producer.FinallyRan), Is.EqualTo((3, 3, 3, true)));
            Assert.That(producer.Token.IsCancellationRequested, Is.True, "the source got the linked token");
        }

        // ---- Concurrency ----

        [Test]
        public async Task TraverseAsync_runs_at_most_maxConcurrency_calls_at_once()
        {
            foreach (var max in L(1, 2, 5))
            {
                // The first calls wait until max of them are in: the peak reaches max exactly when the limit is right.
                var current = 0;
                var peak = 0;
                var allIn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var traversed = await Enumerable.Range(0, 20).ToAsync().TraverseAsync(async (x, ct) =>
                {
                    var now = Interlocked.Increment(ref current);
                    InterlockedMax(ref peak, now);
                    if (now == max) allIn.TrySetResult();
                    await allIn.Task.WaitAsync(ct);
                    await Task.Yield();
                    Interlocked.Decrement(ref current);
                    return x.Ok();
                }, max, true, CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(10));

                Assert.That(traversed.Value, Is.EqualTo(Enumerable.Range(0, 20)));
                Assert.That(peak, Is.EqualTo(max));
            }

            static void InterlockedMax(ref int target, int value)
            {
                int seen;
                while ((seen = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, seen) != seen) { }
            }
        }

        [Test]
        public async Task preserveOrder_chooses_between_the_source_order_and_the_completion_order()
        {
            // A chain of gates, driven by the traversal's own reads (max 2): item 1 and 2 finish at once; reading item 3
            // opens item 0's gate; reading past the end opens item 3's. A read happens only after a call was handled, so
            // the completion order is 1, 2, 0, 3 every time.
            TaskCompletionSource zero = null!, three = null!;
            async IAsyncEnumerable<int> Source()
            {
                await Task.CompletedTask;
                yield return 0;
                yield return 1;
                yield return 2;
                zero.SetResult();
                yield return 3;
                three.SetResult();
            }

            async ValueTask<Result<int, string>> Run(int x, CancellationToken ct)
            {
                if (x == 0) await zero.Task.WaitAsync(ct);
                if (x == 3) await three.Task.WaitAsync(ct);
                return x.Ok<int, string>();
            }

            async Task<(IReadOnlyList<int> Values, IReadOnlyList<string> Errors)> Traverse(Func<int, CancellationToken, ValueTask<Result<int, string>>> f, bool order)
            {
                zero = new(TaskCreationOptions.RunContinuationsAsynchronously);
                three = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var all = await Source().TraverseAllAsync(f, 2, order, CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
                return all.TryGetValue(out var values) ? (values, []) : ([], all.Errors);
            }

            Assert.That((await Traverse(Run, true)).Values, Is.EqualTo(L(0, 1, 2, 3)));
            Assert.That((await Traverse(Run, false)).Values, Is.EqualTo(L(1, 2, 0, 3)));

            async ValueTask<Result<int, string>> Fail(int x, CancellationToken ct) { await Run(x, ct); return $"e{x}".Fail<int, string>(); }
            Assert.That((await Traverse(Fail, true)).Errors, Is.EqualTo(L("e0", "e1", "e2", "e3")));
            Assert.That((await Traverse(Fail, false)).Errors, Is.EqualTo(L("e1", "e2", "e0", "e3")));
        }

        [Test]
        public async Task A_failure_waits_for_earlier_items_cancels_later_ones_stops_reading_and_disposes_the_source()
        {
            var producer = new Producer<int>(Enumerable.Range(0, 1000));
            var cancelled = 0;
            var earlierSawCancellation = new List<bool>();
            var fourthStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var failing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var traversed = await producer.Read().TraverseAsync(async (x, ct) =>
            {
                // Item 2 fails once item 3's call is running: all four slots are in use when the failure lands.
                if (x == 2)
                {
                    await fourthStarted.Task;
                    failing.SetResult();
                    return new Leaf(x).Fail<int>();
                }

                if (x < 2)
                {
                    // Earlier items finish after the failure, and are never cancelled for it.
                    await failing.Task;
                    await Task.Delay(20, CancellationToken.None);
                    lock (earlierSawCancellation) earlierSawCancellation.Add(ct.IsCancellationRequested);
                    return x.Ok();
                }

                fourthStarted.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, ct); }
                catch (OperationCanceledException) { Interlocked.Increment(ref cancelled); throw; }
                return x.Ok();
            }, 4, true, CancellationToken.None);

            Assert.That(traversed.Error, Is.EqualTo(new Leaf(2)));
            Assert.That(earlierSawCancellation, Is.EqualTo(L(false, false)));
            Assert.That((cancelled, producer.Pulls, producer.Completed, producer.FinallyRan), Is.EqualTo((1, 4, false, true)));
        }

        [Test]
        public async Task An_earlier_item_that_fails_later_wins_over_a_later_item_that_failed_first()
        {
            var traversed = await Enumerable.Range(0, 3).ToAsync().TraverseAsync(async (x, ct) =>
            {
                await Task.Delay(x == 0 ? 40 : 1, CancellationToken.None);
                return x == 1 ? x.Ok() : new Leaf(x).Fail<int>();
            }, 3, false, CancellationToken.None);
            Assert.That(traversed.Error, Is.EqualTo(new Leaf(0)));
        }

        [Test]
        public async Task A_synchronous_source_and_selector_stop_at_the_first_failure_without_more_calls()
        {
            foreach (var max in L(1, 4, 16))
            {
                var calls = 0;
                var traversed = await Enumerable.Range(0, 100).ToAsyncEnumerable().TraverseAsync((x, _) =>
                {
                    calls++;
                    return new ValueTask<Result<int>>(x == 2 ? new Leaf(x).Fail<int>() : x.Ok());
                }, max, true, CancellationToken.None);
                Assert.That((traversed.Error, calls), Is.EqualTo(((Error)new Leaf(2), 3)));
            }
        }

        [Test]
        public async Task A_selector_that_ignores_its_token_is_waited_for()
        {
            var finished = false;
            var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var traversed = await Enumerable.Range(0, 2).ToAsync().TraverseAsync(async (x, _) =>
            {
                // Item 0 fails only once item 1's call is running, so there is a call in flight to wait for.
                if (x == 0) { await secondStarted.Task; return "first".Fail<int, string>(); }
                secondStarted.SetResult();
                await Task.Delay(60, CancellationToken.None);
                finished = true;
                return x.Ok<int, string>();
            }, 2, true, CancellationToken.None);
            Assert.That((traversed.Error, finished), Is.EqualTo(("first", true)));
        }

        [Test]
        public void A_source_that_throws_while_calls_are_in_flight_cancels_them_and_propagates()
        {
            var boom = new Boom();
            var cancelled = 0;
            async IAsyncEnumerable<int> Source([EnumeratorCancellation] CancellationToken ct = default)
            {
                yield return 0;
                yield return 1;
                await Task.Delay(20, ct);
                throw boom;
            }

            Assert.That(Catch(() => Source().TraverseAsync(async (x, ct) =>
            {
                try { await Task.Delay(Timeout.Infinite, ct); }
                catch (OperationCanceledException) { Interlocked.Increment(ref cancelled); throw; }
                return x.Ok();
            }, 4, true, CancellationToken.None).AsTask()), Is.SameAs(boom));
            Assert.That(cancelled, Is.EqualTo(2));
        }

        [Test]
        public async Task Empty_single_and_all_failure_inputs()
        {
            var none = Array.Empty<int>();
            Assert.That((await none.ToAsync().TraverseAsync((x, _) => Later(x.Ok()), 2, true, CancellationToken.None)).Value, Is.Empty);
            Assert.That((await none.ToAsync().TraverseAllAsync((x, _) => Later(x.Ok<int, string>()), 2, false, CancellationToken.None)).Value, Is.Empty);
            Assert.That((await Array.Empty<Result<int, string>>().ToAsync().CollectAllAsync(CancellationToken.None)).Value, Is.Empty);

            Assert.That((await L(7).ToAsync().TraverseAsync((x, _) => Later(x.Ok<int, string>()), 3, false, CancellationToken.None)).Value, Is.EqualTo(L(7)));
            Assert.That((await L(7).ToAsync().TraverseAllAsync((x, _) => Later(new Leaf(x).Fail<int>()), 3, true, CancellationToken.None)).Errors, Is.EqualTo(L<Error>(new Leaf(7))));

            var all = L(1, 2, 3);
            Assert.That((await all.ToAsync().TraverseAsync((x, _) => Later(new Leaf(x).Fail<int>()), 3, false, CancellationToken.None)).Error, Is.EqualTo(new Leaf(1)));
            Assert.That((await all.ToAsync().TraverseAllAsync((x, _) => Later($"e{x}".Fail<int, string>()), 3, true, CancellationToken.None)).Errors, Is.EqualTo(L("e1", "e2", "e3")));
            Assert.That((await all.Select(x => $"e{x}".Fail<int, string>()).ToAsync().CollectAllAsync(CancellationToken.None)).Errors, Is.EqualTo(L("e1", "e2", "e3")));
        }

        [Test]
        public async Task A_large_stream_with_a_high_concurrency_limit()
        {
            // Smoke test: each completion scans the calls in flight (O(maxConcurrency)), so 20k items at 1000 wide is
            // about 20M cheap checks: it should finish in seconds, in order.
            var traversed = await Enumerable.Range(0, 20_000).ToAsyncEnumerable().TraverseAsync(async (x, _) => { await Task.Yield(); return x.Ok(); }, 1000, true, CancellationToken.None)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(60));
            Assert.That(traversed.Value, Is.EqualTo(Enumerable.Range(0, 20_000)));
        }

        [Test]
        public async Task A_failure_while_a_read_is_pending_ends_the_traversal_without_waiting_for_the_source()
        {
            // The source hands out one item, then waits forever (until cancelled) for the next.
            var producer = new Producer<int>(Enumerable.Range(0, 10), (i, ct) => i == 0 ? Task.CompletedTask : Task.Delay(Timeout.Infinite, ct));
            var traversed = producer.Read().TraverseAsync(async (x, ct) => { await Task.Delay(10, ct); return "bad".Fail<int, string>(); }, 2, false, CancellationToken.None).AsTask();

            Assert.That(await traversed.WaitAsync(TimeSpan.FromSeconds(10)), Is.EqualTo("bad".Fail<IReadOnlyList<int>, string>()).Using<Result<IReadOnlyList<int>, string>>((a, b) => a.IsFailure && b.IsFailure && a.Error == b.Error));
            Assert.That((producer.Pulls, producer.FinallyRan), Is.EqualTo((1, true)));
        }

        [Test]
        public async Task TraverseAllAsync_runs_every_call_after_a_failure()
        {
            var calls = 0;
            var all = await Enumerable.Range(0, 6).ToAsync().TraverseAllAsync(async (x, ct) =>
            {
                Interlocked.Increment(ref calls);
                await Task.Delay(1, ct);
                return x % 2 == 0 ? new Leaf(x).Fail<int>() : x.Ok();
            }, 2, true, CancellationToken.None);

            Assert.That(calls, Is.EqualTo(6));
            Assert.That(all.Errors, Is.EqualTo(L<Error>(new Leaf(0), new Leaf(2), new Leaf(4))));
            var ok = await Enumerable.Range(0, 3).ToAsync().TraverseAllAsync((x, _) => new ValueTask<Result<int>>(x * 2), 3, true, CancellationToken.None);
            Assert.That(ok.Value, Is.EqualTo(L(0, 2, 4)));
        }

        // ---- Exceptions stay exceptions ----

        [Test]
        public void A_producer_exception_propagates_unchanged_from_every_operator()
        {
            var boom = new Boom();
            Assert.That(Catch(() => Drain(ThrowsAfter(2, boom).SelectResultAsync(x => x))), Is.SameAs(boom));
            Assert.That(Catch(() => Drain(ThrowsAfter(2, boom).SelectResultAsync((x, _) => Later(x.Ok())))), Is.SameAs(boom));
            Assert.That(Catch(() => Drain(ThrowsAfter(2, boom).Successes())), Is.SameAs(boom));
            Assert.That(Catch(() => Drain(ThrowsAfter(2, boom).Failures())), Is.SameAs(boom));
            Assert.That(Catch(() => ThrowsAfter(2, boom).CollectAllAsync(CancellationToken.None).AsTask()), Is.SameAs(boom));
            Assert.That(Catch(() => ThrowsAfter(2, boom).Successes().TraverseAsync((x, _) => Later(x.Ok()), 2, true, CancellationToken.None).AsTask()), Is.SameAs(boom));
            Assert.That(Catch(() => ThrowsAfter(2, boom).Successes().TraverseAllAsync((x, _) => Later(x.Ok<int, string>()), 2, true, CancellationToken.None).AsTask()), Is.SameAs(boom));
        }

        [Test]
        public void A_selector_exception_propagates_and_is_never_a_failure()
        {
            var boom = new Boom();
            var producer = new Producer<int>(Enumerable.Range(0, 100));
            Assert.That(Catch(() => L(1.Ok()).ToAsync().SelectResultAsync<int, int>(_ => throw boom).ToListAsync().AsTask()), Is.SameAs(boom));
            Assert.That(Catch(() => L(1.Ok<int, string>()).ToAsync().SelectResultAsync<int, string, int>((_, _) => throw boom).ToListAsync().AsTask()), Is.SameAs(boom));
            Assert.That(Catch(() => producer.Read().TraverseAsync<int, int>((x, _) => x == 3 ? throw boom : Later(x.Ok()), 2, true, CancellationToken.None).AsTask()), Is.SameAs(boom));
            Assert.That(producer.FinallyRan, Is.True);
            Assert.That(Catch(() => L(1).ToAsync().TraverseAllAsync<int, int, string>(async (_, _) => { await Task.Yield(); throw boom; }, 1, true, CancellationToken.None).AsTask()), Is.SameAs(boom));
        }

        static T[] L<T>(params T[] items) => items;

        static Exception? Catch(Func<Task> run)
        {
            try { run().GetAwaiter().GetResult(); }
            catch (Exception e) { return e; }
            return null;
        }

        [Test]
        public void Arguments_are_checked_when_the_operator_is_called()
        {
            IAsyncEnumerable<Result<int>> none = null!;
            IAsyncEnumerable<Result<int, string>> typedNone = null!;
            IAsyncEnumerable<int> itemsNone = null!;
            var some = L(1.Ok()).ToAsync();
            var items = L(1).ToAsync();

            Assert.Throws<ArgumentNullException>(() => none.SelectResultAsync(x => x));
            Assert.Throws<ArgumentNullException>(() => typedNone.SelectResultAsync(x => x.Ok<int, string>()));
            Assert.Throws<ArgumentNullException>(() => some.SelectResultAsync((Func<int, int>)null!));
            Assert.Throws<ArgumentNullException>(() => some.SelectResultAsync((Func<int, CancellationToken, ValueTask<Result<int>>>)null!));
            Assert.Throws<ArgumentNullException>(() => none.Successes());
            Assert.Throws<ArgumentNullException>(() => typedNone.Failures());
            Assert.Throws<ArgumentNullException>(() => none.CollectAllAsync(CancellationToken.None).AsTask());
            Assert.Throws<ArgumentNullException>(() => itemsNone.TraverseAsync((x, _) => new ValueTask<Result<int>>(x), 1, true, CancellationToken.None).AsTask());
            Assert.Throws<ArgumentNullException>(() => items.TraverseAllAsync((Func<int, CancellationToken, ValueTask<Result<int, string>>>)null!, 1, true, CancellationToken.None).AsTask());
            Assert.That(() => items.TraverseAsync((x, _) => new ValueTask<Result<int>>(x), 0, true, CancellationToken.None).AsTask(), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => items.TraverseAllAsync((x, _) => new ValueTask<Result<int>>(x), -1, true, CancellationToken.None).AsTask(), Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        // ---- The scenario: a queue worker ----

        sealed record Malformed(string Raw) : Error("the message is malformed");
        sealed record OutOfStock(string Sku) : Error($"{Sku} is out of stock");
        sealed record Reservation(string Sku, int Quantity, string Id);

        sealed class Warehouse
        {
            public readonly List<Reservation> Published = [];
            public readonly List<Error> DeadLetters = [];

            // Validation: a message is "sku:quantity".
            public static Result<(string Sku, int Quantity)> Parse(string raw) =>
                raw.Split(':') is [var sku, var q] && int.TryParse(q, out var quantity) && quantity > 0
                    ? (sku, quantity)
                    : new Malformed(raw);

            // A domain error from an async store.
            public static async ValueTask<Result<Reservation>> ReserveAsync((string Sku, int Quantity) line, CancellationToken ct)
            {
                await Task.Delay(1, ct);
                return line.Sku.StartsWith("gone", StringComparison.Ordinal) ? new OutOfStock(line.Sku) : new Reservation(line.Sku, line.Quantity, $"r-{line.Sku}");
            }

            public async Task RunAsync(ChannelReader<string> queue, CancellationToken ct)
            {
                // In-box async LINQ for the plain step, Resultful's for the result-aware one.
                await foreach (var r in queue.ReadAllAsync(ct).Select(Parse).SelectResultAsync(ReserveAsync).WithCancellation(ct))
                {
                    // 24b adds r.Failed(out var reservation, out var error); TryGetValue and Error say the same today.
                    if (!r.TryGetValue(out var reservation))
                    {
                        DeadLetters.Add(r.Error);
                        continue;
                    }

                    Published.Add(reservation);
                }
            }
        }

        [Test]
        public async Task A_worker_reads_a_channel_dead_letters_the_failures_and_publishes_the_rest()
        {
            var queue = Channel.CreateUnbounded<string>();
            var warehouse = new Warehouse();
            using var shutdown = new CancellationTokenSource();
            var worker = warehouse.RunAsync(queue.Reader, shutdown.Token);

            foreach (var message in L("teapot:2", "nonsense", "gone-mug:1", "kettle:1", "cup:0"))
                await queue.Writer.WriteAsync(message);
            queue.Writer.Complete();
            await worker.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.That(warehouse.Published, Is.EqualTo(L(new Reservation("teapot", 2, "r-teapot"), new Reservation("kettle", 1, "r-kettle"))));
            Assert.That(warehouse.DeadLetters, Is.EqualTo(L<Error>(new Malformed("nonsense"), new OutOfStock("gone-mug"), new Malformed("cup:0"))));
            // The dead letters' messages carry nothing from the raw message: only an out-of-stock SKU, by design.
            Assert.That(warehouse.DeadLetters.Select(e => e.Message), Is.EqualTo(L("the message is malformed", "gone-mug is out of stock", "the message is malformed")));
        }

        [Test]
        public async Task Shutting_the_worker_down_cancels_the_wait_on_the_channel()
        {
            var queue = Channel.CreateUnbounded<string>();
            var warehouse = new Warehouse();
            using var shutdown = new CancellationTokenSource();
            var worker = warehouse.RunAsync(queue.Reader, shutdown.Token);
            await queue.Writer.WriteAsync("teapot:1");
            while (warehouse.Published.Count == 0) await Task.Delay(1);

            await shutdown.CancelAsync();
            Assert.That(async () => await worker.WaitAsync(TimeSpan.FromSeconds(10)), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(warehouse.Published, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task A_batch_import_traverses_with_a_concurrency_limit_and_reports_every_bad_line()
        {
            string[] lines = ["teapot:2", "gone-mug:1", "kettle:x", "cup:3"];
            var all = await lines.ToAsync().TraverseAllAsync(
                (raw, ct) => Warehouse.Parse(raw).TryGetValue(out var line) ? Warehouse.ReserveAsync(line, ct) : new ValueTask<Result<Reservation>>(new Malformed(raw)),
                maxConcurrency: 2, preserveOrder: true, CancellationToken.None);
            Assert.That(all.Errors, Is.EqualTo(L<Error>(new OutOfStock("gone-mug"), new Malformed("kettle:x"))));

            var good = await lines.Where(l => !l.StartsWith("gone", StringComparison.Ordinal) && !l.Contains('x')).ToAsync()
                .TraverseAsync((raw, ct) => Warehouse.ReserveAsync(Warehouse.Parse(raw).Value, ct), 2, true, CancellationToken.None);
            Assert.That(good.Value.Select(r => r.Id), Is.EqualTo(L("r-teapot", "r-cup")));
        }
    }
}
