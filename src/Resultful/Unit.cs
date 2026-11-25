namespace Resultful
{
    /// <summary>A type with a single value, <see cref="Value"/>. It is the success value of <c>VoidResult</c> and <c>VoidResult&lt;TError&gt;</c>.</summary>
    public class Unit
    {
        /// <summary>The single <see cref="Unit"/> value.</summary>
        public static Unit Value { get; } = new Unit();

        private Unit()
        {
        }
    }

    /// <summary>The absence of a value: what an empty <see cref="Option{T}"/> holds. It has a single value, <c>default</c>.</summary>
    /// <remarks>Replaces <c>OneOf.Types.None</c> (decision 1). The <c>Resultful.OneOf</c> package converts between the two.</remarks>
    public readonly struct None : IEquatable<None>
    {
        /// <summary>Every <see cref="None"/> equals every other.</summary>
        /// <param name="other">The other value.</param>
        public bool Equals(None other)
            => true;

        /// <summary>Equal to any boxed <see cref="None"/>.</summary>
        /// <param name="obj">The object to compare with.</param>
        public override bool Equals(object? obj)
            => obj is None;

        /// <summary>Zero, the same for every <see cref="None"/>.</summary>
        public override int GetHashCode()
            => 0;

        /// <summary>Returns <c>None</c>.</summary>
        public override string ToString()
            => nameof(None);

        /// <summary>Always <see langword="true"/>.</summary>
        /// <param name="left">The left value.</param>
        /// <param name="right">The right value.</param>
        public static bool operator ==(None left, None right)
            => true;

        /// <summary>Always <see langword="false"/>.</summary>
        /// <param name="left">The left value.</param>
        /// <param name="right">The right value.</param>
        public static bool operator !=(None left, None right)
            => false;
    }
}
