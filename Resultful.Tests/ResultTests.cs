﻿using NUnit.Framework;
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
            var projectedValue = "Test ".Ok().Map(x => x.Trim()).Plus("Final Value".Ok(), (s, j) => $"{s}: {j}");

            projectedValue.ToOneOf().Should().Be<string>().And.Should().Be("Test: Final Value");
        }

    }
}
