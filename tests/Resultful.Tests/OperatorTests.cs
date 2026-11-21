using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
using NUnit.Framework;
using Resultful.Operators;

namespace Resultful.Tests
{
    // Decision 9 (18): operators are opt-in sugar over the named methods. The scenario reads a port from layered
    // configuration: the first source that has it wins (`|`), a slow source is asked only when needed (`||`), parsing
    // and range checks chain (`>>`), the same pipeline runs on tasks, and the request's fields are checked together
    // (`&`). The laws hold every operator to its named method on random inputs, with the laws each one claims.
    [TestFixture]
    public class OperatorTests
    {
        // ---- The scenario ----

        sealed record Missing(string Key) : Error($"{Key} is not set");
        sealed record NotANumber(string Text) : Error($"'{Text}' is not a number");
        sealed record OutOfRange(int Port) : Error($"{Port} is out of range");

        static Result<string> Read(IReadOnlyDictionary<string, string> source, string key)
            => source.TryGetValue(key, out var v) ? v : new Missing(key);

        static Result<int> Parse(string text)
            => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : new NotANumber(text);

        static Result<int> InRange(int port) => port is > 0 and < 65536 ? port : new OutOfRange(port);

        static Task<Result<string>> ReadAsync(IReadOnlyDictionary<string, string> source, string key) => Task.FromResult(Read(source, key));

        static async Task<Result<int>> ParseAsync(string text)
        {
            await Task.Yield();
            return Parse(text);
        }

        static async Task<Result<int>> InRangeAsync(int port)
        {
            await Task.Yield();
            return InRange(port);
        }

        static readonly Dictionary<string, string> Env = new() { ["host"] = "db.local" };
        static readonly Dictionary<string, string> File = new() { ["port"] = "5432", ["timeout"] = "x" };

        int vaultCalls;

        Result<string> Vault(string key)
        {
            vaultCalls++;
            return key == "port" ? "6543" : new Missing(key);
        }

        Result<int> Port(string key) => (Read(Env, key) || Vault(key) | Read(File, key)) >> Parse >> InRange;

        [Test]
        public void A_port_from_layered_configuration()
        {
            vaultCalls = 0;
            Assert.That(Port("port"), Is.EqualTo((Result<int>)6543), "the vault has it; the file isn't read");
            Assert.That(vaultCalls, Is.EqualTo(1));

            // `|` binds tighter than `||`, so `a || b | c` is `a || (b | c)`: the right side runs only when `a` fails.
            Env["port"] = "8080";
            try
            {
                Assert.That(Port("port"), Is.EqualTo((Result<int>)8080));
                Assert.That(vaultCalls, Is.EqualTo(1), "a success on the left never asks the vault");
            }
            finally
            {
                Env.Remove("port");
            }

            Assert.That(Port("timeout").Error, Is.EqualTo(new NotANumber("x")), ">> stops at the first failure");
            Assert.That(Port("retries").Error, Is.EqualTo(new Missing("retries")), "the last fallback's error is kept");
            Assert.That(Port("timeout") | 30, Is.EqualTo(30), "| with a value unwraps");

            var seen = "failure";
            if (Port("port")) seen = "success";
            Assert.That(seen, Is.EqualTo("success"), "if (result) reads IsSuccess");
        }

        [Test]
        public async Task The_same_pipeline_on_tasks()
        {
            // `>>` binds tighter than `|`, so the fallback needs parentheses before the steps.
            var port = await ((ReadAsync(Env, "port") | Read(File, "port")) >> (s => ParseAsync(s)) >> (p => InRangeAsync(p)));
            Assert.That(port, Is.EqualTo((Result<int>)5432));

            var timeout = await (ReadAsync(File, "timeout") >> (s => ParseAsync(s)) | 30);
            Assert.That(timeout, Is.EqualTo(30), "a failed parse falls back to the default");
        }

        sealed record Required(string Field) : Error($"{Field} is required");

        static Validation<string> Host(string? v) => string.IsNullOrEmpty(v) ? new Required("host") : v;

        static Validation<int> PortField(string? v) => string.IsNullOrEmpty(v) ? new Required("port") : Parse(v);

        [Test]
        public void Request_fields_are_checked_together()
        {
            var bad = Host(null) & PortField("80x");
            Assert.That(bad.Errors, Is.EqualTo(new Error[] { new Required("host"), new NotANumber("80x") }), "& keeps every error, in order");

            var good = Host("db.local") & PortField("5432");
            Assert.That(good.Value, Is.EqualTo(("db.local", 5432)));

            // A step that needs the value comes after the checks, in a Result.
            Result<int> checkedPort = good.ToResult() >> (t => InRange(t.Item2));
            Assert.That(checkedPort.Value, Is.EqualTo(5432));
        }

        // ---- The laws ----

        sealed record Leaf(int Code) : Error($"leaf {Code}");

