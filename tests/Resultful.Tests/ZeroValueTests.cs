using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using CsCheck;
using NUnit.Framework;
using OneOf.Types;
using Resultful.LINQ;

namespace Resultful.Tests
{
    // Decision 10 (08b): the zero value (default) of a result is uninitialised. Every member that reads its value, error
    // or case throws InvalidOperationException; IsSuccess and IsFailure are false; ToString, Equals, GetHashCode and the
    // debugger display never throw. Fail rejects null, and on net11.0 an error union that holds no case.
    [TestFixture]
    public class ZeroValueTests
    {
        // Never assigned: the zero value, as an uninitialised field is.
        static readonly Result<int, string> Zero;
        static readonly Result<int> ZeroList;
        static readonly VoidResult<string> ZeroVoid;
        static readonly VoidResult ZeroVoidList;

        static readonly Result<int, string> Ok = 1;
        static readonly Result<int, string> Failed = "bad";

        // One call on a zero value. A returned task is awaited, so an async member counts whether it throws at once or
        // faults its task.
        sealed record Call(string Type, string Member, Func<object?> Run)
        {
            public override string ToString() => $"{Type}.{Member}";
        }

        static Exception? Outcome(Func<object?> run)
        {
            try
            {
                if (run() is Task task) task.GetAwaiter().GetResult();
                return null;
            }
            catch (Exception e)
            {
                return e;
            }
        }

        static Task<T> Async<T>(T value) => Task.FromResult(value);

        static readonly Func<int, Task> NoTask = _ => Task.CompletedTask;

