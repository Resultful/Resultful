using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using NUnit.Framework;

namespace Resultful.Tests
{
    // Decision 14 (10): Validation gathers every error into a non-empty list; Result stops at the first. The scenario is a
    // sign-up: independent field checks are combined so every mistake is reported at once, then the valid request moves
    // into a Result pipeline (ToResult) where domain steps fail with their own records and the edge matches on types.
    // The laws hold Combine, ToResult, the non-empty list and equality to their definitions, with random errors.
    [TestFixture]
    public class ValidationTests
    {
        // ---- The domain ----

        abstract record InputError(string Field, string Text) : Error(Text);
        sealed record Required(string Field) : InputError(Field, $"{Field} is required");
        sealed record TooShort(string Field, int Min) : InputError(Field, $"{Field} needs at least {Min} characters");
        sealed record NotAnEmail(string Field) : InputError(Field, $"{Field} is not an email address");
        sealed record TooYoung(string Field, int Min) : InputError(Field, $"{Field} must be at least {Min}");

        sealed record EmailTaken(string Email) : Error($"{Email} is already registered");
        sealed record TermsNotAccepted() : Error("the terms were not accepted");
        sealed record StoreDown() : Error("the account store is down");
        sealed record SignUpFailed(string Email) : Error($"{Email} was not signed up");

        sealed record SignUpRequest(string? Name, string? Email, string? Password, int Age, bool AcceptedTerms);
        sealed record SignUp(string Name, string Email, string Password, int Age);
        sealed record Account(int Id, string Name, string Email);

        // Pure checks, one per field. Each returns a value or its own error; none depends on another.
        static Validation<string, InputError> Name(string? v) =>
            string.IsNullOrWhiteSpace(v) ? new Required("name") : v.Trim();

        static Validation<string, InputError> EmailOf(string? v) =>
            string.IsNullOrWhiteSpace(v) ? new Required("email")
            : v.Contains('@', StringComparison.Ordinal) ? v.ToLowerInvariant()
            : new NotAnEmail("email");

        static Validation<string, InputError> Password(string? v) =>
            v is null ? new Required("password")
            : v.Length < 12 ? new TooShort("password", 12)
            : v;

        static Validation<int, InputError> Age(int v) =>
            v < 16 ? new TooYoung("age", 16) : v;

        static Validation<SignUp, InputError> Validate(SignUpRequest r) =>
            Validation.Combine(Name(r.Name), EmailOf(r.Email), Password(r.Password), Age(r.Age),
                (name, email, password, age) => new SignUp(name, email, password, age));

        // A check with no value: a Result<Unit> (VoidResult before 0.3), which converts to Validation<Unit>.
        static Result<Unit> Terms(bool accepted) =>
            accepted ? Result.Ok() : new TermsNotAccepted();

        sealed class Accounts
        {
            public List<Account> Saved { get; } = [];
            public bool Down { get; set; }
            public List<string> Log { get; } = [];

            Result<Account> Insert(SignUp s)
            {
                if (Down) return new StoreDown();
                var account = new Account(Saved.Count + 1, s.Name, s.Email);
                Saved.Add(account);
                return account;
            }

            // A dependent step: it needs a valid request, and its failure stops the chain.
            public Result<Account> Register(SignUp s) =>
                Saved.Any(a => a.Email == s.Email)
                    ? new EmailTaken(s.Email)
                    : Insert(s).MapError(e => new SignUpFailed(s.Email) { Cause = e });

            // Validate first (gathers), then act (stops at the first failure).
            public Result<Account> Handle(SignUpRequest r) =>
                Validate(r).ToResult()
                    .Bind(Register)
                    .TeeError(e => Log.Add(e.ToString()))
                    .WithContext("signing up");

            // The same with a check whose errors are Error, not InputError: AsValidation widens the input's list, flat, so
            // Validation<T> combines them all.
            public Result<Account> HandleWithTerms(SignUpRequest r)
            {
                Validation<Unit> terms = Terms(r.AcceptedTerms);
                Validation<SignUp> input = Validate(r).AsValidation();
                return Validation.Combine(input, terms, (s, _) => s).ToResult()
                    .Bind(Register)
                    .WithContext("signing up");
            }
        }

