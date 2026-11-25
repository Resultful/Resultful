using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using CsCheck;
using NUnit.Framework;
using OneOf.Types;

namespace Resultful.Tests
{
    // 08a: every type keeps its own state, a state byte, instead of OneOf storage. Nothing a caller can see changes, so
    // the laws hold every type to a small reference model of the 0.2 storage (plus 05's named members), and the
    // examples pin struct sizes and allocations.
    [TestFixture]
    public class StorageTests
    {
        // The reference model. State 0 is the zero value, 1 a success, 2 a failure. The 0.2 members see the zero
        // value as a success holding default(T); 05's named members see neither case and throw.
        readonly record struct M(byte State, int Value, string? Error)
        {
            public bool Ok => State != 2;
            public int SeenValue => Ok ? Value : 0;
            public static M Success(int value) => new(1, value, null);
            public static M Failure(string? error) => new(2, 0, error);

            // OneOf<T0, T1>.GetHashCode: the case's hash times 397, xor the case index. 0.2's hash was the runtime's mix of
            // the type and this, so the laws check that two results hash alike exactly when their models do.
            public int Hash => Of(Ok, Value, Error);

            public bool Same(M other) => State == other.State && (Ok ? Value == other.Value : Error == other.Error);
        }

        static int Of(bool ok, object? value, object? error)
            => unchecked(((ok ? value?.GetHashCode() : error?.GetHashCode()) ?? 0) * 397 ^ (ok ? 0 : 1));

        static Result<int, string> Make(M m)
            => m.State switch { 0 => default, 1 => m.Value.Ok<int, string>(), _ => m.Error!.Fail<int, string>() };

        // One step of a sequence: what it does to a result, and what the model says it does. Model returns null when
        // the step throws InvalidOperationException (a named member on the zero value). Both write to a log, so
        // callbacks are checked too.
        sealed record Op(string Name, Func<Result<int, string>, List<string>, Result<int, string>> Run, Func<M, List<string>, M?> Model)
        {
            public override string ToString() => Name;
        }

        static readonly Gen<string?> Errors = Gen.OneOf<string?>(Gen.Const((string?)null), Gen.OneOf<string?>("a", "b", "İ"));

        static readonly Gen<M> Models = Gen.Frequency(
            (1, Gen.Const(new M(0, 0, null))),
            (3, Gen.Int[-3, 3].Select(M.Success)),
            (3, Errors.Select(M.Failure)));

        static readonly Gen<Op> Ops = Gen.OneOf(
            Gen.Int[-3, 3].Select(k => new Op($"Map(+{k})",
                (r, _) => r.Map(x => x + k),
                (m, _) => m.Ok ? M.Success(m.Value + k) : m)),
            Gen.Int[-3, 3].Select(k => new Op($"Bind({k})",
                (r, _) => r.Bind(x => x % 2 == 0 ? (x + k).Ok<int, string>() : ("odd" + k).Fail<int, string>()),
                (m, _) => !m.Ok ? m : m.Value % 2 == 0 ? M.Success(m.Value + k) : M.Failure("odd" + k))),
            Gen.Const(new Op("MapError",
                (r, _) => r.MapError(e => e + "!"),
                (m, _) => m.Ok ? M.Success(m.Value) : M.Failure(m.Error + "!"))),
            Gen.Const(new Op("Tee",
                (r, log) => r.Tee(x => log.Add("tee " + x)),
                (m, log) => { if (m.Ok) log.Add("tee " + m.Value); return m.Ok ? M.Success(m.Value) : m; })),
            Gen.Const(new Op("TeeError",
                (r, log) => r.TeeError(e => log.Add("err " + e)),
                (m, log) => { if (!m.Ok) log.Add("err " + m.Error); return m.Ok ? M.Success(m.Value) : m; })),
            Models.Select(o => new Op($"OrElse({o})",
                (r, _) => r.OrElse(Make(o)),
                (m, _) => m.State == 0 ? null : m.Ok ? m : o)),
            Gen.Const(new Op("OrElse(func)",
                (r, log) => r.OrElse(e => { log.Add("else " + e); return e is null ? 9.Ok<int, string>() : (e + "?").Fail<int, string>(); }),
                (m, log) =>
                {
                    if (m.State == 0) return null;
                    if (m.Ok) return m;
                    log.Add("else " + m.Error);
                    return m.Error is null ? M.Success(9) : M.Failure(m.Error + "?");
                })),
            Gen.Int[-3, 3].Select(k => new Op($"DiscardValue.Map({k})",
                (r, _) => r.DiscardValue().Map(k),
                (m, _) => m.Ok ? M.Success(k) : m)),
            Gen.Const(new Op("DiscardError.Match",
                (r, _) => r.DiscardError().Match(v => v.Ok<int, string>(), _ => "none".Fail<int, string>()),
                (m, _) => m.Ok ? M.Success(m.Value) : M.Failure("none"))),
            Gen.Const(new Op("MapError(list).MapError(join)",
                (r, _) => r.MapError(e => (IEnumerable<string>)new[] { e }).MapError(es => string.Join(",", es)),
                (m, _) => m.Ok ? M.Success(m.Value) : M.Failure(m.Error ?? ""))),
            Gen.Const(new Op("Cast.CastError",
                (r, _) => r.Cast<int>().CastError<string>(),
                (m, _) => m.Ok ? M.Success(m.Value) : m)),
            Gen.Int[-3, 3].Select(k => new Op($"= {k}", (_, _) => k, (_, _) => M.Success(k))),
            Gen.Const(new Op("= \"c\"", (_, _) => "c", (_, _) => M.Failure("c"))),
            Gen.Const(new Op("= default", (_, _) => default, (_, _) => new M(0, 0, null))));

