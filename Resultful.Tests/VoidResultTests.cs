using FluentAssertions;
using FluentAssertions.OneOf;
using NUnit.Framework;
using OResultfulTests;

namespace Resultful.Tests
{
    public class VoidResultTests
    {
        private const string Error = "Value must be between 1 and 10";

        VoidResult<Failure> MakeFromString(int value)
        {
            if (value > 0 && value <= 10)
            {
                return Result.Ok();
            }

            return new Failure(Error).Err();
        }

        [Test]
        public void MatchBasicOk()
        {
            var result = MakeFromString(5);

            result.ToOneOf().Should().Be<Unit>();
        }

        [Test]
        public void MatchBasicError()
        {
            var result = MakeFromString(11);

            result.ToOneOf().Should().Be<Failure>().And.Should().BeEquivalentTo(Error);
        }

    }
}