        // Every public instance member of the four result types, plus the extension methods, LINQ, task and conversion
        // members that read a result. Members_that_read_the_zero_value_are_all_listed keeps the instance part complete.
        static readonly Call[] Calls =
        [
            // Result<T, TError>
            new("Result`2", "Value", () => Zero.Value),
            new("Result`2", "Error", () => Zero.Error),
            new("Result`2", "TryGetValue", () => Zero.TryGetValue(out _)),
            new("Result`2", "TryGetError", () => Zero.TryGetError(out _)),
            new("Result`2", "Deconstruct", () => { var (a, b, c) = Zero; return (a, b, c); }),
            new("Result`2", "OrElse", () => Zero.OrElse(Ok)),
            new("Result`2", "OrElse", () => Zero.OrElse(_ => Ok)),
            new("Result`2", "ValueOr", () => Zero.ValueOr(0)),
            new("Result`2", "Match", () => Zero.Match(v => v, _ => 0)),
            new("Result`2", "Switch", () => { Zero.Switch(_ => { }, _ => { }); return null; }),
            new("Result`2", "SwitchAsync", () => { Zero.SwitchAsync(NoTask, _ => Task.CompletedTask); return null; }),
            new("Result`2", "MatchAsync", () => Zero.MatchAsync(Async, _ => Async(0))),
            new("Result`2", "ToOneOf", () => Zero.ToOneOf()),
            new("Result`2", "Bind", () => Zero.Bind(v => v.Ok<int, string>())),
            new("Result`2", "BindAsync", () => Zero.BindAsync(v => Async(v.Ok<int, string>()))),
            new("Result`2", "Map", () => Zero.Map(v => v)),
            new("Result`2", "MapAsync", () => Zero.MapAsync(Async)),
            new("Result`2", "MapError", () => Zero.MapError(e => e)),
            new("Result`2", "MapError", () => Zero.MapError(e => (IEnumerable<string>)[e])),
            new("Result`2", "MapErrorAsync", () => Zero.MapErrorAsync(Async)),
            new("Result`2", "MapErrorAsync", () => Zero.MapErrorAsync(e => Async((IEnumerable<string>)[e]))),
            new("Result`2", "Map2", () => Zero.Map2(e => e, v => v)),
            new("Result`2", "Map2Async", () => Zero.Map2Async(Async, Async)),
            new("Result`2", "Tee", () => Zero.Tee(_ => { })),
            new("Result`2", "TeeAsync", () => Zero.TeeAsync(NoTask)),
            new("Result`2", "TeeError", () => Zero.TeeError(_ => { })),
            new("Result`2", "TeeErrorAsync", () => Zero.TeeErrorAsync(_ => Task.CompletedTask)),
            new("Result`2", "DiscardError", () => Zero.DiscardError()),
            new("Result`2", "DiscardError", () => Zero.DiscardError(_ => { })),
            new("Result`2", "DiscardErrorAsync", () => Zero.DiscardErrorAsync(_ => Task.CompletedTask)),
            new("Result`2", "DiscardValue", () => Zero.DiscardValue()),
            new("Result`2", "DiscardValue", () => Zero.DiscardValue(_ => Result.Ok<string>())),
            new("Result`2", "DiscardValueAsync", () => Zero.DiscardValueAsync(_ => Async(Result.Ok<string>()))),
            new("Result`2", "ReturnOrValue", () => Zero.ReturnOrValue(_ => 0)),
            new("Result`2", "ReturnOrValueAsync", () => Zero.ReturnOrValueAsync(_ => Async(0))),
            new("Result`2", "ReturnOrFail", () => Zero.ReturnOrFail(e => new ArgumentException(e))),
            new("Result`2", "Cast", () => Zero.Cast<object>()),
            new("Result`2", "CastError", () => Zero.CastError<object>()),
            new("Result`2", "linq select", () => from v in Zero select v + 1),
            new("Result`2", "linq from from", () => from a in Ok from b in Zero select a + b),
            new("Result`2", "Plus", () => Ok.Plus(Zero, (a, b) => a + b)),
            new("Result`2", "Then", () => Zero.Then(v => v.Ok<int, string>())),
            new("Result`2", "Check", () => Zero.Check(_ => Result.Ok<string>())),
            new("Result`2", "Task.Map", () => Async(Zero).Map(v => v)),
            new("Result`2", "Task.Bind", () => Async(Zero).Bind(v => v.Ok<int, string>())),
            new("Result`2", "Task.Match", () => Async(Zero).Match(v => v, _ => 0)),
            new("Result`2", "Task.Tee", () => Async(Zero).Tee(_ => { })),
            new("Result`2", "Fold", () => new[] { Ok, Zero }.Fold(0, (sum, r) => r.Map(v => sum + v).ValueOr(sum))),
            new("Result`2", "implicit VoidResult", () => { VoidResult<string> v = default(Result<Unit, string>); return v; }),

            // Result<T>
            new("Result`1", "Value", () => ZeroList.Value),
            new("Result`1", "TryGetValue", () => ZeroList.TryGetValue(out _)),
            new("Result`1", "OrElse", () => ZeroList.OrElse(1.Ok())),
            new("Result`1", "ValueOr", () => ZeroList.ValueOr(0)),
            new("Result`1", "Match", () => ZeroList.Match(v => v, _ => 0)),
            new("Result`1", "Switch", () => { ZeroList.Switch(_ => { }, _ => { }); return null; }),
            new("Result`1", "SwitchAsync", () => { ZeroList.SwitchAsync(NoTask, _ => Task.CompletedTask); return null; }),
            new("Result`1", "MatchAsync", () => ZeroList.MatchAsync(Async, _ => Async(0))),
            new("Result`1", "ToOneOf", () => ZeroList.ToOneOf()),
            new("Result`1", "Bind", () => ZeroList.Bind(v => v.Ok())),
            new("Result`1", "BindAsync", () => ZeroList.BindAsync(v => Async(v.Ok()))),
            new("Result`1", "Map", () => ZeroList.Map(v => v)),
            new("Result`1", "MapAsync", () => ZeroList.MapAsync(Async)),
            new("Result`1", "MapError", () => ZeroList.MapError(e => e.Count())),
            new("Result`1", "MapErrorAsync", () => ZeroList.MapErrorAsync(e => Async(e.Count()))),
            new("Result`1", "Map2", () => ZeroList.Map2(e => e.Count(), v => v)),
            new("Result`1", "Map2Async", () => ZeroList.Map2Async(e => Async(e.Count()), Async)),
            new("Result`1", "Tee", () => ZeroList.Tee(_ => { })),
            new("Result`1", "TeeAsync", () => ZeroList.TeeAsync(NoTask)),
            new("Result`1", "TeeError", () => ZeroList.TeeError(_ => { })),
            new("Result`1", "TeeErrorAsync", () => ZeroList.TeeErrorAsync(_ => Task.CompletedTask)),
            new("Result`1", "DiscardError", () => ZeroList.DiscardError()),
            new("Result`1", "DiscardError", () => ZeroList.DiscardError(_ => { })),
            new("Result`1", "DiscardErrorAsync", () => ZeroList.DiscardErrorAsync(_ => Task.CompletedTask)),
            new("Result`1", "DiscardValue", () => ZeroList.DiscardValue()),
            new("Result`1", "DiscardValue", () => ZeroList.DiscardValue(_ => Result.Ok())),
            new("Result`1", "DiscardValueAsync", () => ZeroList.DiscardValueAsync(_ => Async(Result.Ok()))),
            new("Result`1", "ReturnOrValue", () => ZeroList.ReturnOrValue(_ => 0)),
            new("Result`1", "ReturnOrValueAsync", () => ZeroList.ReturnOrValueAsync(_ => Async(0))),
            new("Result`1", "ReturnOrFail", () => ZeroList.ReturnOrFail(_ => new ArgumentException())),
            new("Result`1", "Cast", () => ZeroList.Cast<object>()),
            new("Result`1", "linq select", () => from v in ZeroList select v + 1),
            new("Result`1", "Then", () => ZeroList.Then(v => v.Ok())),
            new("Result`1", "Check", () => ZeroList.Check(_ => Result.Ok())),
            new("Result`1", "Task.Map", () => Async(ZeroList).Map(v => v)),
            new("Result`1", "implicit Result<T>", () => { Result<int> r = default(Result<int, IEnumerable<string>>); return r; }),

            // VoidResult<TError>
            new("VoidResult`1", "Match", () => ZeroVoid.Match(_ => 1, _ => 0)),
            new("VoidResult`1", "Switch", () => { ZeroVoid.Switch(_ => { }, _ => { }); return null; }),
            new("VoidResult`1", "SwitchAsync", () => { ZeroVoid.SwitchAsync(_ => Task.CompletedTask, _ => Task.CompletedTask); return null; }),
            new("VoidResult`1", "MatchAsync", () => ZeroVoid.MatchAsync(_ => Async(1), _ => Async(0))),
            new("VoidResult`1", "ToOneOf", () => ZeroVoid.ToOneOf()),
            new("VoidResult`1", "Map", () => ZeroVoid.Map(_ => 1)),
            new("VoidResult`1", "Map", () => ZeroVoid.Map(1)),
            new("VoidResult`1", "MapAsync", () => ZeroVoid.MapAsync(_ => Async(1))),
            new("VoidResult`1", "MapAsync", () => ZeroVoid.MapAsync(Async(1))),
            new("VoidResult`1", "Map2", () => ZeroVoid.Map2(e => e, _ => 1)),
            new("VoidResult`1", "Map2Async", () => ZeroVoid.Map2Async(Async, () => Async(1))),
            new("VoidResult`1", "MapError", () => ZeroVoid.MapError(e => e)),
            new("VoidResult`1", "MapError", () => ZeroVoid.MapError(e => (IEnumerable<string>)[e])),
            new("VoidResult`1", "MapErrorAsync", () => ZeroVoid.MapErrorAsync(Async)),
            new("VoidResult`1", "MapErrorAsync", () => ZeroVoid.MapErrorAsync(e => Async((IEnumerable<string>)[e]))),
            new("VoidResult`1", "Bind", () => ZeroVoid.Bind(_ => Result.Ok<string>())),
            new("VoidResult`1", "BindAsync", () => ZeroVoid.BindAsync(_ => Async(Result.Ok<string>()))),
            new("VoidResult`1", "BindValue", () => ZeroVoid.BindValue(_ => Ok)),
            new("VoidResult`1", "BindValueAsync", () => ZeroVoid.BindValueAsync(_ => Async(Ok))),
            new("VoidResult`1", "Tee", () => ZeroVoid.Tee(_ => { })),
            new("VoidResult`1", "TeeAsync", () => ZeroVoid.TeeAsync(_ => Task.CompletedTask)),
            new("VoidResult`1", "TeeError", () => ZeroVoid.TeeError(_ => { })),
            new("VoidResult`1", "TeeErrorAsync", () => ZeroVoid.TeeErrorAsync(_ => Task.CompletedTask)),
            new("VoidResult`1", "Cast", () => ZeroVoid.Cast<object>()),
            new("VoidResult`1", "Then", () => ZeroVoid.Then(() => Result.Ok<string>())),
            new("VoidResult`1", "Then value", () => ZeroVoid.Then(() => Ok)),

            // VoidResult
            new("VoidResult", "Match", () => ZeroVoidList.Match(_ => 1, _ => 0)),
            new("VoidResult", "Switch", () => { ZeroVoidList.Switch(_ => { }, _ => { }); return null; }),
            new("VoidResult", "SwitchAsync", () => { ZeroVoidList.SwitchAsync(_ => Task.CompletedTask, _ => Task.CompletedTask); return null; }),
            new("VoidResult", "MatchAsync", () => ZeroVoidList.MatchAsync(_ => Async(1), _ => Async(0))),
            new("VoidResult", "ToOneOf", () => ZeroVoidList.ToOneOf()),
            new("VoidResult", "Map", () => ZeroVoidList.Map(_ => 1)),
            new("VoidResult", "Map", () => ZeroVoidList.Map(1)),
            new("VoidResult", "MapAsync", () => ZeroVoidList.MapAsync(_ => Async(1))),
            new("VoidResult", "MapAsync", () => ZeroVoidList.MapAsync(Async(1))),
            new("VoidResult", "Map2", () => ZeroVoidList.Map2(e => e.Count(), _ => 1)),
            new("VoidResult", "Map2Async", () => ZeroVoidList.Map2Async(e => Async(e.Count()), () => Async(1))),
            new("VoidResult", "MapError", () => ZeroVoidList.MapError(e => e.Count())),
            new("VoidResult", "MapError", () => ZeroVoidList.MapError(e => e)),
            new("VoidResult", "MapErrorAsync", () => ZeroVoidList.MapErrorAsync(e => Async(e.Count()))),
            new("VoidResult", "MapErrorAsync", () => ZeroVoidList.MapErrorAsync(Async)),
            new("VoidResult", "Bind", () => ZeroVoidList.Bind(_ => Result.Ok())),
            new("VoidResult", "BindAsync", () => ZeroVoidList.BindAsync(_ => Async(Result.Ok()))),
            new("VoidResult", "BindValue", () => ZeroVoidList.BindValue(_ => 1.Ok())),
            new("VoidResult", "BindValueAsync", () => ZeroVoidList.BindValueAsync(_ => Async(1.Ok()))),
            new("VoidResult", "Tee", () => ZeroVoidList.Tee(_ => { })),
            new("VoidResult", "TeeAsync", () => ZeroVoidList.TeeAsync(_ => Task.CompletedTask)),
            new("VoidResult", "TeeError", () => ZeroVoidList.TeeError(_ => { })),
            new("VoidResult", "TeeErrorAsync", () => ZeroVoidList.TeeErrorAsync(_ => Task.CompletedTask)),
            new("VoidResult", "Then", () => ZeroVoidList.Then(() => Result.Ok())),
            new("VoidResult", "Then value", () => ZeroVoidList.Then(() => 1.Ok())),

            // With no steps these would return the zero value unread, so they check it first.
            new("Result`2", "Then no steps", () => Zero.Then()),
            new("Result`2", "Check no checks", () => Zero.Check()),
            new("Result`1", "Then no steps", () => ZeroList.Then()),
            new("Result`1", "Check no checks", () => ZeroList.Check()),
            new("VoidResult`1", "Then no steps", () => ZeroVoid.Then()),
            new("VoidResult", "Then no steps", () => ZeroVoidList.Then()),
        ];