        // Small alphabets, so equal values and equal errors meet.
        static readonly Gen<Result<int>> Results = Gen.Frequency(
            (1, Gen.Int[0, 3].Select(v => v.Ok())),
            (1, Gen.Int[0, 3].Select(c => new Leaf(c).Fail<int>())));

        static readonly Gen<Result<int, string>> Typed = Gen.Frequency(
            (1, Gen.Int[0, 3].Select(v => v.Ok<int, string>())),
            (1, Gen.OneOf("a", "b", "c").Select(e => e.Fail<int, string>())));

        static readonly Gen<Validation<int, Leaf>> Validations = Gen.Frequency(
            (1, Gen.Int[0, 3].Select(v => Validation.Ok<int, Leaf>(v))),
            (2, Gen.Int[0, 3].Select(c => new Leaf(c)).Array[1, 3].Select(es => Validation.Fail<int, Leaf>(es))));

        // A step that fails on some values, so >> meets both cases.
        static Result<int> Step(int k, int x) => (x + k) % 3 == 0 ? new Leaf(x) : x + k;

        static Result<int, string> TypedStep(int k, int x) => (x + k) % 3 == 0 ? $"e{x}" : x + k;

        [Test]
        public void Pipe_is_OrElse_and_ValueOr()
        {
            Gen.Select(Results, Results, Gen.Int).Sample((a, b, v) =>
            {
                Assert.That(a | b, Is.EqualTo(a.OrElse(b)));
                Assert.That(a | v, Is.EqualTo(a.ValueOr(v)));
            });
            Gen.Select(Typed, Typed, Gen.Int).Sample((a, b, v) =>
            {
                Assert.That(a | b, Is.EqualTo(a.OrElse(b)));
                Assert.That(a | v, Is.EqualTo(a.ValueOr(v)));
            });
        }

        [Test]
        public void Pipe_is_associative_and_a_failure_is_its_left_identity()
        {
            Gen.Select(Results, Results, Results).Sample((a, b, c) =>
            {
                Assert.That((a | b) | c, Is.EqualTo(a | (b | c)));
                if (a.IsFailure) Assert.That(a | b, Is.EqualTo(b));
                Assert.That((a | b) | 7, Is.EqualTo(a | (b | 7)));
            });
            Gen.Select(Typed, Typed, Typed).Sample((a, b, c) =>
            {
                Assert.That((a | b) | c, Is.EqualTo(a | (b | c)));
                if (a.IsFailure) Assert.That(a | b, Is.EqualTo(b));
            });
        }

        [Test]
        public void OrOr_is_a_lazy_OrElse()
        {
            Gen.Select(Results, Results).Sample((a, b) =>
            {
                var calls = 0;
                var lazy = a || Count(b);
                Assert.That(lazy, Is.EqualTo(a.OrElse(b)));
                Assert.That(calls, Is.EqualTo(a.IsSuccess ? 0 : 1), "the right side runs only on a failure");

                Result<int> Count(Result<int> r) { calls++; return r; }
            });
            Gen.Select(Typed, Typed).Sample((a, b) =>
            {
                var calls = 0;
                var lazy = a || Count(b);
                Assert.That(lazy, Is.EqualTo(a.OrElse(b)));
                Assert.That(calls, Is.EqualTo(a.IsSuccess ? 0 : 1));

                Result<int, string> Count(Result<int, string> r) { calls++; return r; }
            });
        }

        [Test]
        public void True_and_false_are_IsSuccess_and_IsFailure()
        {
            Results.Sample(r => Assert.That(r ? true : false, Is.EqualTo(r.IsSuccess)));
            Typed.Sample(r => Assert.That(r ? true : false, Is.EqualTo(r.IsSuccess)));

            // The zero value is neither (decision 10): `if` reads it as false; `||` then calls OrElse, which throws.
            Assert.That(default(Result<int>) ? true : false, Is.False);
            Assert.That(() => default(Result<int>) || 1.Ok(), Throws.InvalidOperationException);
        }

        [Test]
        public void Shift_is_Bind_and_keeps_the_monad_laws()
        {
            Gen.Select(Results, Gen.Int[0, 3], Gen.Int[0, 3]).Sample((m, j, k) =>
            {
                Func<int, Result<int>> f = x => Step(j, x), g = x => Step(k, x);
                Assert.That(m >> f, Is.EqualTo(m.Bind(f)));
                Assert.That(m >> (x => x.Ok()), Is.EqualTo(m), "right identity");
                Assert.That(j.Ok() >> f, Is.EqualTo(f(j)), "left identity");
                Assert.That((m >> f) >> g, Is.EqualTo(m >> (x => f(x) >> g)), "associativity");
                Assert.That(m >> (x => x.ToString(CultureInfo.InvariantCulture).Ok()), Is.EqualTo(m.Map(x => x.ToString(CultureInfo.InvariantCulture))), "type-changing");
            });
            Gen.Select(Typed, Gen.Int[0, 3], Gen.Int[0, 3]).Sample((m, j, k) =>
            {
                Func<int, Result<int, string>> f = x => TypedStep(j, x), g = x => TypedStep(k, x);
                Assert.That(m >> f, Is.EqualTo(m.Bind(f)));
                Assert.That(m >> (x => x.Ok<int, string>()), Is.EqualTo(m), "right identity");
                Assert.That(j.Ok<int, string>() >> f, Is.EqualTo(f(j)), "left identity");
                Assert.That((m >> f) >> g, Is.EqualTo(m >> (x => f(x) >> g)), "associativity");
            });
        }