        // Everything a caller can observe about one result, checked against the model.
        static void Observe(Result<int, string> r, M m)
        {
            Assert.That(r.Match(v => (true, v, (string?)null), e => (false, 0, e)), Is.EqualTo((m.Ok, m.SeenValue, m.Ok ? null : m.Error)));
            (bool, int, string?) switched = default;
            r.Switch(v => switched = (true, v, null), e => switched = (false, 0, e));
            Assert.That(switched, Is.EqualTo((m.Ok, m.SeenValue, m.Ok ? null : m.Error)));
            var oneOf = r.ToOneOf();
            Assert.That((oneOf.IsT0, oneOf.Match(v => (object?)v, e => e)), Is.EqualTo((m.Ok, m.Ok ? m.SeenValue : (object?)m.Error)));
            Assert.That((r.IsSuccess, r.IsFailure), Is.EqualTo((m.State == 1, m.State == 2)));
            Assert.That(r.ToString(), Is.EqualTo("Resultful.Result`2[System.Int32,System.String]"));
            Assert.That(r.Equals(r) && r.Equals((object)r), Is.True);
            Assert.That(r.Equals(null) || r.Equals(m.Value) || r.Equals(Make(m).MapError(e => (object?)e)), Is.False);
            if (m.State == 0)
            {
                Assert.That(() => r.Value, Throws.InvalidOperationException);
                Assert.That(() => r.Error, Throws.InvalidOperationException);
                Assert.That(() => r.TryGetValue(out _), Throws.InvalidOperationException);
                Assert.That(() => r.ValueOr(5), Throws.InvalidOperationException);
                return;
            }
            Assert.That(r.TryGetValue(out var value) ? (true, value) : (false, value), Is.EqualTo((m.Ok, m.SeenValue)));
            Assert.That(r.TryGetError(out var error) ? (true, error) : (false, error), Is.EqualTo((!m.Ok, m.Ok ? null : m.Error)));
            Assert.That(r.ValueOr(5), Is.EqualTo(m.Ok ? m.Value : 5));
            var (isSuccess, v2, e2) = r;
            Assert.That((isSuccess, v2, e2), Is.EqualTo((m.Ok, m.SeenValue, m.Ok ? null : m.Error)));
            if (m.Ok) Assert.That(r.Value, Is.EqualTo(m.Value)); else Assert.That(r.Error, Is.EqualTo(m.Error));
        }

        [Test]
        public void Random_operation_sequences_behave_like_the_reference_model()
        {
            Gen.Select(Models, Ops.Array[0, 8], Models).Sample((start, ops, other) =>
            {
                var r = Make(start);
                var m = start;
                var otherResult = Make(other);
                var log = new List<string>();
                var expectedLog = new List<string>();
                Observe(r, m);
                foreach (var op in ops)
                {
                    var next = op.Model(m, expectedLog);
                    if (next is null)
                    {
                        Assert.That(() => op.Run(r, log), Throws.InvalidOperationException, op.Name);
                        return;
                    }
                    r = op.Run(r, log);
                    m = next.Value;
                    Assert.That(log, Is.EqualTo(expectedLog), op.Name);
                    Observe(r, m);
                    Assert.That(r.Equals(otherResult), Is.EqualTo(m.Same(other)), op.Name);
                    Assert.That(r.Equals((object)otherResult), Is.EqualTo(m.Same(other)), op.Name);
                    Assert.That(r.GetHashCode() == otherResult.GetHashCode(), Is.EqualTo(m.Hash == other.Hash), op.Name);
                }
            }, iter: 2000);
        }

