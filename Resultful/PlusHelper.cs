using System;
using System.Collections.Generic;
using System.Text;

namespace Resultful
{
    internal static class PlusHelper
    {
        //These two may need to be moved, they are nothing to do with results
        internal static TResult Plus<TLeft, TRight, TResult>(this TLeft left, TRight right)
            where TLeft : IPlus<TRight, TResult>
            => left.Plus(right);

        internal static T Plus<T>(this T left, T right)
            where T : IPlus<T, T>
            => left.Plus(right);
    }
}