        [Test]
        public void Ampersand_is_Combine_and_associative()
        {
            Gen.Select(Validations, Validations, Validations).Sample((a, b, c) =>
            {
                Assert.That(a & b, Is.EqualTo(Validation.Combine(a, b)));
                var left = ((a & b) & c).Map(t => (t.Item1.Item1, t.Item1.Item2, t.Item2));
                var right = (a & (b & c)).Map(t => (t.Item1, t.Item2.Item1, t.Item2.Item2));
                Assert.That(left, Is.EqualTo(right));

                Validation<int> ua = a.AsValidation(), ub = b.AsValidation();
                Assert.That(ua & ub, Is.EqualTo(Validation.Combine(ua, ub)));
            });
        }

        [Test]
        public void Task_forms_equal_the_sync_ones()
        {
            Gen.Select(Results, Results, Gen.Int, Gen.Int[0, 3]).Sample((a, b, v, k) =>
            {
                var task = Task.FromResult(a);
                Assert.That((task | b).GetAwaiter().GetResult(), Is.EqualTo(a | b));
                Assert.That((task | v).GetAwaiter().GetResult(), Is.EqualTo(a | v));
                Assert.That((task >> (x => Task.FromResult(Step(k, x)))).GetAwaiter().GetResult(), Is.EqualTo(a >> (x => Step(k, x))));
                Assert.That(task.OrElse(b).GetAwaiter().GetResult(), Is.EqualTo(a.OrElse(b)));
                Assert.That(task.ValueOr(v).GetAwaiter().GetResult(), Is.EqualTo(a.ValueOr(v)));
            });
            Gen.Select(Typed, Typed, Gen.Int, Gen.Int[0, 3]).Sample((a, b, v, k) =>
            {
                var task = Task.FromResult(a);
                Assert.That((task | b).GetAwaiter().GetResult(), Is.EqualTo(a | b));
                Assert.That((task | v).GetAwaiter().GetResult(), Is.EqualTo(a | v));
                Assert.That((task >> (x => Task.FromResult(TypedStep(k, x)))).GetAwaiter().GetResult(), Is.EqualTo(a >> (x => TypedStep(k, x))));
                Assert.That(task.OrElse(b).GetAwaiter().GetResult(), Is.EqualTo(a.OrElse(b)));
                Assert.That(task.ValueOr(v).GetAwaiter().GetResult(), Is.EqualTo(a.ValueOr(v)));
            });
        }

        // `==` and `!=` live on the types, not behind the opt-in namespace (DECISIONS-TAKEN 10, 18).
        [Test]
        public void Equality_operators_agree_with_Equals()
        {
            Gen.Select(Results, Results).Sample((a, b) =>
            {
                Assert.That(a == b, Is.EqualTo(a.Equals(b)));
                Assert.That(a != b, Is.Not.EqualTo(a.Equals(b)));
            });
            Gen.Select(Typed, Typed).Sample((a, b) =>
            {
                Assert.That(a == b, Is.EqualTo(a.Equals(b)));
                Assert.That(a != b, Is.Not.EqualTo(a.Equals(b)));
            });
            Gen.Select(Gen.Bool, Gen.Int[0, 2], Gen.Bool, Gen.Int[0, 2]).Sample((p, x, q, y) =>
            {
                Option<int> a = p ? x : default(Option<int>), b = q ? y : default(Option<int>);
                Assert.That(a == b, Is.EqualTo(a.Equals(b)));
                Assert.That(a != b, Is.Not.EqualTo(a.Equals(b)));
            });
#pragma warning disable CS0618 // VoidResult is obsolete; its equality still has operators.
            Gen.Select(Results, Results).Sample((x, y) =>
            {
                VoidResult a = x.DiscardValue(), b = y.DiscardValue();
                Assert.That(a == b, Is.EqualTo(a.Equals(b)));
                Assert.That(a != b, Is.Not.EqualTo(a.Equals(b)));
            });
#pragma warning restore CS0618
            Assert.That(default(Result<int>) == default(Result<int>), Is.True, "the zero value equals itself and never throws");
            Assert.That(default(Result<int, string>) != 1.Ok<int, string>(), Is.True);
        }
    }
}
