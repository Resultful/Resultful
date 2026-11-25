using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using NUnit.Framework;
using Resultful.LINQ;

namespace Resultful.Tests
{
    // Decision 22 item 3. Query syntax with async steps: the laws hold every query form (sync, Task, ValueTask, and any
    // mix of them) to the Bind chain it stands for, on random results. Every async query is a Task. This project builds
    // at AnalysisLevel latest-recommended with warnings as errors, so these ValueTask queries also prove CA2012 has
    // nothing to report in a consumer's code. Then the rules: a failure starts nothing after
    // it, exceptions and cancellation propagate unchanged, the zero value throws, and System.Linq's IEnumerable queries in
    // the same file still bind to System.Linq. Result.Ensure is the step for a condition. The scenario is the decision's
    // receipt query.
    [TestFixture]
    public class ComprehensionTests
    {
        sealed record Leaf(int Code) : Error($"leaf {Code}");

        static readonly Gen<Result<int>> Results = Gen.Frequency(
            (2, Gen.Int[-50, 50].Select(v => v.Ok())),
            (1, Gen.Int[0, 3].Select(c => new Leaf(c).Fail<int>())));

        static readonly Gen<Result<int, string>> Typed = Gen.Frequency(
            (2, Gen.Int[-50, 50].Select(v => v.Ok<int, string>())),
            (1, Gen.OneOf("a", "b", "c").Select(e => e.Fail<int, string>())));

        // A step that depends on the value before it, so the order of binding is visible.
        static Result<int> Step(int x, Result<int> r) => r.Map(v => v * 3 + x);
        static Result<int, string> Step(int x, Result<int, string> r) => r.Map(v => v * 3 + x);

        static async Task<T> LaterTask<T>(T value)
        {
            await Task.Yield();
            return value;
        }

        static async ValueTask<T> LaterValue<T>(T value)
        {
            await Task.Yield();
            return value;
        }

        // ---- Laws: a query equals its Bind chain ----

        [Test]
        public async Task Every_query_form_over_Result_equals_the_Bind_chain()
        {
            await Gen.Select(Results, Results, Results).SampleAsync(async t =>
            {
                var (r1, r2, r3) = t;
                var chain = r1.Bind(a => Step(a, r2).Bind(b => Step(b, r3).Map(c => a + b + c)));

                var sync = from a in r1 from b in Step(a, r2) from c in Step(b, r3) select a + b + c;
                var allTask = from a in LaterTask(r1) from b in LaterTask(Step(a, r2)) from c in LaterTask(Step(b, r3)) select a + b + c;
                var syncThenTask = from a in r1 from b in LaterTask(Step(a, r2)) from c in Step(b, r3) select a + b + c;
                var taskThenSync = from a in LaterTask(r1) from b in Step(a, r2) let d = b from c in Step(d, r3) select a + b + c;
                var allValue = from a in LaterValue(r1) from b in LaterValue(Step(a, r2)) from c in LaterValue(Step(b, r3)) select a + b + c;
                var syncThenValue = from a in r1 from b in LaterValue(Step(a, r2)) from c in Step(b, r3) select a + b + c;
                var valueThenSync = from a in LaterValue(r1) from b in Step(a, r2) let d = b from c in Step(d, r3) select a + b + c;

                Assert.That(sync, Is.EqualTo(chain));
                Assert.That(await allTask, Is.EqualTo(chain));
                Assert.That(await syncThenTask, Is.EqualTo(chain));
                Assert.That(await taskThenSync, Is.EqualTo(chain));
                Assert.That(await allValue, Is.EqualTo(chain));
                Assert.That(await syncThenValue, Is.EqualTo(chain));
                Assert.That(await valueThenSync, Is.EqualTo(chain));
            });
        }

