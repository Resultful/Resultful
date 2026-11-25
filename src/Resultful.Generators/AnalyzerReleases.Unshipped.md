; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
RFG0001 | Resultful.Generators | Error | A shape's Success type overlaps its error types
RFG0002 | Resultful.Generators | Warning | A concrete type inside a [ClosedHierarchy] isn't sealed
RFG0003 | Resultful.Generators | Error | An open generic type derives from a [ClosedHierarchy] base
RFG0004 | Resultful.Generators | Error | A public shape exposes a less accessible leaf
RFG0005 | Resultful.Generators | Error | An attributed declaration is nested in a type that isn't partial
RFG0006 | Resultful.Generators | Error | The attribute is on a declaration the generator can't extend
RFG0007 | Resultful.Generators | Error | A case of an error union doesn't derive from Error
RFG0008 | Resultful.Generators | Error | Two leaves of one hierarchy or union share a simple name
