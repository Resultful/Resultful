namespace Resultful.Compatibility.UpgradeLibrary;

/// <summary>Code compiled against 0.2.0-beta05, called by an app that restores the packed package.</summary>
public static class Parser
{
    public static Result<int> Parse(string text) =>
        int.TryParse(text, out var value) ? value.Ok() : Result.Fail<int>($"not a number: {text}");
}