        [Test]
        public async Task Every_query_form_over_typed_results_equals_the_Bind_chain()
        {
            await Gen.Select(Typed, Typed, Typed).SampleAsync(async t =>
            {
                var (r1, r2, r3) = t;
                var chain = r1.Bind(a => Step(a, r2).Bind(b => Step(b, r3).Map(c => a + b + c)));

                var allTask = from a in LaterTask(r1) from b in LaterTask(Step(a, r2)) from c in LaterTask(Step(b, r3)) select a + b + c;
                var syncThenTask = from a in r1 from b in LaterTask(Step(a, r2)) from c in Step(b, r3) select a + b + c;
                var taskThenSync = from a in LaterTask(r1) from b in Step(a, r2) let d = b from c in Step(d, r3) select a + b + c;
                var allValue = from a in LaterValue(r1) from b in LaterValue(Step(a, r2)) from c in LaterValue(Step(b, r3)) select a + b + c;
                var syncThenValue = from a in r1 from b in LaterValue(Step(a, r2)) from c in Step(b, r3) select a + b + c;
                var valueThenSync = from a in LaterValue(r1) from b in Step(a, r2) let d = b from c in Step(d, r3) select a + b + c;

                Assert.That(await allTask, Is.EqualTo(chain));
                Assert.That(await syncThenTask, Is.EqualTo(chain));
                Assert.That(await taskThenSync, Is.EqualTo(chain));
                Assert.That(await allValue, Is.EqualTo(chain));
                Assert.That(await syncThenValue, Is.EqualTo(chain));
                Assert.That(await valueThenSync, Is.EqualTo(chain));
            });
        }

        [Test]
        public async Task Select_and_two_argument_SelectMany_equal_Map_and_Bind()
        {
            await Gen.Select(Results, Results, Gen.Bool).SampleAsync(async t =>
            {
                var (r1, r2, later) = t;
                Task<Result<int>> T(Result<int> r) => later ? LaterTask(r) : Task.FromResult(r);
                ValueTask<Result<int>> V(Result<int> r) => later ? LaterValue(r) : new ValueTask<Result<int>>(r);
                var bound = r1.Bind(a => Step(a, r2));

                Assert.That(await T(r1).Select(a => a - 1), Is.EqualTo(r1.Map(a => a - 1)));
                Assert.That(await V(r1).Select(a => a - 1), Is.EqualTo(r1.Map(a => a - 1)));
                Assert.That(await T(r1).SelectMany(a => Step(a, r2)), Is.EqualTo(bound));
                Assert.That(await T(r1).SelectMany(a => T(Step(a, r2))), Is.EqualTo(bound));
                Assert.That(await r1.SelectMany(a => T(Step(a, r2))), Is.EqualTo(bound));
                Assert.That(await V(r1).SelectMany(a => Step(a, r2)), Is.EqualTo(bound));
                Assert.That(await V(r1).SelectMany(a => V(Step(a, r2))), Is.EqualTo(bound));
                Assert.That(await r1.SelectMany(a => V(Step(a, r2))), Is.EqualTo(bound));
            });
            await Gen.Select(Typed, Typed, Gen.Bool).SampleAsync(async t =>
            {
                var (r1, r2, later) = t;
                Task<Result<int, string>> T(Result<int, string> r) => later ? LaterTask(r) : Task.FromResult(r);
                ValueTask<Result<int, string>> V(Result<int, string> r) => later ? LaterValue(r) : new ValueTask<Result<int, string>>(r);
                var bound = r1.Bind(a => Step(a, r2));

                Assert.That(await T(r1).Select(a => a - 1), Is.EqualTo(r1.Map(a => a - 1)));
                Assert.That(await V(r1).Select(a => a - 1), Is.EqualTo(r1.Map(a => a - 1)));
                Assert.That(await T(r1).SelectMany(a => Step(a, r2)), Is.EqualTo(bound));
                Assert.That(await T(r1).SelectMany(a => T(Step(a, r2))), Is.EqualTo(bound));
                Assert.That(await r1.SelectMany(a => T(Step(a, r2))), Is.EqualTo(bound));
                Assert.That(await V(r1).SelectMany(a => Step(a, r2)), Is.EqualTo(bound));
                Assert.That(await V(r1).SelectMany(a => V(Step(a, r2))), Is.EqualTo(bound));
                Assert.That(await r1.SelectMany(a => V(Step(a, r2))), Is.EqualTo(bound));
            });
        }

