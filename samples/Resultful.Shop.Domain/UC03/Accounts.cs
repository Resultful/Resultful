namespace Resultful.Shop.Domain;

// Use case 3: validate first, then act.
#region snippet: uc03-error
public sealed record EmailTaken(Email Email) : Error($"{Email} is already registered");
#endregion

public sealed record Account(int Id, string Name, Email Email);
