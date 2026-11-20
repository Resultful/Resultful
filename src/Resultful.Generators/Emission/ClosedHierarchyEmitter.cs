using System.Linq;
using Resultful.Generators.Models;

namespace Resultful.Generators.Emission;

internal static class ClosedHierarchyEmitter
{
    public static string Emit(ClosedHierarchyModel model)
    {
        var w = new SourceWriter();
        w.OpenNamespaceAndEnclosing(model.Namespace, model.EnclosingTypes);

        w.WriteLine($"{model.Accessibility} abstract partial {model.Keyword} {model.Name}");
        w.OpenBrace();

        if (model.Leaves.Length > 0)
        {
            var matchParams = string.Join(
                ", ",
                model.Leaves.Select(l => $"global::System.Func<{l.FullName}, TResult> on{l.ParamSuffix}"));

            w.WriteLine($"public TResult Match<TResult>({matchParams})");
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

            w.WriteLine($"public void Switch({switchParams})");
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
                w.WriteLine();
                w.WriteLine(
                    $"public static bool TryMap<TResult>(global::Resultful.Error error, {matchParams}, [global::System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out TResult mapped)");
                w.OpenBrace();
                w.WriteLine("foreach (global::Resultful.Error cause in error.Causes())");
                w.OpenBrace();
                w.WriteLine("switch (cause)");
                w.OpenBrace();
                for (var i = 0; i < model.Leaves.Length; i++)
                {
                    var leaf = model.Leaves[i];
                    w.WriteLine($"case {leaf.FullName} v: mapped = on{leaf.ParamSuffix}(v); return true;");
                }

                w.CloseBrace();
                w.CloseBrace();
                w.WriteLine("mapped = default;");
                w.WriteLine("return false;");
                w.CloseBrace();
            }
        }

        w.CloseBrace();
        w.CloseNamespaceAndEnclosing(model.Namespace, model.EnclosingTypes);
        return w.ToString();
    }
}
