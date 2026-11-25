using System.Text;

namespace Resultful.Diagnostics;

/// <summary>
/// What the hooks may say about an error: its leaf type name, <see cref="Error.Message"/>, <see cref="Error.Context"/>
/// and the type and message of each error in its cause chain. Never the leaf's own properties, which may hold personal
/// data (the same rule as <c>Error.ToString</c>).
/// </summary>
internal static class ErrorDescription
{
    /// <summary>The simple type name, with type arguments for a generic leaf: <c>ValidationFailed&lt;Error&gt;</c>.</summary>
    public static string TypeName(Type type)
    {
        if (!type.IsGenericType) return type.Name;
        var name = type.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        var builder = new StringBuilder(tick < 0 ? name : name[..tick]).Append('<');
        var arguments = type.GetGenericArguments();
        for (var i = 0; i < arguments.Length; i++)
        {
            if (i > 0) builder.Append(", ");
            builder.Append(TypeName(arguments[i]));
        }

        return builder.Append('>').ToString();
    }

    /// <summary>Each error below this one (through <see cref="Error.Cause"/> and <see cref="Error.Inner"/>, depth first) as "Type: Message".</summary>
    public static string[] Causes(Error error)
    {
        var causes = new List<string>();
        var first = true;
        foreach (var cause in error.Causes())
        {
            if (first) { first = false; continue; }
            causes.Add(TypeName(cause.GetType()) + ": " + cause.Message);
        }

        return causes.ToArray();
    }
}
