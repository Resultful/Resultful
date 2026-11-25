namespace Resultful
{
    // Decision 17: a union of Error records is a struct, not an Error, so it reaches the shared fields through this.
    /// <summary>An error union whose cases all derive from <see cref="Resultful.Error"/>.</summary>
    /// <remarks>
    /// <c>Resultful.Generators</c> adds it to a C# <c>partial union</c> whose cases are all errors. It is on every target,
    /// so the public API is the same everywhere, but only <c>net11.0</c> code can declare a union.
    /// </remarks>
    public interface IErrorUnion
    {
        /// <summary>
        /// The error the union holds, unwrapped to the leaf: when the case is itself an error union, its own
        /// <see cref="Error"/>.
        /// </summary>
        Error Error { get; }
    }
}
