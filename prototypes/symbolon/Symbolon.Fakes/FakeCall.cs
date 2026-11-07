namespace Symbolon.Fakes;

/// <summary>A call a fake received: the operationId and its arguments by parameter name (the request body is <c>body</c>).</summary>
public sealed record FakeCall(string Operation, IReadOnlyDictionary<string, object?> Arguments);
