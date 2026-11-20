using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Resultful.Generators.Models;

internal readonly record struct SourcePosition(
    string FilePath,
    TextSpan Span,
    LinePositionSpan LineSpan)
{
    public static SourcePosition? From(Location? location)
    {
        if (location is null || !location.IsInSource)
        {
            return null;
        }

        var mapped = location.GetMappedLineSpan();
        return new SourcePosition(mapped.Path, location.SourceSpan, mapped.Span);
    }

    public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);
}

internal readonly record struct DiagnosticInfo(
    DiagnosticDescriptor Descriptor,
    SourcePosition? Position,
    EquatableArray<string> MessageArgs)
{
    public DiagnosticInfo(DiagnosticDescriptor descriptor, Location? location, params string[] messageArgs)
        : this(
            descriptor,
            SourcePosition.From(location),
            new EquatableArray<string>(messageArgs ?? Array.Empty<string>()))
    {
    }

    public Diagnostic ToDiagnostic()
    {
        var args = new object?[MessageArgs.Length];
        for (var i = 0; i < args.Length; i++)
        {
            args[i] = MessageArgs[i];
        }

        return Diagnostic.Create(Descriptor, Position?.ToLocation() ?? Location.None, args);
    }
}