        // The edge: a status and body from the error's type, never from its text.
        static (int Status, string Body) Respond(Result<Account> result)
        {
            if (!result.TryGetError(out var error)) return (201, $"account {result.Value.Id}");
            if (error.TryFind<ValidationFailed<InputError>>(out var invalid))
            {
                var fields = invalid.Errors.GroupBy(e => e.Field).Select(g => $"{g.Key}: {string.Join(", ", g.Select(e => e.Message))}");
                return (400, string.Join("; ", fields));
            }

            return error switch
            {
                ValidationFailed<Error> v => (400, string.Join("; ", v.Errors.Select(e => e is InputError i ? i.Field : e.Message))),
                EmailTaken => (409, error.Message),
                _ => (500, "something went wrong"),
            };
        }

        static readonly string[] SigningUp = ["signing up"];
        static readonly string[] InputThenTerms = ["Required", "TermsNotAccepted"];
        static readonly string[] FormTwice = ["form", "form"];
        static readonly string[] NameAndEmail = ["name", "email"];

        static readonly SignUpRequest Good = new("Ada", "ADA@example.com", "correct horse battery", 36, true);

        [Test]
        public void A_good_request_signs_up()
        {
            var accounts = new Accounts();
            var result = accounts.Handle(Good);

            Assert.That(Respond(result), Is.EqualTo((201, "account 1")));
            Assert.That(accounts.Saved.Single(), Is.EqualTo(new Account(1, "Ada", "ada@example.com")));
            Assert.That(accounts.Log, Is.Empty);
        }

        [Test]
        public void Every_bad_field_is_reported_at_once_in_order_and_nothing_is_saved()
        {
            var accounts = new Accounts();
            var request = new SignUpRequest(" ", "ada", "short", 12, true);

            var validation = Validate(request);
            Assert.That(validation.Errors, Is.EqualTo(new InputError[]
            {
                new Required("name"), new NotAnEmail("email"), new TooShort("password", 12), new TooYoung("age", 16),
            }));

            var result = accounts.Handle(request);
            Assert.That(Respond(result), Is.EqualTo((400, "name: name is required; email: email is not an email address; password: password needs at least 12 characters; age: age must be at least 16")));
            var failed = (ValidationFailed<InputError>)result.Error;
            Assert.That(failed.Errors, Is.EqualTo(validation.Errors), "ToResult keeps every error, in order");
            Assert.That(failed.Context, Is.EqualTo(SigningUp));
            Assert.That(accounts.Saved, Is.Empty, "Register didn't run");
            Assert.That(accounts.Log.Single(), Does.StartWith("ValidationFailed<InputError>: Validation failed. [Required: name is required; "));
        }

        [Test]
        public void The_endpoint_can_match_on_the_list_without_a_Result()
        {
            // errors-in-practice.md, use case 2: the list is typed, so the field is a property, not parsed from text.
            var body = Validate(new SignUpRequest(null, null, null, 20, true)).Match(
                _ => "ok",
                errors => string.Join(",", errors.Select(e => e.Field)));
            Assert.That(body, Is.EqualTo("name,email,password"));
        }

        [Test]
        public void A_domain_failure_after_validation_stops_the_chain()
        {
            var accounts = new Accounts();
            _ = accounts.Handle(Good);

            var taken = accounts.Handle(Good with { Name = "Ada again" });
            Assert.That(Respond(taken), Is.EqualTo((409, "ada@example.com is already registered")));
            Assert.That(taken.Error, Is.EqualTo(new EmailTaken("ada@example.com") { Context = ["signing up"] }));

            accounts.Down = true;
            var down = accounts.Handle(Good with { Email = "grace@example.com" });
            Assert.That(Respond(down), Is.EqualTo((500, "something went wrong")));
            Assert.That(down.Error.TryFind<StoreDown>(out _), Is.True, "the store's error is the Cause");
            Assert.That(accounts.Saved, Has.Count.EqualTo(1));
        }

