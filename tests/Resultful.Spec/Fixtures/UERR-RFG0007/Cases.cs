// Decision 17 point 6 (13b): every case of a shape's union must derive from Error (RFG0007, on the shape).
// spec: tfm net11.0
// spec: uses generator
using Resultful;
using Resultful.CodeAnalysis;
using Resultful.Spec.Support;

public sealed record OrderNotFound(int Id) : Error("nf");
public partial union Mixed(OrderNotFound, string);
public partial union Fine(OrderNotFound);

[ResultShape(typeof(Mixed), Success = typeof(int))] public readonly partial struct MixedResult; // expect: RFG0007
[ResultShape(typeof(Fine), Success = typeof(int))] public readonly partial struct FineResult;
