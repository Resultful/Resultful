using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
using NUnit.Framework;

namespace Resultful.Tests
{
    // Decision 14 (12): a step with no value is a Result<Unit> or Result<Unit, TError>. Result.Ok() returns one, and
    // VoidResult and VoidResult<TError> are obsolete. The scenario closes a customer account with a command pipeline whose
    // steps return nothing, written with Result<Unit> end to end: sync and async steps, a check on a loaded value, a typed
    // error form, and the service wrapping the first failure as the Cause of its own error. The laws hold the conversions
    // between VoidResult and Result<Unit> to lossless round trips, and every obsolete member to what its Result<Unit>
    // replacement does, so code that hasn't moved yet keeps its behaviour.
    [TestFixture]
    public class UnitResultTests
    {
        // ---- The domain ----

        sealed record NoSuchAccount(int Id) : Error($"no account {Id}");
        sealed record AccountLocked(int Id) : Error($"account {Id} is locked");
        sealed record BalanceNotZero(int Id, decimal Balance) : Error($"account {Id} still holds {Balance}");
        sealed record GatewayDown(string Gateway) : Error($"{Gateway} did not answer");
        sealed record CloseFailed(int Id) : Error($"account {Id} was not closed");

        sealed record Account(int Id, decimal Balance, bool Locked, bool Subscribed);

        sealed class Bank
        {
            public Dictionary<int, Account> Accounts { get; } = new()
            {
                [1] = new(1, 0m, false, true),
                [2] = new(2, 0m, true, false),
                [3] = new(3, 25m, false, false),
                [4] = new(4, 0m, false, false),
            };
            public bool GatewayIsDown { get; set; }
            public List<string> Log { get; } = [];

            Result<Account> Load(int id)
            {
                Log.Add($"load {id}");
                return Accounts.TryGetValue(id, out var account) ? account : new NoSuchAccount(id);
            }

            // Steps with no value: each returns Result<Unit>, through Result.Ok(), an Error, or Unit.Value.
            Result<Unit> EnsureUnlocked(Account a)
            {
                Log.Add($"check lock {a.Id}");
                return a.Locked ? new AccountLocked(a.Id) : Result.Ok();
            }

            Result<Unit> EnsureEmpty(Account a)
                => a.Balance == 0m ? Unit.Value : new BalanceNotZero(a.Id, a.Balance);

            Result<Unit> Unsubscribe(int id)
            {
                Log.Add($"unsubscribe {id}");
                return Result.Ok();
            }

            async Task<Result<Unit>> RevokeCardsAsync(int id)
            {
                await Task.Yield();
                Log.Add($"revoke {id}");
                return GatewayIsDown ? new GatewayDown("cards") : Result.Ok();
            }

            Result<Unit> Archive(int id)
            {
                Log.Add($"archive {id}");
                Accounts.Remove(id);
                return Result.Ok();
            }

            Result<Unit> Notify(int id)
            {
                Log.Add($"notify {id}");
                return Result.Ok();
            }

            // The command: load and check (a value, then no value), then steps with no value, sync and async, stopping at
            // the first failure, which becomes the Cause of CloseFailed.
            public Task<Result<Unit>> CloseAsync(int id)
                => Load(id)
                    .Check(EnsureUnlocked, EnsureEmpty)
                    .DiscardValue()
                    .Then(() => Unsubscribe(id))
                    .ThenAsync(() => RevokeCardsAsync(id))
                    .Then(() => Archive(id), () => Notify(id))
                    .MapError(e => new CloseFailed(id) { Cause = e });

            // The same steps as one Sequence, for callers that already hold a valid account.
            public Result<Unit> Tidy(Account a)
                => Result.Sequence(() => EnsureUnlocked(a), () => Unsubscribe(a.Id), () => Notify(a.Id));
        }

        // A typed form: the error is an enum-like record, not an Error, and the steps are Result<Unit, string>.
        static Result<Unit, string> Step(List<string> log, string name, bool ok)
        {
            log.Add(name);
            return ok ? Result.Ok<string>() : name + " failed";
        }

        // ---- The scenario ----

        [Test]
        public async Task Closing_an_account_runs_every_step_in_order()
        {
            var bank = new Bank();
            var closed = await bank.CloseAsync(1);

            Assert.That(closed, Is.EqualTo(Result.Ok()));
            Assert.That(closed.Value, Is.SameAs(Unit.Value));
            Assert.That(bank.Log, Is.EqualTo((string[])["load 1", "check lock 1", "unsubscribe 1", "revoke 1", "archive 1", "notify 1"]));
            Assert.That(bank.Accounts.ContainsKey(1), Is.False);
        }

