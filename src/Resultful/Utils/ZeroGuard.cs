using System;
#if NET11_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
#endif

namespace Resultful.Utils
{
    // Decision 10 and 17: a failure always holds an error. Fail rejects null, and on net11.0 an error union with no value.
    internal static class ZeroGuard
    {
        public static TError ThrowIfNoError<TError>(this TError error, string argName)
        {
            // The cast boxes only a Nullable<T>; for a reference type it is free, and other structs skip it, even unoptimised.
            if (Nullability<TError>.CanBeNull && (object?)error is null)
                throw new ArgumentNullException(argName, "A failure needs an error; null isn't one.");
#if NET11_0_OR_GREATER
            if (UnionLayout<TError>.IsEmpty(in error))
                throw new ArgumentException(
                    $"A failure needs an error; this {typeof(TError).Name} is the zero value of the union (default), which holds no case.",
                    argName);
#endif
            return error;
        }
    }

    internal static class Nullability<T>
    {
        internal static readonly bool CanBeNull = !typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) is not null;
    }

#if NET11_0_OR_GREATER
    // How Fail tells whether an error is a union with no case, worked out once per type.
    internal enum UnionKind : byte
    {
        // A sealed type or a struct that isn't an IUnion: only null is rejected.
        None,
        // A compiler-generated union (C# 15): its one instance field is the compiler's backing field for Value, so it is
        // read in place, allocating nothing.
        SingleObjectField,
        // Anything else that is or may hold an IUnion: a hand-written IUnion struct (boxed to ask it), a Nullable of one
        // (unwrapped by boxing), or an interface or non-sealed class such as object or IUnion (the runtime value is asked).
        AskValue,
    }

    internal static class UnionLayout<T>
    {
        internal static readonly UnionKind Kind = Find();

        // 0 bytes for a compiler-generated union and for reference types; a struct on the AskValue path is boxed.
        internal static bool IsEmpty(in T value) => Kind switch
        {
            UnionKind.SingleObjectField => Unsafe.As<T, object?>(ref Unsafe.AsRef(in value)) is null,
            UnionKind.AskValue => value is IUnion { Value: null },
            _ => false,
        };

        // If trimming removed the field's metadata, the reflection finds no field and the type takes the safe AskValue path.
        [UnconditionalSuppressMessage("Trimming", "IL2090", Justification = "A missing field only selects the IUnion path, which is always correct.")]
        private static UnionKind Find()
        {
            var type = typeof(T);
            var union = typeof(IUnion);
            if (!type.IsValueType) return type.IsSealed && !union.IsAssignableFrom(type) ? UnionKind.None : UnionKind.AskValue;
            if (Nullable.GetUnderlyingType(type) is { } underlying) return union.IsAssignableFrom(underlying) ? UnionKind.AskValue : UnionKind.None;
            if (!union.IsAssignableFrom(type)) return UnionKind.None;
            // [Union] alone doesn't prove the compiler wrote the type: a hand-written [Union] struct can hold an object and
            // answer Value with a sentinel. Only the compiler's own backing field is trusted to be the Value it returns.
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var compilerLayout = type.IsDefined(typeof(UnionAttribute), inherit: false)
                && fields.Length == 1
                && fields[0].FieldType == typeof(object)
                && fields[0].IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
                && RuntimeHelpers.IsReferenceOrContainsReferences<T>()
                && Unsafe.SizeOf<T>() == IntPtr.Size;
            return compilerLayout ? UnionKind.SingleObjectField : UnionKind.AskValue;
        }
    }
#endif
}
