using System;
using System.Collections.Generic;
using System.Text;

namespace OneOf.ROP
{
    public interface IPlus<T>
    {
        T Plus(T item);
    }
}
