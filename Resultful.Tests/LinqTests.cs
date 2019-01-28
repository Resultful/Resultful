using FluentAssertions;
using FluentAssertions.OneOf;
using NUnit.Framework;
using Resultful.LINQ;

namespace Resultful.Tests
{
    [TestFixture]
    public class LinqTests
    {
        [Test]
        public void AssertThing()
        {
            var trimmedValue =
                from s in "  Test ".Ok()
                select s.Trim();
            var projectedValue =
                from s in trimmedValue
                from j in "Final Value".Ok()
                select $"{s}: {j}";

            projectedValue.ToOneOf().Should().Be<string>().And.Should().Be("Test: Final Value");
        }

    }
}
