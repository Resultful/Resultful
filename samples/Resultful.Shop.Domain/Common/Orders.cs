namespace Resultful.Shop.Domain;

// Plain data shared by the use cases. Nothing here touches Resultful, so it compiles with every use case off.
public sealed record Order(int Id, string Name, decimal Total, string Card, string? LockedBy = null);

public sealed record OrderRow(int Id, string Name, decimal Total, string Card);