        [Test]
        public void Checks_with_different_error_types_meet_in_Validation_of_T()
        {
            var accounts = new Accounts();

            Assert.That(Respond(accounts.HandleWithTerms(Good)), Is.EqualTo((201, "account 1")));

            var both = accounts.HandleWithTerms(new SignUpRequest("", "x@y", "correct horse battery", 30, false));
            var failed = (ValidationFailed<Error>)both.Error;
            Assert.That(failed.Errors.Select(e => e.GetType().Name), Is.EqualTo(InputThenTerms),
                "the input's errors, flat, then the terms check: nothing is dropped or nested");
            Assert.That(failed.TryFind<Required>(out var required) && required.Field == "name", Is.True, "TryFind searches every error");
            Assert.That(Respond(both), Is.EqualTo((400, "name; the terms were not accepted")));
        }

        [Test]
        public void AsValidation_widens_and_keeps_the_errors_flat()
        {
            Gen.Select(Gen.Bool, Gen.Int, LeafLists).Sample((ok, value, leaves) =>
            {
                var typed = ok ? Validation.Ok<int, Leaf>(value) : Validation.Fail<int, Leaf>(leaves);
                var wide = typed.AsValidation();
                Assert.That(wide.IsSuccess, Is.EqualTo(ok));
                Assert.That(ok ? wide.Value : (object)wide.Errors, Is.EqualTo(ok ? value : (object)leaves));
                Assert.That(wide.ToResult(), Is.EqualTo(ok ? value.Ok() : new ValidationFailed<Error>(leaves).Fail<int>()));
            });
            Assert.That(() => default(Validation<int, Leaf>).AsValidation(), Throws.InvalidOperationException);
        }

        [Test]
        public void ToResult_with_a_function_says_how_many_errors_become_one()
        {
            var first = Validate(new SignUpRequest("", "", "", 1, true)).ToResult(errors => errors[0]);
            Assert.That(first.Error, Is.EqualTo(new Required("name")));

            Validation<int> two = Validation.Fail<int>([Error.From("a"), Error.From("b")]);
            Assert.That(two.ToResult(es => Error.From(string.Join("+", es.Select(e => e.Message)))).Error, Is.EqualTo(Error.From("a+b")));
            Assert.That(Validation.Ok<int>(3).ToResult(_ => throw new InvalidOperationException("not called")).Value, Is.EqualTo(3));
        }

        [Test]
        public void MapError_and_Tee_work_on_each_side()
        {
            var noted = Validation.Fail<int>([Error.From("a"), Error.From("b")]).MapError(e => e.WithContext("form"));
            Assert.That(noted.Errors.Select(e => e.Context.Single()), Is.EqualTo(FormTwice));

            var asText = Validate(new SignUpRequest("", "", "long enough password", 20, true)).MapError(e => e.Field);
            Assert.That(asText.Errors, Is.EqualTo(NameAndEmail));

            var seen = new List<int>();
            Assert.That(Validation.Ok<int, string>(1).Tee(seen.Add).Value, Is.EqualTo(1));
            _ = Validation.Fail<int, string>("e").Tee(seen.Add);
            Assert.That(seen.Single(), Is.EqualTo(1));
        }

#pragma warning disable CS0618 // VoidResult is obsolete (decision 14); a legacy check that still returns one converts as in 10.
        static VoidResult LegacyTerms(bool accepted) =>
            accepted ? Result.Ok() : new TermsNotAccepted();

        [Test]
        public void VoidResult_converts_to_Result_of_Unit_and_back()
        {
            Result<Unit> ok = LegacyTerms(true);
            Result<Unit> failed = LegacyTerms(false);
            Assert.That(ok, Is.EqualTo(Unit.Value.Ok()));
            Assert.That(failed.Error, Is.EqualTo(new TermsNotAccepted()));
            VoidResult back = failed;
            Assert.That(back.Match(_ => (Error?)null, e => e), Is.EqualTo(new TermsNotAccepted()));

            Result<Unit, string> typed = "e".Fail();
            Assert.That(typed.Error, Is.EqualTo("e"));
            Assert.That(((Result<Unit, string>)Result.Ok<string>()).IsSuccess, Is.True);
        }
#pragma warning restore CS0618

