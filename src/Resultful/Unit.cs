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
}