        // ---- A failure starts nothing after it ----

        [Test]
        public async Task A_failed_step_starts_no_later_async_step()
        {
            var started = 0;
            Task<Result<int>> Later(int x) { started++; return LaterTask(x.Ok()); }
            ValueTask<Result<int>> LaterV(int x) { started++; return LaterValue(x.Ok()); }
            Task<Result<int, string>> LaterTyped(int x) { started++; return LaterTask(x.Ok<int, string>()); }
            ValueTask<Result<int, string>> LaterTypedV(int x) { started++; return LaterValue(x.Ok<int, string>()); }
            var failed = new Leaf(1).Fail<int>();
            var typedFailed = "no".Fail<int, string>();

            var results = new List<object>
            {
                await (from a in failed from b in Later(a) select b),
                await (from a in LaterTask(failed) from b in Later(a) from c in Later(b) select c),
                await (from a in LaterTask(1.Ok()) from b in failed from c in Later(b) select c),
                await (from a in failed from b in LaterV(a) select b),
                await (from a in LaterValue(failed) from b in LaterV(a) from c in LaterV(b) select c),
                await (from a in LaterValue(1.Ok()) from b in failed from c in LaterV(b) select c),
            };
            var typed = new List<object>
            {
                await (from a in typedFailed from b in LaterTyped(a) select b),
                await (from a in LaterTask(typedFailed) from b in LaterTyped(a) select b),
                await (from a in typedFailed from b in LaterTypedV(a) select b),
                await (from a in LaterValue(typedFailed) from b in LaterTypedV(a) select b),
            };

            Assert.That(started, Is.Zero);
            Assert.That(results, Is.All.EqualTo(failed));
            Assert.That(typed, Is.All.EqualTo(typedFailed));
        }

        [Test]
        public async Task Each_async_step_starts_only_after_the_one_before_it_succeeded()
        {
            var log = new List<string>();
            async Task<Result<int>> Load(int x) { log.Add($"start {x}"); await Task.Yield(); log.Add($"end {x}"); return x; }

            var result = await (from a in Load(1) from b in Load(a + 1) from c in Load(b + 1) select a + b + c);

            Assert.That(result.Value, Is.EqualTo(6));
            Assert.That(log, Is.EqualTo(new List<string> { "start 1", "end 1", "start 2", "end 2", "start 3", "end 3" }));
        }

        [Test]
        public async Task Task_and_ValueTask_steps_mix_in_one_query()
        {
            await Gen.Select(Results, Results, Results).SampleAsync(async t =>
            {
                var (r1, r2, r3) = t;
                var chain = r1.Bind(a => Step(a, r2).Bind(b => Step(b, r3).Map(c => a + b + c)));
                Task<Result<int>> mixed = from a in LaterValue(r1) from b in LaterTask(Step(a, r2)) from c in LaterValue(Step(b, r3)) select a + b + c;

                Assert.That(await mixed, Is.EqualTo(chain));
            });
            await Gen.Select(Typed, Typed, Typed).SampleAsync(async t =>
            {
                var (r1, r2, r3) = t;
                var chain = r1.Bind(a => Step(a, r2).Bind(b => Step(b, r3).Map(c => a + b + c)));
                Task<Result<int, string>> mixed = from a in LaterTask(r1) from b in LaterValue(Step(a, r2)) from c in LaterTask(Step(b, r3)) select a + b + c;

                Assert.That(await mixed, Is.EqualTo(chain));
            });
        }

        // A hand-written shape: the generator's are tested in Resultful.Generators.Tests.
        readonly struct Shape(Result<int, string> inner) : IResultShape<int, string>
        {
            public Result<int, string> AsResult() => inner;
        }

