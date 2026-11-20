using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Resultful.Generators.Models;

// A position the pipeline can cache: no SyntaxTree, so old trees aren't kept alive (building-the-generator.md rule 2).
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

        var mapped = location.GetLineSpan();
        return new SourcePosition(mapped.Path, location.SourceSpan, mapped.Span);
    }

    // Rebuilt against the current compilation's tree when the diagnostic is reported, so it is a source location
    // (the IDE, #pragma and the spec harness's markers all need one). An external file location is the fallback.
    public Location ToLocation(IReadOnlyDictionary<string, SyntaxTree>? trees) =>
        trees is not null && trees.TryGetValue(FilePath, out var tree) && Span.End <= tree.Length
            ? Location.Create(tree, Span)
            : Location.Create(FilePath, Span, LineSpan);
}

internal readonly record struct DiagnosticInfo(
    DiagnosticDescriptor Descriptor,
    SourcePosition? Position,
    EquatableArray<string> MessageArgs)
{
    public DiagnosticInfo(DiagnosticDescriptor descriptor, Location? location, params string[] messageArgs)
        : this(descriptor, SourcePosition.From(location), messageArgs)
    {
    }

    public DiagnosticInfo(DiagnosticDescriptor descriptor, SourcePosition? position, params string[] messageArgs)
        : this(descriptor, position, new EquatableArray<string>(messageArgs ?? Array.Empty<string>()))
    {
    }

    public Diagnostic ToDiagnostic(IReadOnlyDictionary<string, SyntaxTree>? trees)
    {
        var args = new object?[MessageArgs.Length];
        for (var i = 0; i < args.Length; i++)
        {
            args[i] = MessageArgs[i];
        }

        return Diagnostic.Create(Descriptor, Position?.ToLocation(trees) ?? Location.None, args);
    }
}
