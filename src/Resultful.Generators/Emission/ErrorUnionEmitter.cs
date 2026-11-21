using System.Linq;
using Resultful.Generators.Models;

namespace Resultful.Generators.Emission;

// Decision 17: a partial union whose cases are all errors gets IErrorUnion, the shared fields forwarded to its leaf,
// WithContext that keeps the union's type, and the case list and Wrap that Resultful.Text.Json reads through.
internal static class ErrorUnionEmitter
{
    private const string ErrorType = "global::Resultful.Error";

    public static string Emit(ErrorUnionModel m)
    {
        var w = new SourceWriter();
        w.OpenNamespaceAndEnclosing(m.Namespace, m.EnclosingTypes);
        var zero = $"throw new global::System.InvalidOperationException(\"This {m.Name} is the zero value (default), which holds no case.\")";

        w.WriteLine($"{m.Accessibility} partial union {m.Name} : global::Resultful.IErrorUnion");
        w.OpenBrace();

        // A member the union declares itself is left to it.
        bool Writes(string member) => !m.DeclaredMembers.Contains(member);

        // The innermost leaves: the "type" names a JSON reader accepts. A [ClosedHierarchy] case adds its own Cases.
        if (Writes("Cases"))
        {
            var direct = m.Leaves.Where(l => !l.IsHierarchy).Select(l => $"typeof({l.FullName})");
            var cases = $"new global::System.Type[] {{ {string.Join(", ", direct)} }}";
            foreach (var hierarchy in m.Leaves.Where(l => l.IsHierarchy))
            {
                cases = $"global::System.Linq.Enumerable.Concat({cases}, {hierarchy.FullName}.Cases)";
            }

            if (m.Leaves.Any(l => l.IsHierarchy))
            {
                cases = $"global::System.Linq.Enumerable.ToArray({cases})";
            }

            w.WriteLine($"public static global::System.Collections.Generic.IReadOnlyList<global::System.Type> Cases {{ get; }} = {cases};");
            w.WriteLine();
        }

        // A nested union unwraps to its own leaf.
        if (Writes("Error"))
        {
            w.WriteLine($"public {ErrorType} Error => Value switch");
            w.OpenBrace();
            w.WriteLine($"{ErrorType} e => e,");
            w.WriteLine("global::Resultful.IErrorUnion u => u.Error,");
            w.WriteLine($"_ => {zero},");
            w.CloseBrace(";");
            w.WriteLine();
        }

        if (Writes("Message"))
        {
            w.WriteLine("public string Message => Error.Message;");
            w.WriteLine();
        }

        if (Writes("Cause"))
        {
            w.WriteLine($"public {ErrorType}? Cause => Error.Cause;");
            w.WriteLine();
        }

        if (Writes("TryFind"))
        {
            w.WriteLine($"public bool TryFind<TFound>([global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TFound? found) where TFound : {ErrorType} => Error.TryFind(out found);");
            w.WriteLine();
        }

        // Per direct case, most-derived first, so a nested union is rewrapped as itself and the outer union keeps its
        // type.
        if (Writes("WithContext"))
        {
            WriteWithContext(w, m, zero);
            w.WriteLine();
        }

        // Builds the union from a leaf read back, rebuilding any nesting. A leaf reached directly and through a nested
        // union is wrapped on the direct path.
        if (Writes("Wrap"))
        {
            WriteWrap(w, m);
        }

        w.CloseBrace();
        w.CloseNamespaceAndEnclosing(m.Namespace, m.EnclosingTypes);
        return w.ToString();
    }

    private static void WriteWithContext(SourceWriter w, ErrorUnionModel m, string zero)
    {
        w.WriteLine($"public {m.Name} WithContext(string message) => Value switch");
        w.OpenBrace();
        for (var i = 0; i < m.Cases.Length; i++)
        {
            var c = m.Cases[i];
            w.WriteLine(c.IsUnion
                ? $"{c.FullName} c => new {m.Name}(c.WithContext(message)),"
                : $"{c.FullName} c => new {m.Name}(global::Resultful.ErrorExtensions.WithContext(c, message)),");
        }

        w.WriteLine($"_ => {zero},");
        w.CloseBrace(";");
    }

    private static void WriteWrap(SourceWriter w, ErrorUnionModel m)
    {
        w.WriteLine($"public static {m.Name} Wrap({ErrorType} leaf) => leaf switch");
        w.OpenBrace();
        for (var i = 0; i < m.Leaves.Length; i++)
        {
            var leaf = m.Leaves[i];
            w.WriteLine($"{leaf.FullName} c => {leaf.WrapOpen}c{new string(')', leaf.Depth)},");
        }

        w.WriteLine("null => throw new global::System.ArgumentNullException(nameof(leaf)),");
        w.WriteLine($"_ => throw new global::System.ArgumentException($\"{{leaf.GetType().Name}} isn't a case of {m.Name}.\", nameof(leaf)),");
        w.CloseBrace(";");
    }
}
