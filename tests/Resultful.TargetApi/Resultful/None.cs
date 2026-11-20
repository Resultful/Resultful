#nullable enable
using System;

namespace Resultful
{
    // Decision 1: replaces OneOf.Types.None in Option<T>'s public API.
    public readonly struct None : IEquatable<None>
    {
        public static None Value => throw null!;
        public bool Equals(None other) => throw null!;
        public override bool Equals(object? obj) => throw null!;
        public override int GetHashCode() => throw null!;
        public override string ToString() => throw null!;
        public static bool operator ==(None left, None right) => throw null!;
        public static bool operator !=(None left, None right) => throw null!;
    }
}
