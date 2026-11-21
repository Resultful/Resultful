using System.Linq;
using Resultful.Generators.Models;

namespace Resultful.Generators.Emission;

// Tier 1 (decision 16 item 3): the constructor, Cases, Match, Switch and TryMap.
internal static class ClosedHierarchyEmitter
{
    public static string Emit(ClosedHierarchyModel model)
    {
        var w = new SourceWriter();
        w.OpenNamespaceAndEnclosing(model.Namespace, model.EnclosingTypes);

        w.WriteLine($"{model.Accessibility} abstract partial {model.Keyword} {model.Name}");
        w.OpenBrace();

        if (model.EmitConstructor)
        {
            // Closes the ordinary way to derive from another assembly; the analyzer closes the rest (decision 15).
            w.WriteLine($"private protected {model.Name}(string message) : base(message)");
            w.OpenBrace();
            w.CloseBrace();
        }

        if (model.EmitMembers)
        {
            if (model.EmitConstructor)
            {
                w.WriteLine();
            }

            var hides = model.HidesCases ? "new " : string.Empty;
            var cases = string.Join(", ", model.Leaves.Select(l => $"typeof({l.FullName})"));
            w.WriteLine($"public static {hides}global::System.Collections.Generic.IReadOnlyList<global::System.Type> Cases {{ get; }} = new global::System.Type[] {{ {cases} }};");

            if (model.DispatchAccessibility is { } access)
            {
                var hidesDispatch = model.HidesDispatch ? "new " : string.Empty;
                var matchParams = string.Join(
                    ", ",
                    model.Leaves.Select(l => $"global::System.Func<{l.FullName}, TResult> on{l.ParamSuffix}"));

                w.WriteLine();
                w.WriteLine($"{access} {hidesDispatch}TResult Match<TResult>({matchParams})");
                w.OpenBrace();
                w.WriteLine("switch (this)");
                w.OpenBrace();
                for (var i = 0; i < model.Leaves.Length; i++)
                {
                    var leaf = model.Leaves[i];
                    w.WriteLine($"case {leaf.FullName} v: return on{leaf.ParamSuffix}(v);");
                }

                w.WriteLine("default: throw new global::System.InvalidOperationException(\"Unexpected hierarchy member.\");");
                w.CloseBrace();
                w.CloseBrace();
                w.WriteLine();

                var switchParams = string.Join(
                    ", ",
                    model.Leaves.Select(l => $"global::System.Action<{l.FullName}> on{l.ParamSuffix}"));

                w.WriteLine($"{access} {hidesDispatch}void Switch({switchParams})");
                w.OpenBrace();
                w.WriteLine("switch (this)");
                w.OpenBrace();
                for (var i = 0; i < model.Leaves.Length; i++)
                {
                    var leaf = model.Leaves[i];
                    w.WriteLine($"case {leaf.FullName} v: on{leaf.ParamSuffix}(v); return;");
                }

                w.WriteLine("default: throw new global::System.InvalidOperationException(\"Unexpected hierarchy member.\");");
                w.CloseBrace();
                w.CloseBrace();

                if (model.DerivesFromError)
                {
                    // Walks error.Causes() outer to inner and maps the first cause that is one of these leaves.
                    w.WriteLine();
                    w.WriteLine(
                        $"{access} static {hidesDispatch}bool TryMap<TResult>(global::Resultful.Error error, {matchParams}, [global::System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out TResult result)");
                    w.OpenBrace();
                    w.WriteLine("foreach (global::Resultful.Error cause in error.Causes())");
                    w.OpenBrace();
                    w.WriteLine("switch (cause)");
                    w.OpenBrace();
                    for (var i = 0; i < model.Leaves.Length; i++)
                    {
                        var leaf = model.Leaves[i];
                        w.WriteLine($"case {leaf.FullName} v: result = on{leaf.ParamSuffix}(v); return true;");
                    }

                    w.CloseBrace();
                    w.CloseBrace();
                    w.WriteLine("result = default;");
                    w.WriteLine("return false;");
                    w.CloseBrace();
                }
            }
        }

        w.CloseBrace();
        w.CloseNamespaceAndEnclosing(model.Namespace, model.EnclosingTypes);
        return w.ToString();
    }
}