        [Test]
        public async Task A_shape_is_a_step_anywhere_in_a_query()
        {
            await Gen.Select(Typed, Typed, Typed).SampleAsync(async t =>
            {
                var (r1, r2, r3) = t;
                var chain = r1.Bind(a => Step(a, r2).Bind(b => Step(b, r3).Map(c => a + b + c)));
                IResultShape<int, string> S(Result<int, string> r) => new Shape(r);

                Assert.That(from a in S(r1) from b in S(Step(a, r2)) from c in S(Step(b, r3)) select a + b + c, Is.EqualTo(chain));
                Assert.That(from a in r1 from b in S(Step(a, r2)) from c in Step(b, r3) select a + b + c, Is.EqualTo(chain));
                Assert.That(await (from a in S(r1) from b in LaterTask(Step(a, r2)) from c in S(Step(b, r3)) select a + b + c), Is.EqualTo(chain));
                Assert.That(await (from a in S(r1) from b in LaterValue(Step(a, r2)) from c in Step(b, r3) select a + b + c), Is.EqualTo(chain));
            });
        }

        [Test]
        public void Shape_steps_check_their_arguments_and_start_nothing_after_a_failure()
        {
            IResultShape<int, string> ok = new Shape(1.Ok<int, string>());
            var calls = 0;
            IResultShape<int, string> Next(int x) { calls++; return new Shape(x.Ok<int, string>()); }
            var failed = from a in new Shape("no".Fail<int, string>()) from b in Next(a) select b;

            Assert.That((failed.Error, calls), Is.EqualTo(("no", 0)));
            var nulls = new (string Param, TestDelegate Call)[]
            {
                ("value", () => ((IResultShape<int, string>)null!).SelectMany(Next, (a, b) => b)),
                ("bindFunc", () => ok.SelectMany((Func<int, IResultShape<int, string>>)null!, (a, b) => b)),
                ("mapFunc", () => ok.SelectMany(Next, (Func<int, int, int>)null!)),
                ("bindFunc", () => 1.Ok<int, string>().SelectMany((Func<int, IResultShape<int, string>>)null!, (a, b) => b)),
                ("mapFunc", () => 1.Ok<int, string>().SelectMany(Next, (Func<int, int, int>)null!)),
                ("bindFunc", () => Ignore(Task.FromResult(1.Ok<int, string>()).SelectMany((Func<int, IResultShape<int, string>>)null!, (a, b) => b))),
                ("value", () => Ignore(((IResultShape<int, string>)null!).SelectMany(x => Task.FromResult(x.Ok<int, string>()), (a, b) => b))),
                ("bindFunc", () => Ignore(ok.SelectMany((Func<int, Task<Result<int, string>>>)null!, (a, b) => b))),
                ("bindFunc", () => Ignore(ok.SelectMany((Func<int, ValueTask<Result<int, string>>>)null!, (a, b) => b))),
            };
            Assert.That(nulls.Select(c => Assert.Throws<ArgumentNullException>(c.Call)!.ParamName), Is.EqualTo(nulls.Select(c => c.Param)));
        }

        // ---- Exceptions, cancellation and the zero value ----

        sealed class Boom() : Exception("boom");