        [Test]
        public async Task The_first_failure_stops_the_command_and_becomes_the_cause()
        {
            var bank = new Bank();

            var missing = await bank.CloseAsync(9);
            Assert.That(missing.Error, Is.EqualTo(new CloseFailed(9) { Cause = new NoSuchAccount(9) }));

            var locked = await bank.CloseAsync(2);
            Assert.That(locked.Error.TryFind<AccountLocked>(out var l) ? l.Id : 0, Is.EqualTo(2));

            var notEmpty = await bank.CloseAsync(3);
            Assert.That(notEmpty.Error.Cause, Is.EqualTo(new BalanceNotZero(3, 25m)));
            Assert.That(bank.Log, Is.EqualTo((string[])["load 9", "load 2", "check lock 2", "load 3", "check lock 3"]), "nothing after a failed check runs");

            bank.Log.Clear();
            bank.GatewayIsDown = true;
            var down = await bank.CloseAsync(4);
            Assert.That(down.Error.Cause, Is.EqualTo(new GatewayDown("cards")));
            Assert.That(bank.Log, Is.EqualTo((string[])["load 4", "check lock 4", "unsubscribe 4", "revoke 4"]));
            Assert.That(bank.Accounts.ContainsKey(4), Is.True, "a failed async step stops the sync steps after it");
        }

        [Test]
        public void A_sequence_of_steps_with_no_value_is_a_Result_of_Unit()
        {
            var bank = new Bank();
            Result<Unit> tidy = bank.Tidy(bank.Accounts[1]);
            Result<Unit> refused = bank.Tidy(bank.Accounts[2]);

            Assert.That(tidy.IsSuccess, Is.True);
            Assert.That(refused.Error, Is.EqualTo(new AccountLocked(2)));
            Assert.That(bank.Log, Is.EqualTo((string[])["check lock 1", "unsubscribe 1", "notify 1", "check lock 2"]));
        }

        [Test]
        public async Task The_typed_form_stops_at_the_first_failure_too()
        {
            var log = new List<string>();
            var result = await Result.Sequence<string>(() => Step(log, "a", true), () => Step(log, "b", true))
                .Then(() => Step(log, "c", false), () => Step(log, "d", true))
                .ThenAsync(() => Task.FromResult(Step(log, "e", true)));

            Assert.That(result, Is.EqualTo("c failed".Fail()));
            Assert.That(log, Is.EqualTo((string[])["a", "b", "c"]));

            var asyncOnly = await Result.SequenceAsync<string>(() => Task.FromResult(Step(log, "f", true)));
            Assert.That(asyncOnly, Is.EqualTo(Unit.Value.Ok<Unit, string>()));
        }

        [Test]
        public void Ok_and_Fail_build_a_Result_of_Unit()
        {
            Result<Unit> ok = Result.Ok();
            Result<Unit, string> typed = Result.Ok<string>();
            Result<Unit> failed = Result.Fail("bad");
            Result<Unit, string> typedFailed = "bad".Fail();
            Result<Unit, IEnumerable<string>> many = Result.Fail("a", "b");

            Assert.That(ok, Is.EqualTo(Unit.Value.Ok()));
            Assert.That(typed, Is.EqualTo(Unit.Value.Ok<Unit, string>()));
            Assert.That(failed.Error, Is.EqualTo(Error.From("bad")));
            Assert.That(typedFailed.Error, Is.EqualTo("bad"));
            Assert.That(many.Error, Is.EqualTo((string[])["a", "b"]));
            Assert.That(5.Ok().DiscardValue(), Is.EqualTo(Result.Ok()));
            Assert.That(5.Ok<int, string>().DiscardValue(), Is.EqualTo(Result.Ok<string>()));
        }

        // ---- The laws ----

        static readonly Gen<Error> Errors = Gen.Int[0, 3].Select(i => Error.From("e" + i));
        static readonly Gen<string> Messages = Gen.Int[0, 3].Select(i => "m" + i);

        // A Result<Unit> (and Result<Unit, string>) in either case; the zero value is pinned separately.
        static readonly Gen<Result<Unit>> Units = Gen.Frequency((1, Gen.Const(Result.Ok())), (2, Errors.Select(Result.Fail)));
        static readonly Gen<Result<Unit, string>> TypedUnits = Gen.Frequency((1, Gen.Const(Result.Ok<string>())), (2, Messages.Select(m => m.Fail())));

#pragma warning disable CS0618 // VoidResult is obsolete (decision 14); these laws pin that it still behaves as before.
        [Test]
        public void VoidResult_and_Result_of_Unit_round_trip_without_loss()
        {
            Units.Sample(r =>
            {
                VoidResult v = r;
                Result<Unit> back = v;
                Assert.That(back, Is.EqualTo(r));
                Assert.That(v, Is.EqualTo((VoidResult)back));
                if (r.IsFailure) Assert.That(back.Error, Is.SameAs(r.Error), "the same Error object, not a copy");
            });
            TypedUnits.Sample(r =>
            {
                VoidResult<string> v = r;
                Result<Unit, string> back = v;
                Assert.That(back, Is.EqualTo(r));
                if (r.IsFailure) Assert.That(back.Error, Is.SameAs(r.Error));
            });

            Assert.That(() => { Result<Unit> r = default(VoidResult); }, Throws.InvalidOperationException);
            Assert.That(() => { VoidResult v = default(Result<Unit>); }, Throws.InvalidOperationException);
            Assert.That(() => { Result<Unit, string> r = default(VoidResult<string>); }, Throws.InvalidOperationException);
            Assert.That(() => { VoidResult<string> v = default(Result<Unit, string>); }, Throws.InvalidOperationException);
        }

