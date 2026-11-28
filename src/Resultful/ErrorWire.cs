using System;
using System.Collections.Generic;
using System.Linq;

namespace Resultful
{
    // Proposed decision 25: the rules every format writes an error by, kept once here so the JSON, HTTP, GraphQL and
    // logging packages can't drift apart.
    /// <summary>
    /// How an <see cref="Error"/> is named and laid out on any wire: the keys of the error object and the name its
    /// <c>type</c> key holds.
    /// </summary>
    public static class ErrorWire
    {
        /// <summary>The key that names the error's leaf type.</summary>
        public const string TypeKey = "type";

        /// <summary>The key of <see cref="Error.Message"/>.</summary>
        public const string MessageKey = "message";

        /// <summary>The key of <see cref="Error.Context"/>.</summary>
        public const string ContextKey = "context";

        /// <summary>The key of <see cref="Error.Cause"/>.</summary>
        public const string CauseKey = "cause";

        /// <summary>
        /// The name a leaf is written as: its simple name, and for a generic leaf its type arguments' names in angle
        /// brackets with no spaces, so the closed type can be read back: <c>ValidationFailed&lt;PaymentError&gt;</c>,
        /// <c>Conflict&lt;Int32,String&gt;</c>.
        /// </summary>
        /// <param name="errorType">The leaf type.</param>
        /// <exception cref="ArgumentNullException"><paramref name="errorType"/> is <c>null</c>.</exception>
        public static string Name(Type errorType)
        {
            var name = (errorType ?? throw new ArgumentNullException(nameof(errorType))).Name;
            var tick = name.IndexOf('`');
            if (tick < 0) return name;
            return name.Remove(tick) + "<" + string.Join(",", errorType.GetGenericArguments().Select(Name)) + ">";
        }

        /// <summary>Whether <paramref name="type"/> is a closed <see cref="ValidationFailed{TError}"/>.</summary>
        /// <param name="type">The type to test.</param>
        /// <exception cref="ArgumentNullException"><paramref name="type"/> is <c>null</c>.</exception>
        public static bool IsValidationFailed(Type type)
        {
            return (type ?? throw new ArgumentNullException(nameof(type))).IsGenericType && !type.IsGenericTypeDefinition && type.GetGenericTypeDefinition() == typeof(ValidationFailed<>);
        }
    }

    /// <summary>
    /// The leaves each error type can hold, for formats that read an error back by its <see cref="ErrorWire.TypeKey"/>,
    /// and, for an error union, how a leaf is wrapped into it. Fill it from the generated <c>Cases</c> and <c>Wrap</c>.
    /// </summary>
    /// <remarks>An instance is filled at start-up and then only read; it isn't safe to add to while it is being read.</remarks>
    public sealed class ErrorCases
    {
        private readonly Dictionary<Type, List<Type>> _byError = new Dictionary<Type, List<Type>>();
        private readonly Dictionary<Type, Delegate> _wraps = new Dictionary<Type, Delegate>();

        /// <summary>Every error type with cases, and its cases, in the order added.</summary>
        public IEnumerable<KeyValuePair<Type, IReadOnlyList<Type>>> All =>
            _byError.Select(static p => new KeyValuePair<Type, IReadOnlyList<Type>>(p.Key, p.Value));

        /// <summary>Adds <paramref name="cases"/> as leaves of <typeparamref name="TError"/>, after any already added.</summary>
        /// <typeparam name="TError">The error base, error union or other declared error type.</typeparam>
        /// <param name="cases">The leaves, usually the generated <c>Cases</c>.</param>
        /// <returns>This registry, for chaining.</returns>
        /// <exception cref="ArgumentException">A case is <c>null</c>, isn't a concrete <see cref="Error"/>, isn't a <typeparamref name="TError"/>, or shares its <see cref="ErrorWire.Name"/> with another case.</exception>
        public ErrorCases Add<TError>(IEnumerable<Type> cases) => Add(typeof(TError), cases);

        /// <summary>Adds <paramref name="cases"/> as leaves of <paramref name="errorType"/>, after any already added.</summary>
        /// <param name="errorType">The error base, error union or other declared error type.</param>
        /// <param name="cases">The leaves, usually the generated <c>Cases</c>.</param>
        /// <returns>This registry, for chaining.</returns>
        /// <exception cref="ArgumentException">A case is <c>null</c>, isn't a concrete <see cref="Error"/>, isn't a <paramref name="errorType"/>, or shares its <see cref="ErrorWire.Name"/> with another case.</exception>
        public ErrorCases Add(Type errorType, IEnumerable<Type> cases)
        {
            var list = Validate(errorType, cases);
            if (_byError.TryGetValue(errorType, out var existing))
            {
                list = Validate(errorType, existing.Concat(list).Distinct());
            }

            _byError[errorType] = list;
            return this;
        }