        // Map2 now reads the state byte instead of calling Match with closures; its argument checks stay as they were.
        [Test]
        public void Callbacks_are_checked_for_null_as_before()
        {
            Result<int, string> ok = 1, failed = "bad";
            Assert.That(() => ok.Map<int>(null!), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("mapFunc"));
            Assert.That(failed.Map<int>(null!).Error, Is.EqualTo("bad"), "a failure never reads mapFunc");
            Assert.That(() => failed.MapError<string>(null!), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("errorMapFunc"));
            Assert.That(ok.MapError<string>(null!).Value, Is.EqualTo(1), "a success never reads errorMapFunc");
            Assert.That(() => failed.Match(null!, e => e), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("successfulFunc"));
            Assert.That(() => ok.Switch(_ => { }, null!), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("errorFunc"));
            Assert.That(() => 1.Ok().Map<int>(null!), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("mapFunc"));
            Assert.That(() => Result.Ok().Match(null!, _ => 0), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("successfulFunc"));
            Assert.That(() => Option<int>.None.SwitchAsync(_ => Task.CompletedTask, null!), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("noneFunc"));
        }

        // IEquatable<T>.Equals disagrees with Equals(object) here; 0.2 (OneOf) used object.Equals, so results do too.
#pragma warning disable CA1067 // the disagreement is the point
        sealed class Eq : IEquatable<Eq>
        {
            public int V;
            public bool Equals(Eq? other) => other?.V == V;
        }

        struct EqStruct : IEquatable<EqStruct>
        {
            public int V, Extra;
            public readonly bool Equals(EqStruct other) => other.V == V;
        }
#pragma warning restore CA1067

        [Test]
        public void Values_and_errors_are_compared_with_object_Equals()
        {
            var (a, b) = (new Eq { V = 1 }, new Eq { V = 1 });
            Assert.That(a.Equals(b), Is.True, "the IEquatable path says equal");
            Assert.That(a.Ok<Eq, string>().Equals(b.Ok<Eq, string>()), Is.False);
            Assert.That(a.Fail<int, Eq>().Equals(b.Fail<int, Eq>()), Is.False);
            Assert.That(a.Ok().Equals(b.Ok()), Is.False);
            Assert.That(a.Fail().Equals(b.Fail()), Is.False);
            Assert.That(a.Some().Equals(b.Some()), Is.False);
            Assert.That(a.Ok<Eq, string>().Equals(a.Ok<Eq, string>()), Is.True);

            var (s, t) = (new EqStruct { V = 1, Extra = 1 }, new EqStruct { V = 1, Extra = 2 });
            Assert.That(s.Equals(t), Is.True, "the IEquatable path says equal");
            Assert.That(s.Ok<EqStruct, string>().Equals(t.Ok<EqStruct, string>()), Is.False);
            Assert.That(s.Fail<int, EqStruct>().Equals(t.Fail<int, EqStruct>()), Is.False);
            Assert.That(s.Ok().Equals(t.Ok()), Is.False);
            Assert.That(s.Fail().Equals(t.Fail()), Is.False);
            Assert.That(s.Some().Equals(t.Some()), Is.False);
            Assert.That(s.Some().Equals(new EqStruct { V = 1, Extra = 1 }.Some()), Is.True);
        }

        // The other types, value by value: each 0.2 member against the same model.
        [Test]
        public void Result_with_an_error_list_behaves_like_the_reference_model()
        {
            var errors = new[] { "x" };
            var gen = Gen.Frequency((1, Gen.Const(((byte)0, 0))), (3, Gen.Int[-3, 3].Select(v => ((byte)1, v))), (3, Gen.Const(((byte)2, 0))));
            Gen.Select(gen, gen).Sample((a, b) =>
            {
                Result<int> Make((byte State, int Value) m) => m.State switch { 0 => default, 1 => m.Value.Ok(), _ => errors.Fail<int>() };
                var (r, s) = (Make(a), Make(b));
                var ok = a.Item1 != 2;
                Assert.That(r.Match(v => (true, v, (IEnumerable<string>?)null), e => (false, 0, e)), Is.EqualTo((ok, a.Item2, ok ? null : errors)));
                Assert.That((r.IsSuccess, r.IsFailure), Is.EqualTo((a.Item1 == 1, a.Item1 == 2)));
                Assert.That(r.ToOneOf().IsT0, Is.EqualTo(ok));
                Assert.That(r.GetHashCode() == s.GetHashCode(), Is.EqualTo(Of(ok, a.Item2, errors) == Of(b.Item1 != 2, b.Item2, errors)));
                Assert.That(r.Equals(s), Is.EqualTo(a.Item1 == b.Item1 && (!ok || a.Item2 == b.Item2)));
                Assert.That(r.ToString(), Is.EqualTo("Resultful.Result`1[System.Int32]"));
                var mapped = r.Map(x => x * 2).Bind(x => x > 2 ? x.Ok() : Result.Fail<int>("small"));
                Assert.That(mapped.Match(v => v.ToString(), e => string.Join(",", e)),
                    Is.EqualTo(!ok ? "x" : a.Item2 * 2 > 2 ? (a.Item2 * 2).ToString() : "small"));
                Assert.That(r.DiscardValue().Match(_ => "ok", e => string.Join(",", e)), Is.EqualTo(ok ? "ok" : "x"));
            });
        }

