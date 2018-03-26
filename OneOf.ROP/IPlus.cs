using System;
using System.Collections.Generic;
using System.Text;

namespace OneOf.ROP
{
    public interface IPlus<T, TResult>
    {
        TResult Plus(T item);
    }
}