        // The law: no call on a zero value returns normally. Each throws InvalidOperationException that names the zero value.
        [Test]
        public void No_member_that_reads_a_zero_value_returns_normally()
        {
            Gen.OneOf(Calls).Sample(call =>
            {
                var error = Outcome(call.Run);
                Assert.That(error, Is.InstanceOf<InvalidOperationException>().With.Message.Contains("zero value (default)"), call.ToString());
            }, iter: Calls.Length * 20);
        }

        // Members that never throw on any value, the zero value included: the case flags, ToString, Equals, GetHashCode
        // and the debugger display.
        [Test]
        public void The_never_throw_members_never_throw()
        {
            var states = Gen.Int[0, 2];
            Gen.Select(states, states, Gen.Int[-2, 2]).Sample((a, b, v) =>
            {
                Result<int, string> R(int s) => s switch { 0 => default, 1 => v, _ => "e" + v };
                Result<int> L(int s) => s switch { 0 => default, 1 => v.Ok(), _ => Result.Fail<int>("e" + v) };
                VoidResult<string> V(int s) => s switch { 0 => default, 1 => Result.Ok<string>(), _ => ("e" + v).Fail() };
                VoidResult W(int s) => s switch { 0 => default, 1 => Result.Ok(), _ => Result.Fail("e" + v) };
                object[] left = [R(a), L(a), V(a), W(a)], right = [R(b), L(b), V(b), W(b)];
                // Error lists (i = 1, 3) are compared by reference, as in 0.2, so two failures built apart differ.
                for (var i = 0; i < left.Length; i++)
                {
                    var (x, y) = (left[i], right[i]);
                    Assert.That(x.Equals(y), Is.EqualTo(a == b && !(a == 2 && i % 2 == 1)), $"{x.GetType().Name} {a} {b}");
                    Assert.That(x.Equals(x), Is.True);
                    if (x.Equals(y)) Assert.That(x.GetHashCode(), Is.EqualTo(y.GetHashCode()));
                    Assert.That(x.ToString(), a == 0 ? Does.StartWith("<default ") : Does.StartWith("Resultful."));
                    Assert.That(Display(x), a == 0 ? Does.StartWith("<default ") : a == 1 ? Does.StartWith("Ok") : Does.StartWith("Fail("));
                    if (i < 2)
                    {
                        var flags = ((bool)x.GetType().GetProperty("IsSuccess")!.GetValue(x)!, (bool)x.GetType().GetProperty("IsFailure")!.GetValue(x)!);
                        Assert.That(flags, Is.EqualTo((a == 1, a == 2)));
                    }
                }
            });
        }

