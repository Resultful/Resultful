using System;
using System.Collections.Generic;
using System.Text;

namespace OneOf.ROP.Utils
{
    public static class NullArgsHelper
    {
        public static T ThrowIfDefault<T>(this T value, string argName) where T : class
            => value ?? throw new ArgumentNullException(argName);
    }
}
