using System;
using System.Collections;
using System.Collections.Generic;

namespace Resultful.Generators.Models;

internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly T[]? _array;

    public EquatableArray(T[]? array)
    {
        _array = array;
    }

    public static EquatableArray<T> Empty { get; } = new(Array.Empty<T>());

    public int Length => _array?.Length ?? 0;

    public T this[int index] => (_array ?? Array.Empty<T>())[index];

    public bool Equals(EquatableArray<T> other) => AsSpan().SequenceEqual(other.AsSpan());

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (_array is null)
        {
            return 0;
        }

        var hash = 17;
        for (var i = 0; i < _array.Length; i++)
        {
            hash = unchecked((hash * 31) + EqualityComparer<T>.Default.GetHashCode(_array[i]));
        }

        return hash;
    }

    public ReadOnlySpan<T> AsSpan() =>
        _array is null ? ReadOnlySpan<T>.Empty : new ReadOnlySpan<T>(_array);

    public IEnumerator<T> GetEnumerator() =>
        ((IEnumerable<T>)(_array ?? Array.Empty<T>())).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);
}