        static string Display(object value)
            => (string)value.GetType().GetProperty("DebuggerDisplay", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;

        static readonly string[] NeverThrow = ["IsSuccess", "IsFailure", "Equals", "GetHashCode", "ToString", "GetType", "Deconstruct"];

        // Every public instance member of the result types is either a never-throw member or has a call in the law above,
        // so a member added later is checked too. (Deconstruct is listed by hand for Result<T, TError>.)
        [TestCase(typeof(Result<int, string>), "Result`2")]
        [TestCase(typeof(Result<int>), "Result`1")]
        [TestCase(typeof(VoidResult<string>), "VoidResult`1")]
        [TestCase(typeof(VoidResult), "VoidResult")]
        public void Members_that_read_the_zero_value_are_all_listed(Type type, string name)
        {
            var listed = Calls.Where(c => c.Type == name).Select(c => c.Member).ToHashSet();
            var members = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Select(m => m.IsSpecialName ? m.Name.Substring(4) : m.Name)
                .Where(m => !NeverThrow.Contains(m))
                .Distinct()
                .Where(m => !listed.Contains(m));
            Assert.That(members, Is.Empty);
            Assert.That(type == typeof(Result<int, string>) ? listed.Contains("Deconstruct") : true, Is.True);
        }

        [Test]
        public void ToString_Equals_GetHashCode_and_the_debugger_display_name_the_zero_value()
        {
            Assert.That(Zero.ToString(), Is.EqualTo("<default Result>"));
            Assert.That(ZeroList.ToString(), Is.EqualTo("<default Result>"));
            Assert.That(ZeroVoid.ToString(), Is.EqualTo("<default VoidResult>"));
            Assert.That(ZeroVoidList.ToString(), Is.EqualTo("<default VoidResult>"));
            Assert.That(Ok.ToString(), Is.EqualTo("Resultful.Result`2[System.Int32,System.String]"), "a success or failure prints as in 0.2");
            Assert.That(Display(Zero), Is.EqualTo("<default Result>"));
            Assert.That(Display(Ok), Is.EqualTo("Ok(1)"));
            Assert.That(Display(Failed), Is.EqualTo("Fail(bad)"));
            Assert.That(Display(Result.Fail<int>("a", "b")), Is.EqualTo("Fail(a, b)"));
            Assert.That(Display(Result.Ok()), Is.EqualTo("Ok"));
            Assert.That(Display(ZeroVoidList), Is.EqualTo("<default VoidResult>"));
            Assert.That(typeof(Result<int, string>).GetCustomAttribute<System.Diagnostics.DebuggerDisplayAttribute>()?.Value, Is.EqualTo("{DebuggerDisplay,nq}"));

            Assert.That(Zero.Equals(default(Result<int, string>)), Is.True);
            Assert.That(Zero.Equals(0.Ok<int, string>()), Is.False, "the zero value isn't Ok(default(T))");
            Assert.That(Zero.Equals(((string?)null).Ok<string?, string>()), Is.False);
            Assert.That(ZeroVoid.Equals(Result.Ok<string>()), Is.False);
            Assert.That(Zero.GetHashCode(), Is.EqualTo(default(Result<int, string>).GetHashCode()));
        }

        // Decision 10, layer 2: new Result<T>() doesn't compile (CS0619). Reflection and new() constraints still reach the
        // constructor, and it throws.
        [TestCase(typeof(Result<int, string>))]
        [TestCase(typeof(Result<int>))]
        [TestCase(typeof(VoidResult<string>))]
        [TestCase(typeof(VoidResult))]
        public void The_parameterless_constructor_is_an_obsolete_error_and_throws(Type type)
        {
            var ctor = type.GetConstructor(Type.EmptyTypes)!;
            var obsolete = ctor.GetCustomAttribute<ObsoleteAttribute>();
            Assert.That(obsolete?.IsError, Is.True);
            Assert.That(obsolete!.Message, Does.Contain("Result.Ok or Result.Fail"));
            Assert.That(() => ctor.Invoke(null), Throws.InstanceOf<TargetInvocationException>().With.InnerException.InstanceOf<InvalidOperationException>());
        }

        static T New<T>() where T : new() => new T();

        [Test]
        public void A_new_constraint_reaches_the_constructor_and_throws()
            => Assert.That(Outcome(() => New<Result<int, string>>()), Is.InstanceOf<InvalidOperationException>()
                .Or.InstanceOf<TargetInvocationException>().With.InnerException.InstanceOf<InvalidOperationException>());

        // Decision 10 for Option: its zero value is None, a safe meaning, so nothing throws.
        [Test]
        public void The_zero_option_is_None()
        {
            var zero = default(Option<int>);
            Assert.That(zero.Equals(Option<int>.None), Is.True);
            Assert.That(zero.GetHashCode(), Is.EqualTo(Option<int>.None.GetHashCode()));
            Assert.That(zero.Match(_ => "some", _ => "none"), Is.EqualTo("none"));
            Assert.That(zero.Map(x => x + 1).Or(-1), Is.EqualTo(-1));
            Assert.That(zero.Equals(0.Some()), Is.False);
            Assert.That((from x in zero select x).ToOneOf().IsT1, Is.True);
        }

        // Scenario: a cache backed by an array whose slots start as the zero value. Before 08b a missing slot passed
        // through the pipeline as a success holding 0 and the order was "placed" with id 0.
        sealed class OrderService
        {
            readonly Result<int, string>[] _cache = new Result<int, string>[4];
            readonly Dictionary<Result<int, string>, int> _seen = [];
            public readonly List<string> Log = [];

            public void Store(int slot, Result<int, string> order) => _cache[slot] = order;

            public async Task<Result<string, string>> PlaceAsync(int slot)
            {
                var cached = _cache[slot];
                Log.Add($"placing {cached}");
                _seen[cached] = _seen.TryGetValue(cached, out var n) ? n + 1 : 1;
                if (!cached.IsSuccess && !cached.IsFailure) Log.Add("slot is uninitialised");
                return await Task.FromResult(cached)
                    .Map(id => id * 10)
                    .Bind(id => id > 0 ? id.Ok<int, string>() : "bad id".Fail<int, string>())
                    .MapAsync(id => Task.FromResult($"order-{id}"));
            }

            public int Seen(Result<int, string> key) => _seen.TryGetValue(key, out var n) ? n : 0;
        }

        [Test]
        public async Task An_uninitialised_cache_slot_fails_loudly_while_logging_and_dictionaries_keep_working()
        {
            var service = new OrderService();
            service.Store(1, 7);
            service.Store(2, "locked");

            Assert.That((await service.PlaceAsync(1)).Value, Is.EqualTo("order-70"));
            Assert.That((await service.PlaceAsync(2)).Error, Is.EqualTo("locked"));
            var error = Assert.ThrowsAsync<InvalidOperationException>(() => service.PlaceAsync(3));
            Assert.That(error!.Message, Does.Contain("Result is the zero value (default)"));
            Assert.That(() => service.PlaceAsync(0), Throws.InvalidOperationException);

            Assert.That(service.Log, Has.Some.EqualTo("placing <default Result>"));
            Assert.That(service.Log.Count(l => l == "slot is uninitialised"), Is.EqualTo(2));
            Assert.That(service.Seen(default), Is.EqualTo(2), "zero values are equal dictionary keys");
            Assert.That(service.Seen(7), Is.EqualTo(1));
        }

        // Scenario: an uninitialised field in a validation step, composed with Then, Plus and LINQ.
        sealed class Signup
        {
            public Result<string, string> Email;
            public Result<int, string> Age = 30;
        }

        [Test]
        public void An_unassigned_result_field_stops_a_composed_validation()
        {
            var signup = new Signup();
            Assert.That(() => signup.Email.Plus(signup.Age, (a, b) => a + b, (e, a) => $"{e}:{a}"), Throws.InvalidOperationException);
            Assert.That(() => from e in signup.Email from a in signup.Age select $"{e}:{a}", Throws.InvalidOperationException);
            Assert.That(() => signup.Age.Then(a => signup.Email.Map(_ => a)), Throws.InvalidOperationException);
            signup.Email = "a@b.c".Ok<string, string>();
            Assert.That((from e in signup.Email from a in signup.Age select $"{e}:{a}").Value, Is.EqualTo("a@b.c:30"));
        }

        // ZERO-NULL-ERR: a failure always holds an error.
        [Test]
        public void Fail_and_the_error_conversions_reject_null()
        {
            Assert.That(() => ((string)null!).Fail<int, string>(), Throws.ArgumentNullException);
            Assert.That(() => ((Error)null!).Fail(), Throws.ArgumentNullException);
            Assert.That(() => { VoidResult<string> v = (string)null!; }, Throws.ArgumentNullException);
            Assert.That(() => Result.Fail<int>((string[])null!), Throws.ArgumentNullException);
            Assert.That(() => Ok.Map2<int, string>(_ => null!, v => v), Throws.Nothing, "a success never builds a failure");
            Assert.That(() => Failed.MapError<string>(_ => null!), Throws.ArgumentNullException, "a mapped error is checked like any other");
            // The list builders read null as no errors in 0.2; a null list is now rejected like a null error.
            Assert.That(() => Result.Fail<int, string>((IEnumerable<string>)null!), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("errors"));
            Assert.That(() => ((IEnumerable<string>)null!).Fail<int, string>(), Throws.ArgumentNullException);
            Assert.That(() => Result.Fail<int, string>((string[])null!), Throws.ArgumentNullException);
            Assert.That(() => Result.Fail((IEnumerable<string>)null!), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("value"));
            Assert.That(() => Result.Fail((string[])null!), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("errors"));
            Assert.That(() => Result.Fail<string>((string[])null!), Throws.ArgumentNullException);
            Assert.That(Result.Fail<int, string>(Array.Empty<string>()).Error, Is.Empty, "an empty list is still a failure");
            Assert.That(() => default(int?).Fail<int, int?>(), Throws.ArgumentNullException);
        }

        static long Allocated(Action action)
        {
            action();
            action();
            var before = GC.GetAllocatedBytesForCurrentThread();
            action();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

#if NET11_0_OR_GREATER
        public sealed record OrderNotFound(int Id) : Error("Order not found.");
        public sealed record OrderLocked(int Id) : Error("Order locked.");
        public union OrderError(OrderNotFound, OrderLocked);

        // A hand-written IUnion whose one field is a long. 42 means "no case", so neither a comparison with default nor
        // reading the long as a reference gives the right answer: only IUnion.Value does.
        // A hand-written [Union] struct with one object field, like the compiler's, but its own meaning: a sentinel object
        // means "no case", and a null field is a case. Reading the field in place would get both wrong.
        [Union]
        public readonly struct Sentinel : IUnion
        {
            static readonly object NoCase = new();
            readonly object? _held;

            public Sentinel(string held) => _held = held;

            Sentinel(object held, bool _) => _held = held;

            public static Sentinel Empty => new(NoCase, true);

            public object? Value => _held is null ? "a null field is a case" : ReferenceEquals(_held, NoCase) ? null : _held;
        }

        [Test]
        public void A_hand_written_Union_struct_is_asked_through_IUnion_not_read_in_place()
        {
            Assert.That(() => Sentinel.Empty.Fail<int, Sentinel>(), Throws.ArgumentException);
            Assert.That(default(Sentinel).Fail<int, Sentinel>().IsFailure, Is.True);
            Assert.That(Allocated(() => default(Sentinel).Fail<int, Sentinel>()), Is.Positive, "boxed to ask IUnion.Value");
        }

        // The type argument may hide the union: a Nullable of it, an interface or object. The runtime value is asked.
        [Test]
        public void A_union_behind_Nullable_an_interface_or_object_is_still_checked()
        {
            OrderError locked = new OrderLocked(2);
            Assert.That(() => ((OrderError?)default(OrderError)).Fail<int, OrderError?>(), Throws.ArgumentException);
            Assert.That(() => ((OrderError?)null).Fail<int, OrderError?>(), Throws.ArgumentNullException);
            Assert.That(((OrderError?)locked).Fail<int, OrderError?>().IsFailure, Is.True);
            Assert.That(() => Result.Fail<int, IUnion>(default(OrderError)), Throws.ArgumentException);
            Assert.That(() => Result.Fail<int, object>(default(OrderError)), Throws.ArgumentException);
            Assert.That(() => Result.Fail<IUnion>(new Tagged(42)), Throws.ArgumentException);
            Assert.That(Result.Fail<int, IUnion>(locked).IsFailure, Is.True);
            Assert.That(Result.Fail<int, object>(new Tagged(0)).IsFailure, Is.True);
            Error error = new OrderLocked(1);
            Assert.That(Allocated(() => error.Fail<int, Error>()), Is.Zero, "a non-sealed error type is checked without allocating");
        }

        public readonly struct Tagged(long tag) : IUnion
        {
            public object? Value => tag == 42 ? null : tag;
        }

        // UERR-ZERO-NOALLOC: Fail rejects a union with no case, and accepts one with a case, allocating nothing.
        [Test]
        public void Fail_rejects_an_error_union_with_no_case_without_allocating()
        {
            OrderError locked = new OrderLocked(2);
            Assert.That(locked.Fail<int, OrderError>().Error.Value, Is.EqualTo(new OrderLocked(2)));
            Assert.That(() => default(OrderError).Fail<int, OrderError>(), Throws.ArgumentException.With.Message.Contains("OrderError"));
            Assert.That(() => default(OrderError).Fail(), Throws.ArgumentException);
            Assert.That(() => { Result<int, OrderError> r = default(OrderError); }, Throws.ArgumentException);
            Assert.That(() => default(OrderError).Fail<int, OrderError>(), Throws.Exception.Not.InstanceOf<ArgumentNullException>());

            var sink = 0;
            Assert.That(Allocated(() => sink += locked.Fail<int, OrderError>().IsFailure ? 1 : 0), Is.Zero);
            Assert.That(Allocated(() => sink += locked.Fail().Match(static _ => 1, static _ => 2)), Is.Zero);
            Assert.That(sink, Is.Not.Zero);
        }

        // UERR-CUSTOM-IUNION: a hand-written IUnion takes the IUnion.Value path (it boxes), and is never read as a reference.
        [Test]
        public void A_hand_written_IUnion_holding_a_long_is_asked_through_IUnion()
        {
            Assert.That(() => new Tagged(42).Fail<int, Tagged>(), Throws.ArgumentException);
            Assert.That(() => new Tagged(42).Fail(), Throws.ArgumentException);
            Assert.That(new Tagged(0).Fail<int, Tagged>().IsFailure, Is.True, "0 is a case here, though a zero reference would read as null");
            Assert.That(default(Tagged).Fail<int, Tagged>().Error.Value, Is.EqualTo(0L));
            Assert.That(new Tagged(7).Fail<int, Tagged>().Error.Value, Is.EqualTo(7L));
            Assert.That(Allocated(() => new Tagged(7).Fail<int, Tagged>()), Is.Positive, "Tagged is boxed to ask IUnion.Value");
        }

        // The zero-allocation path reads a compiler-generated union's one field in place. This pins the layout it relies
        // on, so a compiler that changes it fails here rather than misreading memory.
        [Test]
        public void A_compiler_generated_union_has_the_layout_Fail_reads()
        {
            var type = typeof(OrderError);
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(type.IsDefined(typeof(UnionAttribute), inherit: false), Is.True);
            Assert.That(typeof(IUnion).IsAssignableFrom(type), Is.True);
            Assert.That(fields.Select(f => f.FieldType), Is.EqualTo(new[] { typeof(object) }));
            Assert.That(Unsafe.SizeOf<OrderError>(), Is.EqualTo(IntPtr.Size));
            Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<OrderError>(), Is.True);
            OrderError u = new OrderNotFound(1);
            Assert.That(fields[0].GetValue(u), Is.SameAs(u.Value), "the field is the object Value returns");
            Assert.That(fields[0].GetValue(default(OrderError)), Is.Null);
        }
#endif
    }
}
