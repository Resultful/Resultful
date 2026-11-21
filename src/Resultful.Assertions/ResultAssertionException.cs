using System;

namespace Resultful.Assertions
{
    /// <summary>
    /// A <c>Resultful.Assertions</c> assertion failed. Every test framework (xUnit, NUnit, MSTest, TUnit) reports a test
    /// that throws it as failed.
    /// </summary>
    /// <remarks>
    /// It derives from <see cref="Exception"/> only, so the package depends on no test framework. The message names
    /// the expected and actual case, and prints an error with <see cref="Error.ToString"/> only.
    /// </remarks>
    public sealed class ResultAssertionException : Exception
    {
        /// <summary>Creates the exception with no message.</summary>
        public ResultAssertionException()
        {
        }

        /// <summary>Creates the exception with a failure message.</summary>
        /// <param name="message">What was expected, and what was found.</param>
        public ResultAssertionException(string message)
            : base(message)
        {
        }

        /// <summary>Creates the exception with a failure message and the exception that caused it.</summary>
        /// <param name="message">What was expected, and what was found.</param>
        /// <param name="innerException">The exception that caused this one.</param>
        public ResultAssertionException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
