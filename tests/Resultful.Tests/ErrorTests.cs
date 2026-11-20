using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CsCheck;
using NUnit.Framework;

namespace Resultful.Tests
{
    // Decision 14 (06): the Error record. The laws hold the real Error to a small reference model (ErrorModel) on random
    // error trees: equality and hash codes, WithContext, Causes, TryFind and ToString. The examples pin the format and
    // the argument checks.
    [TestFixture]
    public class ErrorTests
    {
        // Two leaves with a property ToString must never print.
        sealed record Alpha : Error
        {
            public Alpha(string message, string secret)
                : base(message) => Secret = secret;

            public string Secret { get; }
        }

        sealed record Beta : Error
        {
            public Beta(string message, string secret)
                : base(message) => Secret = secret;

            public string Secret { get; }
        }

        // An Inner override that returns itself, or an error set after construction (an ancestor), or null.
        sealed record SelfInner(string Note) : Error("loops on itself")
        {
            public override IEnumerable<Error> Inner()
            {
                yield return this;
            }
        }

        sealed record BackInner(string Note) : Error("points back up")
        {
            public Error? Back { get; set; }

            public override IEnumerable<Error> Inner() => Back is null ? [] : [Back];
        }

        sealed record NullInner(string Note) : Error("inner is null")
        {
            public override IEnumerable<Error> Inner() => null!;
        }

        // The reference model: what an error is, as plain data. Kind is Alpha, Beta or Validation (a ValidationFailed<Error>).
        sealed record ErrorModel(string Kind, string Message, string Secret, string[] Context, ErrorModel? Cause, ErrorModel[] Inner);

        const string ValidationMessage = "Validation failed.";

        // Small alphabets so equal trees come up often; dotted and dotless i so tr-TR would show any culture-dependent text.
        static readonly Gen<string> Messages = Gen.OneOf("id", "İd", "ıd");
        static readonly Gen<string> Secrets = Gen.OneOf("SECRET-4111", "SECRET-5500");
        static readonly Gen<string[]> Notes = Gen.OneOf("in", "IN", "ın").Array[0, 2];
        static readonly Gen<ErrorModel> Models = GenModel(3);

        static Gen<ErrorModel> GenModel(int depth)
        {
            var leaf = Gen.Select(Gen.OneOf("Alpha", "Beta"), Messages, Secrets, Notes,
                (kind, message, secret, notes) => new ErrorModel(kind, message, secret, notes, null, []));
            if (depth == 0)
            {
                return leaf;
            }

            var below = GenModel(depth - 1);
            var wrapper = Gen.Select(leaf, below, (outer, cause) => outer with { Cause = cause });
            var validation = Gen.Select(below.Array[1, 3], Notes, Gen.Bool, below,
                (inner, notes, hasCause, cause) => new ErrorModel("Validation", ValidationMessage, "", notes, hasCause ? cause : null, inner));
            return Gen.OneOf<ErrorModel>(leaf, leaf, wrapper, validation);
        }

        static Error Build(ErrorModel m)
        {
            Error error = m.Kind switch
            {
                "Alpha" => new Alpha(m.Message, m.Secret),
                "Beta" => new Beta(m.Message, m.Secret),
                _ => new ValidationFailed<Error>(m.Inner.Select(Build).ToArray()),
            };
            return error with { Cause = m.Cause is null ? null : Build(m.Cause), Context = m.Context };
        }

        static bool ModelEquals(ErrorModel? a, ErrorModel? b) =>
            a is null
                ? b is null
                : b is not null
                  && a.Kind == b.Kind
                  && a.Message == b.Message
                  && a.Secret == b.Secret
                  && a.Context.SequenceEqual(b.Context)
                  && ModelEquals(a.Cause, b.Cause)
                  && a.Inner.Length == b.Inner.Length
                  && a.Inner.Zip(b.Inner, ModelEquals).All(x => x);

