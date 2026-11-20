namespace Resultful
{
    /// <summary>A value that can be combined with a <typeparamref name="T"/> to give a <typeparamref name="TResult"/>.</summary>
    /// <typeparam name="T">The type of the value it combines with.</typeparam>
    /// <typeparam name="TResult">The type of the combined value.</typeparam>
    /// <remarks>The <c>Plus</c>, <c>Reduce</c> and <c>Fold</c> overloads without a merge or combine function use it.</remarks>
    public interface IPlus<in T, out TResult>
    {
        /// <summary>Combines this value with <paramref name="item"/>.</summary>
        /// <param name="item">The value to combine with.</param>
        /// <returns>The combined value.</returns>
        TResult Plus(T item);
    }
}