        [Test]
        public void A_failed_Validation_is_never_empty()
        {
            Assert.That(() => Validation.Fail<int, string>([]), Throws.ArgumentException.With.Message.Contains("at least one"));
            Assert.That(() => Validation.Fail<int>(Array.Empty<Error>()), Throws.ArgumentException.With.Message.Contains("at least one"));
            Assert.That(() => Validation.Fail<int>((IEnumerable<Error>)null!), Throws.ArgumentNullException);
            Assert.That(() => Validation.Fail<int>([Error.From("a"), null!]), Throws.ArgumentNullException);
            Assert.That(() => Validation.Fail<int>((Error)null!), Throws.ArgumentNullException);
            Assert.That(() => { Validation<int, string> v = (string)null!; return v; }, Throws.ArgumentNullException);
            Assert.That(() => Validation.Fail<int, string>(["a"]).MapError(_ => (string)null!), Throws.ArgumentNullException);
            Assert.That(() => Validation.Fail<int, string>(["a"]).ToResult(_ => null!), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("combine"));
            Assert.That(() => Validation.Fail<int>(default(System.Collections.Immutable.ImmutableArray<Error>)), Throws.ArgumentException.With.Property("ParamName").EqualTo("errors"));
        }

        [Test]
        public void The_list_is_copied_and_read_only()
        {
            var source = new List<Error> { Error.From("a") };
            var v = Validation.Fail<int>(source);
            source.Add(Error.From("b"));
            Assert.That(v.Errors, Has.Count.EqualTo(1));
            Assert.That(v.Errors, Is.Not.InstanceOf<List<Error>>().And.Not.InstanceOf<Error[]>());
            Assert.That(() => ((IList<Error>)v.Errors).Add(Error.From("c")), Throws.InstanceOf<NotSupportedException>());
        }

        [Test]
        public void The_named_members_read_each_case()
        {
            var ok = Validation.Ok<int, string>(2);
            var failed = Validation.Fail<int, string>(["a", "b"]);

            Assert.That((ok.IsSuccess, ok.IsFailure, failed.IsSuccess, failed.IsFailure), Is.EqualTo((true, false, false, true)));
            Assert.That(ok.TryGetValue(out var value) && value == 2, Is.True);
            Assert.That(failed.TryGetErrors(out var errors) && errors.Count == 2, Is.True);
            Assert.That(ok.TryGetErrors(out var none) || none is not null, Is.False);
            var (isSuccess, v, es) = failed;
            Assert.That((isSuccess, v, es), Is.EqualTo((false, 0, failed.Errors)));
            Assert.That(() => ok.Errors, Throws.InvalidOperationException.With.Message.Contains("success"));
            Assert.That(() => failed.Value, Throws.InvalidOperationException.With.Message.Contains("failure"));
            Assert.That(ok.ToString(), Is.EqualTo("Resultful.Validation`2[System.Int32,System.String]"));
        }

        // ---- The laws ----

        sealed record Leaf(int Code) : Error($"leaf {Code}");

        // Small alphabets, so equal errors and equal lists meet.
        static readonly Gen<Leaf> Leaves = Gen.Int[0, 3].Select(c => new Leaf(c));
        static readonly Gen<Leaf[]> LeafLists = Leaves.Array[1, 3];

        static readonly Gen<Validation<int, Leaf>> Validations = Gen.Frequency(
            (1, Gen.Int[0, 2].Select(v => Validation.Ok<int, Leaf>(v))),
            (2, LeafLists.Select(es => Validation.Fail<int, Leaf>(es))));

        static readonly Gen<Validation<int>> Untyped = Validations.Select(Widen);

        static IReadOnlyList<Leaf> ErrorsOf<T>(Validation<T, Leaf> v) => v.IsFailure ? v.Errors : [];

        static Validation<int> Widen(Validation<int, Leaf> v) => v.Match(x => (Validation<int>)x, es => Validation.Fail<int>(es));