        // This error, then its cause's tree, then each inner error's tree.
        static IEnumerable<ErrorModel> ModelCauses(ErrorModel m)
        {
            yield return m;
            foreach (var below in (m.Cause is null ? [] : ModelCauses(m.Cause)).Concat(m.Inner.SelectMany(ModelCauses)))
            {
                yield return below;
            }
        }

        static string KindOf(Error e) => e switch
        {
            Alpha => "Alpha",
            BackInner => "BackInner",
            Beta => "Beta",
            ValidationFailed<Error> => "Validation",
            _ => e.GetType().Name,
        };

        static string ToStringUnder(Error e, string culture)
        {
            var saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = culture.Length == 0 ? CultureInfo.InvariantCulture : new CultureInfo(culture);
                return e.ToString();
            }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
        }

        [Test]
        public void Equality_agrees_with_the_model_and_hash_codes_agree_with_equality()
        {
            Gen.Select(Models, Models).Sample((a, b) =>
            {
                Error x = Build(a), y = Build(b), x2 = Build(a);
                var expected = ModelEquals(a, b);
                Assert.That(x.Equals(y), Is.EqualTo(expected), "Equals agrees with the model");
                Assert.That(y.Equals(x), Is.EqualTo(expected), "symmetric");
                Assert.That(x == y, Is.EqualTo(expected), "==");
                Assert.That(x.Equals((object)y), Is.EqualTo(expected), "Equals(object)");
                Assert.That(x, Is.EqualTo(x2), "a tree built twice is equal");
                Assert.That(x.GetHashCode(), Is.EqualTo(x2.GetHashCode()), "and has the same hash code");
                if (expected)
                {
                    Assert.That(x.GetHashCode(), Is.EqualTo(y.GetHashCode()), "equal errors, equal hash codes");
                }
            });
        }

        [Test]
        public void Two_equal_errors_stay_equal_after_the_same_note_and_unequal_after_different_notes()
        {
            Gen.Select(Models, Models, Gen.OneOf("a", "b")).Sample((a, b, note) =>
            {
                var expected = ModelEquals(a, b);
                Assert.That(Build(a).WithContext(note).Equals(Build(b).WithContext(note)), Is.EqualTo(expected));
                Assert.That(Build(a).WithContext("a").Equals(Build(a).WithContext("b")), Is.False);
                Assert.That(Build(a).Equals(Build(a).WithContext(note)), Is.False, "context takes part in equality");
            });
        }

        [Test]
        public void WithContext_keeps_the_type_adds_one_note_at_the_end_and_copies()
        {
            Gen.Select(Models, Gen.OneOf("a", "b")).Sample((m, note) =>
            {
                var original = Build(m);
                var before = original.Context.ToArray();
                var noted = original.WithContext(note);
                Assert.That(noted.GetType(), Is.EqualTo(original.GetType()), "same type");
                Assert.That(noted.Context, Is.EqualTo(before.Append(note)), "one more note, at the end");
                Assert.That(original.Context, Is.EqualTo(before), "the original is unchanged");
                Assert.That(noted, Is.Not.SameAs(original));
                Assert.That(noted with { Context = before }, Is.EqualTo(original), "nothing else changes");
            });
        }

        [Test]
        public void Causes_runs_outer_to_inner_depth_first()
        {
            Models.Sample(m =>
            {
                var actual = Build(m).Causes().Select(e => (KindOf(e), e.Message, string.Join("|", e.Context)));
                var expected = ModelCauses(m).Select(x => (x.Kind, x.Message, string.Join("|", x.Context)));
                Assert.That(actual, Is.EqualTo(expected));
                Assert.That(Build(m).Causes().First(), Is.EqualTo(Build(m)), "starts with the error itself");
            });
        }