        [Test]
        public void Every_obsolete_VoidResult_member_does_what_its_Result_of_Unit_replacement_does()
        {
            Gen.Select(Units, Units, Errors).Sample((r, next, other) =>
            {
                VoidResult v = r;
                VoidResult w = next;
                Assert.That(v.Match(_ => "ok", e => e.Message), Is.EqualTo(r.Match(_ => "ok", e => e.Message)));
                Assert.That(v.Map(_ => 1), Is.EqualTo(r.Map(_ => 1)));
                Assert.That(v.Map(2), Is.EqualTo(r.Map(_ => 2)));
                Assert.That((Result<Unit>)v.Bind(_ => w), Is.EqualTo(r.Bind(_ => next)));
                Assert.That(v.BindValue(_ => 3.Ok()), Is.EqualTo(r.Bind(_ => 3.Ok())));
                Assert.That((Result<Unit>)v.MapError(_ => other), Is.EqualTo(r.MapError(_ => other)));
                Assert.That(v.MapError(e => e.Message), Is.EqualTo((VoidResult<string>)r.Map2(e => e.Message, u => u)));
                Assert.That(v.Map2(e => e.Message, _ => 4), Is.EqualTo(r.Map2(e => e.Message, _ => 4)));
                Assert.That((Result<Unit>)v.Then(() => w, () => w), Is.EqualTo(r.Then(() => next, () => next)));
                Assert.That(v.Then(() => 5.Ok()), Is.EqualTo(r.Then(() => 5.Ok())));
                Assert.That((Result<Unit>)v.ThenAsync(() => Task.FromResult(w)).Result, Is.EqualTo(r.ThenAsync(() => Task.FromResult(next)).Result));
                Assert.That((Result<Unit>)Task.FromResult(v).Bind(_ => w).Result, Is.EqualTo(r.Bind(_ => next)));
                Assert.That((Result<Unit>)new[] { v, w }.ReduceUntil(), Is.EqualTo(r.Then(() => next)));

                var (fromVoid, fromUnit) = (new List<string>(), new List<string>());
                Assert.That((Result<Unit>)v.Tee(_ => fromVoid.Add("ok")).TeeError(e => fromVoid.Add(e.Message)), Is.EqualTo(r));
                _ = r.Tee(_ => fromUnit.Add("ok")).TeeError(e => fromUnit.Add(e.Message));
                Assert.That(fromVoid, Is.EqualTo(fromUnit));
            });

            Gen.Select(TypedUnits, TypedUnits).Sample((r, next) =>
            {
                VoidResult<string> v = r;
                VoidResult<string> w = next;
                Assert.That((Result<Unit, string>)v.Bind(_ => w), Is.EqualTo(r.Bind(_ => next)));
                Assert.That((Result<Unit, string>)v.MapError(e => e + "!"), Is.EqualTo(r.MapError(e => e + "!")));
                Assert.That((Result<Unit, string>)v.Then(() => w), Is.EqualTo(r.Then(() => next)));
                Assert.That(v.BindValue(_ => 6.Ok<int, string>()), Is.EqualTo(r.Bind(_ => 6.Ok<int, string>())));
                Assert.That((Result<Unit, string>)v.Cast<string>(), Is.EqualTo(r.CastError<string>()));
                // Plus still gathers, as in 0.2: both errors merged, else the one failure.
                var plus = (Result<Unit, string>)v.Plus(w, (a, b) => a + "+" + b);
                var expected = r.IsSuccess ? next : next.IsSuccess ? r : (r.Error + "+" + next.Error).Fail();
                Assert.That(plus, Is.EqualTo(expected));
            });
        }

        [Test]
        public void Code_written_against_VoidResult_still_compiles_into_the_Result_of_Unit_members()
        {
            // A lambda that returns a VoidResult converts its return to Result<Unit>, so Sequence, Check and DiscardValue
            // take the old steps unchanged; only a method group (CS0407) and a var holding Result.Ok() need an edit.
            VoidResult Legacy(bool ok) => ok ? Result.Ok() : Result.Fail("legacy");
            Gen.Select(Gen.Bool, Gen.Bool).Sample((a, b) =>
            {
                Result<Unit> sequence = Result.Sequence(() => Legacy(a), () => Legacy(b));
                Assert.That(sequence, Is.EqualTo(a && b ? Result.Ok() : Result.Fail("legacy")));
                Assert.That(1.Ok().Check(_ => Legacy(a)).IsSuccess, Is.EqualTo(a));
                Assert.That(1.Ok().DiscardValue(_ => Legacy(a)).IsSuccess, Is.EqualTo(a));
            });
        }
#pragma warning restore CS0618
    }
}
