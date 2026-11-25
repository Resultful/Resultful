using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Resultful.AspNetCore;

namespace Resultful.FluentValidation.Tests;

// Every line of every C# block in src/Resultful.FluentValidation/README.md appears in this file's compiled snippets,
// so a README sample that stops compiling fails here.
public sealed partial class ReadmeTests
{
    public sealed record Account(int Id);

    sealed class Accounts
    {
        public Result<Account> Open(SignUp signUp) => Result.Ok(new Account(1));
    }

    sealed class PasswordValidator : AbstractValidator<Passwords>
    {
        public PasswordValidator()
        {
            RuleFor(s => s.ConfirmPassword).Equal(s => s.Password).WithMessage("The passwords don't match.");
        }
    }

    public sealed record Passwords(string Password, string ConfirmPassword);

    static async Task<Result<Account>> Snippet(SignUp signUp, Accounts accounts, CancellationToken cancellationToken)
    {
        Validation<SignUp> checkedSignUp = new SignUpValidator().ValidateToResult(signUp);
        Validation<SignUp> checkedAsync = await new SignUpValidator().ValidateToResultAsync(signUp, cancellationToken);

        Result<Account> account = checkedSignUp.ToResult() // the failures travel as one ValidationFailed<Error>
            .Bind(accounts.Open);
        Assert.Equal(checkedSignUp, checkedAsync);
        return account;
    }

    static void ConfigureSnippet(IServiceCollection services) =>
        services.AddResultful(o => o.ValidationKey = e => (e as FluentValidationError)?.PropertyName ?? "");

    [Fact]
    public async Task The_snippets_run()
    {
        var opened = await Snippet(new SignUp("ann", "a@b.c"), new Accounts(), TestContext.Current.CancellationToken);
        var refused = await Snippet(new SignUp("", "a@b.c"), new Accounts(), TestContext.Current.CancellationToken);
        ConfigureSnippet(new ServiceCollection());

        Assert.Equal(new Account(1), opened.Value);
        Assert.IsType<ValidationFailed<Error>>(refused.Error);
        Assert.Equal("The passwords don't match.", Assert.Single(new PasswordValidator().Validate(new Passwords("a", "b")).Errors).ErrorMessage);
    }

    [Fact]
    public void Every_readme_snippet_line_is_in_this_file()
    {
        var root = Root();
        var self = File.ReadAllText(Path.Combine(root, "tests", "Resultful.FluentValidation.Tests", "ReadmeTests.cs"));
        var readme = File.ReadAllText(Path.Combine(root, "src", "Resultful.FluentValidation", "README.md"));
        var lines = CSharpBlock().Matches(readme).SelectMany(m => m.Groups[1].Value.Split('\n')).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.Contains(line, self, StringComparison.Ordinal));
    }

    // The repository root, found from the test binary (CallerFilePath is path-mapped in CI builds).
    static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "Resultful.slnx"))) directory = directory.Parent ?? throw new InvalidOperationException("Resultful.slnx not found");
        return directory.FullName;
    }

    [GeneratedRegex("```csharp\\n(.*?)```", RegexOptions.Singleline)]
    private static partial Regex CSharpBlock();
}