        [Test]
        public void TryFind_finds_the_first_match_in_Causes_and_the_same_one_after_a_note()
        {
            static void Law<T>(Error e)
                where T : Error
            {
                var first = e.Causes().OfType<T>().FirstOrDefault();
                Assert.That(e.TryFind<T>(out var found), Is.EqualTo(first is not null));
                Assert.That(found, Is.SameAs(first));

                // Law 5 of One error or many: context keeps the error.
                var noted = e.WithContext("note");
                Assert.That(noted.TryFind<T>(out var again), Is.EqualTo(first is not null));
                if (first is not null && !ReferenceEquals(first, e))
                {
                    Assert.That(again, Is.SameAs(first), "an error under the note is the same object");
                }
            }

            Models.Sample(m =>
            {
                var e = Build(m);
                Law<Alpha>(e);
                Law<Beta>(e);
                Law<ValidationFailed<Error>>(e);
                Law<MessageError>(e);
                Law<Error>(e);
            });
        }

        [Test]
        public void ToString_never_prints_a_leafs_own_properties_and_shows_every_message()
        {
            Models.Sample(m =>
            {
                var text = Build(m).ToString();
                Assert.That(text, Does.Not.Contain("SECRET"), "a leaf's property stays out");
                foreach (var e in ModelCauses(m))
                {
                    Assert.That(text, Does.Contain(e.Message));
                    foreach (var note in e.Context)
                    {
                        Assert.That(text, Does.Contain(note));
                    }
                }
            });
        }

        [Test]
        public void ToString_is_the_same_under_every_culture()
        {
            Models.Sample(m =>
            {
                var e = Build(m);
                var invariant = ToStringUnder(e, "");
                Assert.That(ToStringUnder(e, "tr-TR"), Is.EqualTo(invariant), "tr-TR");
                Assert.That(ToStringUnder(e, "de-DE"), Is.EqualTo(invariant), "de-DE");
            });
        }

        [Test]
        public void An_error_that_contains_itself_is_visited_once_and_printed_with_an_ellipsis()
        {
            // Any random tree under a node whose Inner returns itself: each error object once, and nothing throws.
            Models.Sample(m =>
            {
                var tree = Build(m);
                var loop = new SelfInner("x") { Cause = tree };
                Assert.That(loop.Causes().Count(), Is.EqualTo(ModelCauses(m).Count() + 1));
                Assert.That(loop.TryFind<SelfInner>(out var found) && ReferenceEquals(found, loop), Is.True);
                Assert.That(loop.ToString(), Does.StartWith("SelfInner: loops on itself [").And.Contains("…"));
            });

            var back = new BackInner("y");
            var top = new Alpha("top", "SECRET") { Cause = back };
            back.Back = top;
            Assert.That(string.Join(",", top.Causes().Select(KindOf)), Is.EqualTo("Alpha,BackInner"));
            Assert.That(top.TryFind<Beta>(out _), Is.False, "ends when nothing matches");
            Assert.That(top.ToString(), Is.EqualTo("Alpha: top ---> BackInner: points back up […]"));
        }

        [Test]
        public void A_shared_error_listed_twice_prints_twice_but_Causes_visits_it_once()
        {
            var shared = Error.From("shared");
            var v = new ValidationFailed<Error>([shared, shared]);
            Assert.That(v.ToString(), Is.EqualTo("ValidationFailed<Error>: Validation failed. [MessageError: shared; MessageError: shared]"));
            Assert.That(v.Causes().Count(), Is.EqualTo(2));
        }

        [Test]
        public void A_very_deep_cause_chain_doesnt_overflow()
        {
            Error chain = Error.From("bottom");
            for (var i = 0; i < 100_000; i++)
            {
                chain = new Alpha("level", "SECRET") { Cause = chain };
            }

            var text = chain.ToString();
            Assert.That(text, Does.EndWith("---> …"));
            Assert.That(text.Split(" ---> ").Length, Is.EqualTo(65), "64 levels, then the ellipsis");
            Assert.That(chain.Causes().Count(), Is.EqualTo(100_001));
            Assert.That(chain.TryFind<MessageError>(out var bottom) && bottom.Message == "bottom", Is.True);
        }

