// RF0010 (17, error): a type outside a [ClosedHierarchy] base's assembly derives from it, here through the record's
// protected copy constructor, which C# can't close (CS8878).
// spec: uses analyzers
using Resultful;
using Resultful.CodeAnalysis;

public sealed record Evil : OrderError { public Evil(OrderError original) : base(original) { } } // expect: RF0010
