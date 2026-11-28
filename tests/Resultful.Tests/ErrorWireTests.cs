using System;
using System.Linq;
using NUnit.Framework;

namespace Resultful.Tests
{
    // Proposed decision 25, item 1: the name, keys and case registry every format shares.
    [TestFixture]
    public class ErrorWireTests
    {
        abstract record PaymentError(string Message) : Error(Message);
        sealed record CardDeclined(string Last4) : PaymentError($"card {Last4} was declined");
        sealed record Expired() : PaymentError("the card has expired");
        sealed record NotFound<TKey>(TKey Key) : Error($"{Key} not found");
        sealed record Conflict<TA, TB>(TA A, TB B) : Error("conflict");
        sealed record Unrelated() : Error("unrelated");

        static class Elsewhere
        {
            public sealed record Expired() : PaymentError("also expired");
        }

        readonly struct PaymentUnion(Error error) : IErrorUnion
        {
            public Error Error { get; } = error;
        }

        [Test]
        public void Name_is_the_simple_name_and_spells_out_generic_arguments_without_spaces()
        {
            Assert.That(ErrorWire.Name(typeof(CardDeclined)), Is.EqualTo("CardDeclined"));
            Assert.That(ErrorWire.Name(typeof(NotFound<Guid>)), Is.EqualTo("NotFound<Guid>"));
            Assert.That(ErrorWire.Name(typeof(Conflict<int, string>)), Is.EqualTo("Conflict<Int32,String>"));
            Assert.That(ErrorWire.Name(typeof(ValidationFailed<PaymentError>)), Is.EqualTo("ValidationFailed<PaymentError>"));
            Assert.That(ErrorWire.Name(typeof(NotFound<NotFound<int>>)), Is.EqualTo("NotFound<NotFound<Int32>>"));
            Assert.Throws<ArgumentNullException>(() => ErrorWire.Name(null!));
        }

        static readonly string[] Keys = { "type", "message", "context", "cause" };

        [Test]
        public void Keys_are_the_four_the_error_object_starts_with()
        {
            Assert.That(new[] { ErrorWire.TypeKey, ErrorWire.MessageKey, ErrorWire.ContextKey, ErrorWire.CauseKey }, Is.EqualTo(Keys));
        }

        [Test]
        public void IsValidationFailed_only_for_a_closed_ValidationFailed()
        {
            Assert.That(ErrorWire.IsValidationFailed(typeof(ValidationFailed<Error>)), Is.True);
            Assert.That(ErrorWire.IsValidationFailed(typeof(ValidationFailed<PaymentError>)), Is.True);
            Assert.That(ErrorWire.IsValidationFailed(typeof(ValidationFailed<>)), Is.False);
            Assert.That(ErrorWire.IsValidationFailed(typeof(NotFound<int>)), Is.False);
            Assert.That(ErrorWire.IsValidationFailed(typeof(CardDeclined)), Is.False);
        }

        [Test]
        public void Cases_keep_their_order_drop_repeats_and_merge_later_adds()
        {
            var cases = new ErrorCases()
                .Add<PaymentError>(new[] { typeof(CardDeclined), typeof(CardDeclined) })
                .Add<PaymentError>(new[] { typeof(Expired), typeof(CardDeclined) });

            Assert.That(cases.TryGetCases(typeof(PaymentError), out var found), Is.True);
            Assert.That(found, Is.EqualTo(new[] { typeof(CardDeclined), typeof(Expired) }));
            Assert.That(cases.TryGetCases(typeof(Error), out var none), Is.False);
            Assert.That(none, Is.Empty);
            Assert.That(cases.All.Select(p => p.Key), Is.EqualTo(new[] { typeof(PaymentError) }));

            Assert.That(cases.TryFind(typeof(PaymentError), "Expired", out var leaf), Is.True);
            Assert.That(leaf, Is.EqualTo(typeof(Expired)));
            Assert.That(cases.TryFind(typeof(PaymentError), "expired", out _), Is.False);
            Assert.That(cases.TryFind(typeof(Error), "Expired", out _), Is.False);
        }

        [Test]
        public void Cases_reject_what_could_not_be_read_back()
        {
            var cases = new ErrorCases();
            Assert.Throws<ArgumentException>(() => cases.Add<PaymentError>(new[] { typeof(Unrelated) }));
            Assert.Throws<ArgumentException>(() => cases.Add<PaymentError>(new[] { typeof(PaymentError) }));
            Assert.Throws<ArgumentException>(() => cases.Add<Error>(new[] { typeof(NotFound<>) }));
            Assert.Throws<ArgumentException>(() => cases.Add<Error>(new[] { typeof(string) }));
            Assert.Throws<ArgumentException>(() => cases.Add<Error>(new Type[] { null! }));
            var clash = Assert.Throws<ArgumentException>(() => cases.Add<PaymentError>(new[] { typeof(Expired), typeof(Elsewhere.Expired) }));
            Assert.That(clash!.Message, Does.Contain("\"Expired\"").And.Contain("RFG0008"));

            // A clash with cases added earlier is caught too, and leaves them as they were.
            cases.Add<PaymentError>(new[] { typeof(Expired) });
            Assert.Throws<ArgumentException>(() => cases.Add<PaymentError>(new[] { typeof(Elsewhere.Expired) }));
            Assert.That(cases.TryGetCases(typeof(PaymentError), out var kept), Is.True);
            Assert.That(kept, Is.EqualTo(new[] { typeof(Expired) }));
        }

        [Test]
        public void A_union_keeps_its_wrap()
        {
            var cases = new ErrorCases().AddUnion(new[] { typeof(CardDeclined), typeof(Expired) }, static e => new PaymentUnion(e));

            Assert.That(cases.TryFind(typeof(PaymentUnion), "CardDeclined", out _), Is.True);
            var leaf = new Expired();
            Assert.That(cases.TryWrap<PaymentUnion>(leaf, out var union), Is.True);
            Assert.That(union.Error, Is.SameAs(leaf));
            Assert.That(new ErrorCases().TryWrap<PaymentUnion>(leaf, out _), Is.False);
            Assert.Throws<ArgumentNullException>(() => new ErrorCases().AddUnion<PaymentUnion>(Array.Empty<Type>(), null!));
        }
    }
}