        [Test]
        public void VoidResults_behave_like_the_reference_model()
        {
            var errors = new[] { "x" };
            var gen = Gen.Byte[0, 2];
            Gen.Select(gen, gen, Errors).Sample((a, b, error) =>
            {
                VoidResult Plain(byte state) => state switch { 0 => default, 1 => Result.Ok(), _ => Result.Fail(errors) };
                VoidResult<string?> Typed(byte state) => state switch { 0 => default, 1 => Result.Ok<string?>(), _ => error.Fail<string?>() };
                // The zero value is a success whose Unit is null; Ok() holds Unit.Value.
                var unit = a == 1 ? Unit.Value : null;
                var ok = a != 2;
                bool Same() => a == b;

                var (p, q) = (Plain(a), Plain(b));
                Assert.That(p.Match(u => (true, u, (IEnumerable<string>?)null), e => (false, (Unit?)null, e)), Is.EqualTo((ok, ok ? unit : null, ok ? null : errors)));
                Assert.That(p.ToOneOf().IsT0, Is.EqualTo(ok));
                Assert.That(p.GetHashCode() == q.GetHashCode(), Is.EqualTo(Of(ok, unit, errors) == Of(b != 2, b == 1 ? Unit.Value : null, errors)));
                Assert.That(p.Equals(q), Is.EqualTo(Same()));
                Assert.That(p.ToString(), Is.EqualTo("Resultful.VoidResult"));
                Assert.That(p.Map(_ => 1).Match(v => v, _ => -1), Is.EqualTo(ok ? 1 : -1));

                var (t, u2) = (Typed(a), Typed(b));
                Assert.That(t.Match(u => (true, u, (string?)null), e => (false, (Unit?)null, e)), Is.EqualTo((ok, ok ? unit : null, ok ? null : error)));
                Assert.That(t.ToOneOf().IsT0, Is.EqualTo(ok));
                Assert.That(t.GetHashCode() == u2.GetHashCode(), Is.EqualTo(Of(ok, unit, error) == Of(b != 2, b == 1 ? Unit.Value : null, error)));
                Assert.That(t.Equals(u2), Is.EqualTo(Same()));
                Assert.That(t.ToString(), Is.EqualTo("Resultful.VoidResult`1[System.String]"));
                Assert.That(t.Bind(_ => "next".Fail<string?>()).Match(_ => "ok", e => e), Is.EqualTo(ok ? "next" : error));
                Assert.That(t.MapError(e => e + "!").Match(_ => "ok", e => e), Is.EqualTo(ok ? "ok" : error + "!"));
            });
        }

        [Test]
        public void Option_behaves_like_the_reference_model()
        {
            // 0.2: the zero value is Some(default(T)) in every way, equality and hash included (08b makes it None).
            var gen = Gen.Frequency((1, Gen.Const(((byte)0, 0))), (3, Gen.Int[-3, 3].Select(v => ((byte)1, v))), (3, Gen.Const(((byte)2, 0))));
            Gen.Select(gen, gen).Sample((a, b) =>
            {
                Option<int> Make((byte State, int Value) m) => m.State switch { 0 => default, 1 => m.Value.Some(), _ => Option<int>.None };
                var (o, p) = (Make(a), Make(b));
                var some = a.Item1 != 2;
                Assert.That(o.Match(v => (true, v), _ => (false, 0)), Is.EqualTo((some, a.Item2)));
                Assert.That(o.ToOneOf().IsT0, Is.EqualTo(some));
                Assert.That(o.GetHashCode() == p.GetHashCode(), Is.EqualTo(Of(some, a.Item2, new None()) == Of(b.Item1 != 2, b.Item2, new None())));
                Assert.That(o.Equals(p), Is.EqualTo(some == (b.Item1 != 2) && (!some || a.Item2 == b.Item2)));
                Assert.That(o.ToString(), Is.EqualTo("Resultful.Option`1[System.Int32]"));
                Assert.That(o.Map(x => x + 1).Or(-1), Is.EqualTo(some ? a.Item2 + 1 : -1));
                Assert.That(o.Bind(x => x > 0 ? x.Some() : Option<int>.None).Or(-1), Is.EqualTo(some && a.Item2 > 0 ? a.Item2 : -1));
            });
        }

