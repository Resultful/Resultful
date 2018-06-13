using System;

namespace OneOf.ROP.Utils
{
    internal static class NullArgsHelper
    {
        public static T ThrowIfDefault<T>(this T value, string argName) where T : class
            => value ?? throw new ArgumentNullException(argName);
    }
}