        [Test]
        public void Exceptions_and_cancellation_propagate_unchanged()
        {
            var boom = new Boom();
            Task<Result<int>> ThrowNow(int _) => throw boom;
            async Task<Result<int>> Faults(int _) { await Task.Yield(); throw boom; }
            ValueTask<Result<int>> ThrowNowV(int _) => throw boom;
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.That(Assert.ThrowsAsync<Boom>(async () => await (from a in 1.Ok() from b in ThrowNow(a) select b)), Is.SameAs(boom));
            Assert.That(Assert.ThrowsAsync<Boom>(async () => await (from a in LaterTask(1.Ok()) from b in Faults(a) select b)), Is.SameAs(boom));
            Assert.That(Assert.ThrowsAsync<Boom>(async () => await (from a in LaterTask(1.Ok()) select a > 0 ? throw boom : a)), Is.SameAs(boom));
            Assert.That(Assert.ThrowsAsync<Boom>(async () => await (from a in 1.Ok() from b in ThrowNowV(a) select b)), Is.SameAs(boom));
            Assert.That(Assert.ThrowsAsync<Boom>(async () => await (from a in LaterValue(1.Ok()) from b in 2.Ok() select a > 0 ? throw boom : b)), Is.SameAs(boom));
            Assert.That(async () => await (from a in Task.FromCanceled<Result<int>>(cts.Token) from b in 2.Ok() select b), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(async () => await (from a in 1.Ok<int, string>() from b in Task.FromCanceled<Result<int, string>>(cts.Token) select b), Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public void The_zero_value_throws_wherever_a_query_reads_it()
        {
            Assert.That(async () => await (from a in LaterTask(default(Result<int>)) select a), Throws.InvalidOperationException);
            Assert.That(async () => await (from a in default(Result<int>) from b in LaterTask(a.Ok()) select b), Throws.InvalidOperationException);
            Assert.That(async () => await (from a in 1.Ok() from b in LaterTask(default(Result<int>)) select b), Throws.InvalidOperationException);
            Assert.That(async () => await (from a in LaterValue(default(Result<int, string>)) from b in 1.Ok<int, string>() select b), Throws.InvalidOperationException);
            Assert.That(async () => await (from a in 1.Ok<int, string>() from b in LaterValue(default(Result<int, string>)) select b), Throws.InvalidOperationException);
        }

        [Test]
        public void Null_arguments_throw_before_anything_runs()
        {
            var ok = 1.Ok();
            var typed = 1.Ok<int, string>();
            var task = Task.FromResult(ok);
            var typedTask = Task.FromResult(typed);
            ValueTask<Result<int>> Value() => new(ok);
            ValueTask<Result<int, string>> TypedValue() => new(typed);
            var calls = new (string Param, Action Call)[]
            {
                ("value", () => Ignore(((Task<Result<int>>)null!).Select(x => x))),
                ("mapFunc", () => Ignore(task.Select<int, int>(null!))),
                ("bindFunc", () => Ignore(task.SelectMany((Func<int, Result<int>>)null!))),
                ("bindFunc", () => Ignore(task.SelectMany((Func<int, Task<Result<int>>>)null!))),
                ("bindFunc", () => Ignore(ok.SelectMany((Func<int, Task<Result<int>>>)null!))),
                ("mapFunc", () => Ignore(task.SelectMany(x => ok, (Func<int, int, int>)null!))),
                ("mapFunc", () => Ignore(task.SelectMany(x => task, (Func<int, int, int>)null!))),
                ("mapFunc", () => Ignore(ok.SelectMany(x => task, (Func<int, int, int>)null!))),
                ("value", () => Ignore(((Task<Result<int, string>>)null!).Select(x => x))),
                ("mapFunc", () => Ignore(typedTask.Select<int, string, int>(null!))),
                ("bindFunc", () => Ignore(typedTask.SelectMany((Func<int, Result<int, string>>)null!))),
                ("bindFunc", () => Ignore(typedTask.SelectMany((Func<int, Task<Result<int, string>>>)null!))),
                ("bindFunc", () => Ignore(typed.SelectMany((Func<int, Task<Result<int, string>>>)null!))),
                ("mapFunc", () => Ignore(typedTask.SelectMany(x => typed, (Func<int, int, int>)null!))),
                ("mapFunc", () => Ignore(typedTask.SelectMany(x => typedTask, (Func<int, int, int>)null!))),
                ("mapFunc", () => Ignore(typed.SelectMany(x => typedTask, (Func<int, int, int>)null!))),
                ("bindFunc", () => Ignore(task.SelectMany((Func<int, ValueTask<Result<int>>>)null!))),
                ("bindFunc", () => Ignore(Value().SelectMany((Func<int, Task<Result<int>>>)null!))),
                ("mapFunc", () => Ignore(task.SelectMany(x => Value(), (Func<int, int, int>)null!))),
                ("mapFunc", () => Ignore(Value().SelectMany(x => task, (Func<int, int, int>)null!))),
                ("bindFunc", () => Ignore(typedTask.SelectMany((Func<int, ValueTask<Result<int, string>>>)null!))),
                ("bindFunc", () => Ignore(TypedValue().SelectMany((Func<int, Task<Result<int, string>>>)null!))),
                ("mapFunc", () => Ignore(typedTask.SelectMany(x => TypedValue(), (Func<int, int, int>)null!))),
                ("mapFunc", () => Ignore(TypedValue().SelectMany(x => typedTask, (Func<int, int, int>)null!))),
                ("mapFunc", () => Ignore(Value().Select<int, int>(null!))),
                ("bindFunc", () => Ignore(Value().SelectMany((Func<int, Result<int>>)null!))),
                ("bindFunc", () => Ignore(Value().SelectMany((Func<int, ValueTask<Result<int>>>)null!))),
                ("bindFunc", () => Ignore(ok.SelectMany((Func<int, ValueTask<Result<int>>>)null!))),
                ("mapFunc", () => Ignore(Value().SelectMany(x => ok, (Func<int, int, int>)null!))),
                ("mapFunc", () => Ignore(Value().SelectMany(x => Value(), (Func<int, int, int>)null!))),
                ("mapFunc", () => Ignore(ok.SelectMany(x => Value(), (Func<int, int, int>)null!))),
                ("mapFunc", () => Ignore(TypedValue().Select<int, string, int>(null!))),
                ("bindFunc", () => Ignore(TypedValue().SelectMany((Func<int, Result<int, string>>)null!))),
                ("bindFunc", () => Ignore(TypedValue().SelectMany((Func<int, ValueTask<Result<int, string>>>)null!))),
                ("bindFunc", () => Ignore(typed.SelectMany((Func<int, ValueTask<Result<int, string>>>)null!))),
                ("mapFunc", () => Ignore(TypedValue().SelectMany(x => typed, (Func<int, int, int>)null!))),
                ("mapFunc", () => Ignore(TypedValue().SelectMany(x => TypedValue(), (Func<int, int, int>)null!))),
                ("mapFunc", () => Ignore(typed.SelectMany(x => TypedValue(), (Func<int, int, int>)null!))),
            };

            Assert.That(calls.Select(c => Assert.Throws<ArgumentNullException>(() => c.Call())!.ParamName), Is.EqualTo(calls.Select(c => c.Param)));
        }

        // Each call throws before it returns a task.
        static void Ignore<T>(T _)
        {
        }

        [Test]
        public void System_Linq_queries_in_the_same_file_still_bind_to_System_Linq()
        {
            // Both namespaces are imported in this file. Task<T> isn't IEnumerable, so neither set competes for the other.
            IEnumerable<int> numbers = from x in Enumerable.Range(1, 2) from y in Enumerable.Range(10, 2) select x + y;
            Task<Result<int>> query = from a in Task.FromResult(1.Ok()) from b in Enumerable.Repeat(2, 1).Single().Ok() select a + b;

            Assert.That(numbers, Is.EqualTo(new List<int> { 11, 12, 12, 13 }));
            Assert.That(query.Result.Value, Is.EqualTo(3));
        }

        // ---- Ensure ----

        sealed record EmptyOrder() : Error("the order is empty");

        [Test]
        public void Ensure_is_a_success_or_the_given_error()
        {
            var error = new EmptyOrder();
            Assert.That(Result.Ensure(true, error), Is.EqualTo(Result.Ok()));
            Assert.That(Result.Ensure(false, error).Error, Is.SameAs(error));
            Assert.That(Result.Ensure(true, "wrong"), Is.EqualTo(Result.Ok()));
            Assert.That(Result.Ensure(false, "wrong").Error, Is.EqualTo(Error.From("wrong")));
            Assert.That(Result.Ensure<string>(true, "no"), Is.EqualTo(Result.Ok<string>()));
            Assert.That(Result.Ensure<string>(false, "no").Error, Is.EqualTo("no"));
            Assert.That(Result.Ensure(false, 404).Error, Is.EqualTo(404));
            Assert.That(Result.Ensure<Error>(false, error).Error, Is.SameAs(error));

            Assert.That(Assert.Throws<ArgumentNullException>(() => Result.Ensure(true, (Error)null!))!.ParamName, Is.EqualTo("error"));
            Assert.That(Assert.Throws<ArgumentNullException>(() => Result.Ensure<string>(true, null!))!.ParamName, Is.EqualTo("error"));
            Assert.That(Assert.Throws<ArgumentNullException>(() => Result.Ensure(true, (string)null!))!.ParamName, Is.EqualTo("message"));
        }

        [Test]
        public void Ensure_with_a_leaf_error_joins_a_Result_query_and_naming_the_type_gives_a_typed_one()
        {
            // OverloadResolutionPriority: a leaf derived from Error picks Result<Unit>, not Result<Unit, EmptyOrder>.
            Result<Unit> untyped = Result.Ensure(false, new EmptyOrder());
            Result<Unit, Error> typed = Result.Ensure<Error>(false, new EmptyOrder());
            Assert.That(Result.Ensure(false, new EmptyOrder()).GetType(), Is.EqualTo(typeof(Result<Unit>)));
            Assert.That(untyped.Error, Is.EqualTo(typed.Error));
        }

        [Test]
        public void Ensure_defaults_its_message_to_the_code_of_the_condition_and_never_its_values()
        {
            const string Secret = "SECRET-4111";
            var card = Secret;
            var total = -12.5m;

            var empty = Result.Ensure(total > 0);
            var leaked = Result.Ensure(card.Length == 0 && total > 0);

            Assert.That(empty.Error, Is.EqualTo(Error.From("total > 0")));
            Assert.That(leaked.Error.Message, Is.EqualTo("card.Length == 0 && total > 0"));
            Assert.That(leaked.Error.ToString(), Does.Not.Contain(Secret).And.Not.Contain("12.5"));
            Assert.That(Result.Ensure(total < 0), Is.EqualTo(Result.Ok()));
        }

        [Test]
        public async Task Ensure_is_a_step_in_a_query_with_from_underscore()
        {
            await Gen.Select(Gen.Int[-5, 5], Gen.Bool).SampleAsync(async t =>
            {
                var (total, later) = t;
                Task<Result<int>> Load() => later ? LaterTask(total.Ok()) : Task.FromResult(total.Ok());

                var sync = from x in total.Ok() from _ in Result.Ensure(x > 0, new EmptyOrder()) select x * 2;
                var async = from x in Load() from _ in Result.Ensure(x > 0) select x * 2;
                // A string is also Ensure's message, so a typed step over string names its type.
                var typed = from x in total.Ok<int, string>() from _ in Result.Ensure<string>(x > 0, "empty") select x * 2;

                var expected = total > 0 ? (total * 2).Ok() : new EmptyOrder().Fail<int>();
                Assert.That(sync, Is.EqualTo(expected));
                Assert.That(await async, Is.EqualTo(total > 0 ? (total * 2).Ok() : Error.From("x > 0").Fail<int>()));
                Assert.That(typed, Is.EqualTo(total > 0 ? (total * 2).Ok<int, string>() : "empty".Fail<int, string>()));
            });
        }

        // ---- The scenario: decision 22's receipt query ----

        sealed record OrderDto(string Id, string Card, int[] Quantities);
        sealed record Order(string Id, string Card, IReadOnlyList<int> Quantities);
        sealed record Stock(string OrderId, int Reserved);
        sealed record Payment(string Id, decimal Amount);
        sealed record Receipt(string OrderId, string PaymentId);

        sealed record InvalidQuantity(int Line) : Error($"line {Line} needs a positive quantity");
        sealed record OutOfStock(string OrderId) : Error("not enough stock");
        sealed record Declined() : Error("payment declined");

        sealed class Checkout
        {
            public readonly List<string> Calls = [];
            public int Stock = 100;
            public bool Decline;

            // Every line is checked, and every bad line reported; then the order is one Result.
            public Result<Order> ParseOrder(OrderDto dto) =>
                dto.Quantities.Select((q, i) => (q, i)).TraverseAll(l => l.q > 0 ? l.q.Ok() : new InvalidQuantity(l.i + 1).Fail<int>())
                    .ToResult()
                    .Map(qs => new Order(dto.Id, dto.Card, qs));

            public async Task<Result<Stock>> ReserveAsync(Order order)
            {
                Calls.Add("reserve");
                await Task.Yield();
                var wanted = order.Quantities.Sum();
                return wanted <= Stock ? new Stock(order.Id, wanted) : new OutOfStock(order.Id);
            }

            public static decimal Total(Order order) => order.Quantities.Sum() * 2.5m;

            public async ValueTask<Result<Payment>> ChargeAsync(string card, decimal total)
            {
                Calls.Add($"charge {total}");
                await Task.Yield();
                return Decline ? new Declined() : new Payment($"pay-{card[^4..]}", total);
            }

            public Task<Result<Receipt>> Place(OrderDto dto) =>
                from order in ParseOrder(dto)
                from stock in ReserveAsync(order)
                let total = Total(order)
                from _ in Result.Ensure(total > 0, new InvalidQuantity(0))
                from payment in ChargeAsync(order.Card, total)
                select new Receipt(order.Id, payment.Id);
        }

        [Test]
        public async Task The_receipt_query_parses_reserves_checks_and_charges_in_order()
        {
            var checkout = new Checkout();
            var receipt = await checkout.Place(new OrderDto("o-1", "4111-0000-1234", [2, 3]));

            Assert.That(receipt.Value, Is.EqualTo(new Receipt("o-1", "pay-1234")));
            Assert.That(checkout.Calls, Is.EqualTo(new List<string> { "reserve", "charge 12.5" }));
        }

        [Test]
        public async Task Each_failure_stops_the_receipt_query_where_it_happens()
        {
            var badLines = new Checkout();
            var invalid = await badLines.Place(new OrderDto("o-2", "4111-0000-1234", [0, 1, -4]));
            Assert.That(invalid.Error.TryFind<ValidationFailed<Error>>(out var failed), Is.True);
            Assert.That(failed!.Errors, Is.EqualTo(new Error[] { new InvalidQuantity(1), new InvalidQuantity(3) }));
            Assert.That(badLines.Calls, Is.Empty);

            var noStock = new Checkout { Stock = 1 };
            Assert.That((await noStock.Place(new OrderDto("o-3", "4111-0000-1234", [2]))).Error, Is.EqualTo(new OutOfStock("o-3")));
            Assert.That(noStock.Calls, Is.EqualTo(new List<string> { "reserve" }));

            var declined = new Checkout { Decline = true };
            Assert.That((await declined.Place(new OrderDto("o-4", "4111-0000-1234", [1]))).Error, Is.EqualTo(new Declined()));
            Assert.That(declined.Calls, Is.EqualTo(new List<string> { "reserve", "charge 2.5" }));
        }

        [Test]
        public async Task A_query_over_a_ValueTask_step_is_a_Task()
        {
            var checkout = new Checkout();
            Task<Result<Receipt>> Place(Result<Order> order) =>
                from o in order
                from payment in checkout.ChargeAsync(o.Card, Checkout.Total(o))
                select new Receipt(o.Id, payment.Id);

            var order = checkout.ParseOrder(new OrderDto("o-5", "4111-0000-9876", [4]));
            Assert.That((await Place(order)).Value, Is.EqualTo(new Receipt("o-5", "pay-9876")));
        }
    }
}
