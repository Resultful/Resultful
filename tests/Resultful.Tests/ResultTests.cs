using NUnit.Framework;
using FluentAssertions;
using FluentAssertions.OneOf;

namespace Resultful.Tests
{
    [TestFixture]
    public class ResultTests
    {
        [Test]
        public void AssertChange()
        {
            // 09: Plus on Result<T> (which gathered errors) is gone; Bind combines two results and stops at the first failure.
            var projectedValue = "Test ".Ok().Map(x => x.Trim()).Bind(s => "Final Value".Ok().Map(j => $"{s}: {j}"));

            projectedValue.ToOneOf().Should().Be<string>().And.Should().Be("Test: Final Value");
        }

    }
}