        [Test]
        public void An_Inner_that_returns_null_counts_as_none()
        {
            var e = new NullInner("z") { Cause = Error.From("below") };
            Assert.That(e.Causes().Count(), Is.EqualTo(2));
            Assert.That(e.TryFind<MessageError>(out _), Is.True);
            Assert.That(e.ToString(), Is.EqualTo("NullInner: inner is null ---> MessageError: below"));
        }

        [Test]
        public void ToString_format()
        {
            var e = new Alpha("order 7 was not saved", "SECRET") { Cause = Error.From("the database timed out").WithContext("saving") }
                .WithContext("checkout")
                .WithContext("api");
            Assert.That(e.ToString(), Is.EqualTo("Alpha: order 7 was not saved (context: checkout; api) ---> MessageError: the database timed out (context: saving)"));

            var v = new ValidationFailed<Error>([Error.From("name is required"), new Beta("email is invalid", "SECRET")]);
            Assert.That(v.ToString(), Is.EqualTo("ValidationFailed<Error>: Validation failed. [MessageError: name is required; Beta: email is invalid]"));
        }

        [Test]
        public void From_makes_a_MessageError_with_nothing_else()
        {
            var e = Error.From("plain");
            Assert.That(e, Is.TypeOf<MessageError>());
            Assert.That(e.Message, Is.EqualTo("plain"));
            Assert.That(e.Cause, Is.Null);
            Assert.That(e.Context, Is.Empty);
            Assert.That(e.Inner(), Is.Empty);
            Assert.That(e, Is.EqualTo(new MessageError("plain")));
        }

        [Test]
        public void Context_is_copied_when_set()
        {
            var notes = new List<string> { "a" };
            var e = Error.From("x") with { Context = notes };
            notes.Add("b");
            Assert.That(e.Context, Has.Count.EqualTo(1).And.Contains("a"));
            Assert.That(e.Context, Is.Not.InstanceOf<string[]>(), "not a writable array behind the interface");
        }

        [Test]
        public void ValidationFailed_copies_its_list_and_compares_it_element_by_element()
        {
            var errors = new List<Error> { Error.From("a") };
            var v = new ValidationFailed<Error>(errors);
            errors.Add(Error.From("b"));
            Assert.That(v.Errors, Has.Count.EqualTo(1));
            Assert.That(v.Message, Is.EqualTo("Validation failed."));
            Assert.That(v, Is.EqualTo(new ValidationFailed<Error>([Error.From("a")])));
            Assert.That(v.GetHashCode(), Is.EqualTo(new ValidationFailed<Error>([Error.From("a")]).GetHashCode()));
            Assert.That(v, Is.Not.EqualTo(new ValidationFailed<Error>([Error.From("b")])));
            Assert.That(v, Is.Not.EqualTo(new ValidationFailed<Error>([Error.From("a"), Error.From("a")])));
        }

        [Test]
        public void Argument_checks()
        {
            Assert.That(() => Error.From(null!), Throws.ArgumentNullException);
            Assert.That(() => Error.From("x").WithContext(null!), Throws.ArgumentNullException);
            Assert.That(() => ((Error)null!).WithContext("x"), Throws.ArgumentNullException);
            Assert.That(() => Error.From("x") with { Context = null! }, Throws.ArgumentNullException);
            Assert.That(() => Error.From("x") with { Context = [null!] }, Throws.ArgumentNullException);
            Assert.That(() => new ValidationFailed<Error>(null!), Throws.ArgumentNullException);
            Assert.That(() => new ValidationFailed<Error>([]), Throws.ArgumentException.With.Message.Contains("at least one"));
            Assert.That(() => new ValidationFailed<Error>([null!]), Throws.ArgumentException.With.Message.Contains("index 0"));
        }
    }
}
