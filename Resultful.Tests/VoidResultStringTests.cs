using System;
using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using FluentAssertions.OneOf;
using NUnit.Framework;

namespace Resultful.Tests
{
    public class VoidResultStringTests
    {
        private const string Error = "Value must be between 1 and 10";

        VoidResult MakeFromString(int value)
        {
            if (value > 0 && value <= 10)
            {
                return Result.Ok();
            }

            return Error.Fail();
        }

        VoidResult MakeFromStringList(int value)
        {
            if (value > 0 && value <= 10)
            {
                return Result.Ok();
            }

            return new List<string> { Error }.Fail();
        }

        VoidResult MakeFromStringArray(int value)
        {
            if (value > 0 && value <= 10)
            {
                return Result.Ok();
            }

            return new List<string> { Error }.Fail();
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

            result.ToOneOf().Should().Be<IEnumerable<string>>().And.Should().BeEquivalentTo(Error);
        }


        [Test]
        public void MatchListOk()
        {
            var result = MakeFromStringList(5);

            result.ToOneOf().Should().Be<Unit>();
        }

        [Test]
        public void MatchListError()
        {
            var result = MakeFromStringList(11);

            result.ToOneOf().Should().Be<IEnumerable<string>>().And.Should().BeEquivalentTo(Error);
        }

        [Test]
        public void MatchArrayOk()
        {
            var result = MakeFromStringArray(5);

            result.ToOneOf().Should().Be<Unit>();
        }

        [Test]
        public void MatchArrayError()
        {
            var result = MakeFromStringArray(11);

            result.ToOneOf().Should().Be<IEnumerable<string>>().And.Should().BeEquivalentTo(Error);
        }
    }
}