        // The Combine overload for vs.Length values, summing them.
        static Validation<int, Leaf> CombineN(Validation<int, Leaf>[] v) => v.Length switch
        {
            2 => Validation.Combine(v[0], v[1], (a, b) => a + b),
            3 => Validation.Combine(v[0], v[1], v[2], (a, b, c) => a + b + c),
            4 => Validation.Combine(v[0], v[1], v[2], v[3], (a, b, c, d) => a + b + c + d),
            5 => Validation.Combine(v[0], v[1], v[2], v[3], v[4], (a, b, c, d, e) => a + b + c + d + e),
            6 => Validation.Combine(v[0], v[1], v[2], v[3], v[4], v[5], (a, b, c, d, e, f) => a + b + c + d + e + f),
            7 => Validation.Combine(v[0], v[1], v[2], v[3], v[4], v[5], v[6], (a, b, c, d, e, f, g) => a + b + c + d + e + f + g),
            _ => Validation.Combine(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], (a, b, c, d, e, f, g, h) => a + b + c + d + e + f + g + h),
        };

        static Validation<int> CombineN(Validation<int>[] v) => v.Length switch
        {
            2 => Validation.Combine(v[0], v[1], (a, b) => a + b),
            3 => Validation.Combine(v[0], v[1], v[2], (a, b, c) => a + b + c),
            4 => Validation.Combine(v[0], v[1], v[2], v[3], (a, b, c, d) => a + b + c + d),
            5 => Validation.Combine(v[0], v[1], v[2], v[3], v[4], (a, b, c, d, e) => a + b + c + d + e),
            6 => Validation.Combine(v[0], v[1], v[2], v[3], v[4], v[5], (a, b, c, d, e, f) => a + b + c + d + e + f),
            7 => Validation.Combine(v[0], v[1], v[2], v[3], v[4], v[5], v[6], (a, b, c, d, e, f, g) => a + b + c + d + e + f + g),
            _ => Validation.Combine(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], (a, b, c, d, e, f, g, h) => a + b + c + d + e + f + g + h),
        };

        [Test]
        public void Combine_is_associative()
        {
            Gen.Select(Validations, Validations, Validations).Sample((a, b, c) =>
            {
                var left = Validation.Combine(Validation.Combine(a, b), c).Map(t => (t.Item1.Item1, t.Item1.Item2, t.Item2));
                var right = Validation.Combine(a, Validation.Combine(b, c)).Map(t => (t.Item1, t.Item2.Item1, t.Item2.Item2));
                Assert.That(left, Is.EqualTo(right));
            });
            Gen.Select(Untyped, Untyped, Untyped).Sample((a, b, c) =>
            {
                var left = Validation.Combine(Validation.Combine(a, b), c, (ab, z) => (ab.Item1, ab.Item2, z));
                var right = Validation.Combine(a, Validation.Combine(b, c), (x, bc) => (x, bc.Item1, bc.Item2));
                Assert.That(left.Equals(right) && left.GetHashCode() == right.GetHashCode(), Is.True);
            });
        }

        [Test]
        public void Combine_gathers_every_error_in_order()
        {
            Validations.Array[8].Sample(vs =>
            {
                var all = Validation.Combine(vs[0], vs[1], vs[2], vs[3], vs[4], vs[5], vs[6], vs[7],
                    (a, b, c, d, e, f, g, h) => a + b + c + d + e + f + g + h);
                var expected = vs.SelectMany(ErrorsOf).ToArray();

                Assert.That(all.IsSuccess, Is.EqualTo(expected.Length == 0));
                Assert.That(ErrorsOf(all), Is.EqualTo(expected));
                if (all.IsSuccess) Assert.That(all.Value, Is.EqualTo(vs.Sum(v => v.Value)));

                // Every arity, typed and untyped, keeps every side's errors in order.
                for (var n = 2; n <= 8; n++)
                {
                    var some = vs.Take(n).ToArray();
                    Assert.That(ErrorsOf(CombineN(some)), Is.EqualTo(some.SelectMany(ErrorsOf).ToArray()), $"{n} typed");
                    var untyped = CombineN(some.Select(Widen).ToArray());
                    Assert.That(untyped.IsFailure ? untyped.Errors : [], Is.EqualTo(some.SelectMany(ErrorsOf).ToArray()), $"{n} untyped");
                }

                Assert.That(ErrorsOf(Validation.Combine(vs[0], vs[1])), Is.EqualTo(vs.Take(2).SelectMany(ErrorsOf).ToArray()), "the tuple form");
            });
        }

        [Test]
        public void Combine_calls_its_function_only_when_every_side_succeeds()
        {
            Gen.Select(Validations, Validations).Sample((a, b) =>
            {
                var calls = 0;
                _ = Validation.Combine(a, b, (x, y) => { calls++; return x + y; });
                Assert.That(calls, Is.EqualTo(a.IsSuccess && b.IsSuccess ? 1 : 0));
            });
        }

        [Test]
        public void ToResult_keeps_every_error()
        {
            Validations.Sample(v =>
            {
                var result = v.ToResult();
                Assert.That(result.IsSuccess, Is.EqualTo(v.IsSuccess));
                if (v.IsSuccess) return;

                var failed = (ValidationFailed<Leaf>)result.Error;
                Assert.That(failed.Errors, Is.EqualTo(v.Errors), "the same errors, in order");
                Assert.That(failed.Causes().Skip(1).ToArray(), Is.EqualTo(v.Errors.SelectMany(e => e.Causes()).ToArray()), "Causes walks every one");
                Assert.That(failed.TryFind<Leaf>(out var found) && found == v.Errors[0], Is.True);
            });
        }

        [Test]
        public void Result_to_Validation_and_back_loses_nothing()
        {
            Gen.Select(Gen.Bool, Gen.Int, Leaves).Sample((ok, value, leaf) =>
            {
                var result = ok ? value.Ok<int, Leaf>() : leaf.Fail<int, Leaf>();
                Validation<int, Leaf> validation = result;
                Assert.That(validation.ToResult(es => es[0]), Is.EqualTo(result));

                Result<int> general = ok ? value.Ok() : leaf.Fail<int>();
                Validation<int> untyped = general;
                Assert.That(untyped.ToResult(es => es[0]), Is.EqualTo(general));
                Assert.That(untyped.IsFailure ? untyped.Errors.Count : 1, Is.EqualTo(1), "one error is a list of one");
            });
        }

        [Test]
        public void Every_failure_holds_at_least_one_error()
        {
            // Every way to build or transform a validation keeps a failure non-empty.
            Gen.Select(Validations, Validations, Gen.Int[0, 4]).Sample((a, b, op) =>
            {
                Validation<int, Leaf> v = op switch
                {
                    0 => Validation.Combine(a, b, (x, y) => x * y),
                    1 => a.Map(x => x + 1),
                    2 => a.MapError(e => new Leaf(e.Code + 1)),
                    3 => a.Tee(_ => { }),
                    _ => (Result<int, Leaf>)a.ToResult(es => es[^1]),
                };
                if (v.IsFailure) Assert.That(v.Errors, Is.Not.Empty);
                Assert.That(v.IsSuccess ^ v.IsFailure, Is.True);
            });
        }

        [Test]
        public void Equality_and_hash_codes_agree()
        {
            Gen.Select(Validations, Validations).Sample((a, b) =>
            {
                var expected = a.IsSuccess == b.IsSuccess && (a.IsSuccess ? a.Value == b.Value : a.Errors.SequenceEqual(b.Errors));
                Assert.That(a.Equals(b), Is.EqualTo(expected));
                Assert.That(a == b, Is.EqualTo(expected));
                Assert.That(a != b, Is.Not.EqualTo(expected));
                Assert.That(a.Equals((object)b), Is.EqualTo(expected));
                if (expected) Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));

                // VAL-EQ: ValidationFailed compares its list the same way, so equal failures stay equal through ToResult.
                Assert.That(a.ToResult().Equals(b.ToResult()), Is.EqualTo(expected));

                Validation<int> ua = a.Match(x => (Validation<int>)x, es => Validation.Fail<int>(es));
                Validation<int> ub = b.Match(x => (Validation<int>)x, es => Validation.Fail<int>(es));
                Assert.That(ua == ub, Is.EqualTo(expected));
                if (expected) Assert.That(ua.GetHashCode(), Is.EqualTo(ub.GetHashCode()));
            });
        }
    }
}