        // On OneOf storage (with 05's flag) they were 24, 32, 16, 32, 24, 24, 24, 12, 16.
        static readonly int[] Sizes = [16, 24, 12, 24, 16, 16, 16, 8, 16];

        // The state byte replaces OneOf's int case index and 05's bool flag (64-bit runtime).
        [Test]
        public void Struct_sizes_are_pinned()
        {
            Assume.That(IntPtr.Size, Is.EqualTo(8));
            int[] actual =
            [
                Unsafe.SizeOf<Result<int, string>>(),
                Unsafe.SizeOf<Result<string, string>>(),
                Unsafe.SizeOf<Result<int, int>>(),
                Unsafe.SizeOf<Result<long, Error>>(),
                Unsafe.SizeOf<Result<int>>(),
                Unsafe.SizeOf<VoidResult<string>>(),
                Unsafe.SizeOf<VoidResult>(),
                Unsafe.SizeOf<Option<int>>(),
                Unsafe.SizeOf<Option<string>>(),
            ];
            Assert.That(actual, Is.EqualTo(Sizes), string.Join(", ", actual));
        }

        static long Allocated(Action action)
        {
            action();
            action();
            var before = GC.GetAllocatedBytesForCurrentThread();
            action();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        static readonly string Message = "bad";

        // On OneOf storage GetHashCode took 144 bytes and Equals 184 (the runtime's field-by-field fallback), and Map and
        // MapError 144 to 160 (Map2's closures, now a switch on the state byte).
        [Test]
        public void Allocations_of_the_common_operations_are_pinned()
        {
            Result<int, string> ok = 1, failed = Message;
            var list = 1.Ok();
            var sink = 0;
            var actual = new Dictionary<string, long>
            {
                ["Ok"] = Allocated(() => sink += 1.Ok<int, string>().IsSuccess ? 1 : 0),
                ["Fail"] = Allocated(() => sink += Message.Fail<int, string>().IsFailure ? 1 : 0),
                ["implicit"] = Allocated(() => { Result<int, string> r = 2; sink += r.IsSuccess ? 1 : 0; }),
                ["Match"] = Allocated(() => sink += ok.Match(static v => v, static _ => 0) + failed.Match(static v => v, static _ => 0)),
                ["Switch"] = Allocated(() => ok.Switch(static _ => { }, static _ => { })),
                ["named"] = Allocated(() => sink += ok.Value + failed.ValueOr(0) + (ok.TryGetValue(out var v) ? v : 0) + failed.Error.Length),
                ["GetHashCode"] = Allocated(() => sink += ok.GetHashCode() ^ failed.GetHashCode()),
                ["Equals"] = Allocated(() => sink += ok.Equals(failed) ? 1 : 0),
                ["Map"] = Allocated(() => sink += ok.Map(static x => x + 1).IsSuccess ? 1 : 0),
                ["Bind"] = Allocated(() => sink += ok.Bind(static x => x.Ok<int, string>()).IsSuccess ? 1 : 0),
                ["MapError"] = Allocated(() => sink += failed.MapError(static e => e).IsFailure ? 1 : 0),
                ["Result<T>.Map"] = Allocated(() => sink += list.Map(static x => x + 1).IsSuccess ? 1 : 0),
                ["VoidResult.Match"] = Allocated(() => sink += Result.Ok().Match(static _ => 1, static _ => 0)),
                ["Option.Match"] = Allocated(() => sink += 1.Some().Match(static v => v, static _ => 0)),
            };
            Assert.That(sink, Is.Not.Zero);
            Assert.That(actual, Is.EquivalentTo(new Dictionary<string, long>
            {
                ["Ok"] = 0,
                ["Fail"] = 0,
                ["implicit"] = 0,
                ["Match"] = 0,
                ["Switch"] = 0,
                ["named"] = 0,
                ["GetHashCode"] = 0,
                ["Equals"] = 32, // Equals(object) boxes its argument; 0.2's reflection-based equality took 184
                ["Map"] = 0,
                ["Bind"] = 0,
                ["MapError"] = 0,
                ["Result<T>.Map"] = 0,
                ["VoidResult.Match"] = 0,
                ["Option.Match"] = 0,
            }));
        }
    }
}