        /// <summary>Adds the cases of an error union and how a leaf is wrapped into it.</summary>
        /// <typeparam name="TUnion">The error union.</typeparam>
        /// <param name="cases">The leaves, usually the generated <c>Cases</c>.</param>
        /// <param name="wrap">Wraps a leaf into the union, usually the generated <c>Wrap</c>.</param>
        /// <returns>This registry, for chaining.</returns>
        public ErrorCases AddUnion<TUnion>(IEnumerable<Type> cases, Func<Error, TUnion> wrap)
            where TUnion : struct, IErrorUnion
        {
            _ = wrap ?? throw new ArgumentNullException(nameof(wrap));
            _ = Add<TUnion>(cases);
            _wraps[typeof(TUnion)] = wrap;
            return this;
        }

        /// <summary>The cases added for exactly <paramref name="errorType"/>.</summary>
        /// <param name="errorType">The declared error type.</param>
        /// <param name="cases">The cases, in the order added.</param>
        /// <returns><c>true</c> if any were added.</returns>
        public bool TryGetCases(Type errorType, out IReadOnlyList<Type> cases)
        {
            if (_byError.TryGetValue(errorType ?? throw new ArgumentNullException(nameof(errorType)), out var list))
            {
                cases = list;
                return true;
            }

            cases = Array.Empty<Type>();
            return false;
        }

        /// <summary>The leaf of <paramref name="errorType"/> written as <paramref name="name"/>.</summary>
        /// <param name="errorType">The declared error type.</param>
        /// <param name="name">The value of the <see cref="ErrorWire.TypeKey"/> key.</param>
        /// <param name="leaf">The leaf, if one matches.</param>
        /// <returns><c>true</c> if a case of <paramref name="errorType"/> is written as <paramref name="name"/>.</returns>
        public bool TryFind(Type errorType, string name, out Type leaf)
        {
            _ = name ?? throw new ArgumentNullException(nameof(name));
            if (TryGetCases(errorType, out var cases))
            {
                foreach (var candidate in cases)
                {
                    if (string.Equals(ErrorWire.Name(candidate), name, StringComparison.Ordinal))
                    {
                        leaf = candidate;
                        return true;
                    }
                }
            }

            leaf = null!;
            return false;
        }

        /// <summary>Wraps <paramref name="leaf"/> into <typeparamref name="TUnion"/> with the wrap added by <see cref="AddUnion{TUnion}"/>.</summary>
        /// <typeparam name="TUnion">The error union.</typeparam>
        /// <param name="leaf">The leaf to wrap.</param>
        /// <param name="union">The union holding <paramref name="leaf"/>.</param>
        /// <returns><c>false</c> if no wrap was added for <typeparamref name="TUnion"/>.</returns>
        public bool TryWrap<TUnion>(Error leaf, out TUnion union)
            where TUnion : struct, IErrorUnion
        {
            _ = leaf ?? throw new ArgumentNullException(nameof(leaf));
            if (_wraps.TryGetValue(typeof(TUnion), out var wrap))
            {
                union = ((Func<Error, TUnion>)wrap)(leaf);
                return true;
            }

            union = default;
            return false;
        }

        /// <summary>
        /// Checks a case list for <paramref name="errorType"/>: each case a concrete <see cref="Error"/> that is a
        /// <paramref name="errorType"/> (when that is an <see cref="Error"/>), no two with the same <see cref="ErrorWire.Name"/>.
        /// Duplicates of one type are dropped.
        /// </summary>
        /// <param name="errorType">The declared error type.</param>
        /// <param name="cases">The cases.</param>
        /// <returns>The cases, in order, without duplicates.</returns>
        public static List<Type> Validate(Type errorType, IEnumerable<Type> cases)
        {
            _ = errorType ?? throw new ArgumentNullException(nameof(errorType));
            _ = cases ?? throw new ArgumentNullException(nameof(cases));
            var list = new List<Type>();
            var names = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (var leaf in cases)
            {
                if (leaf is null) throw new ArgumentException($"The cases of {errorType.Name} contain null.", nameof(cases));
                if (!typeof(Error).IsAssignableFrom(leaf) || leaf.IsAbstract || leaf.IsGenericTypeDefinition)
                    throw new ArgumentException($"{leaf.Name} isn't a concrete Error, so it can't be a case of {errorType.Name}.", nameof(cases));
                if (typeof(Error).IsAssignableFrom(errorType) && !errorType.IsAssignableFrom(leaf))
                    throw new ArgumentException($"{leaf.Name} isn't a {errorType.Name}, so it can't be one of its cases.", nameof(cases));
                var name = ErrorWire.Name(leaf);
                if (names.TryGetValue(name, out var other))
                {
                    if (other != leaf)
                        throw new ArgumentException($"{other.FullName} and {leaf.FullName} are both written as \"{name}\", so one couldn't be read back (RFG0008).", nameof(cases));
                    continue;
                }

                names.Add(name, leaf);
                list.Add(leaf);
            }

            return list;
        }
    }
}
