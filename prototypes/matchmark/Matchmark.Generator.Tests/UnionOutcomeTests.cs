using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// Outcomes shaped by the consumer's compiler (Q55): a project on C# 15 (.NET 11) gets each operation's outcomes as a C# 15
/// union, one on C# 14 (.NET 10) the plain classes, and MatchmarkOutcomes overrides either way. Compiled with Roslyn 5.9,
/// where C# 15 is the preview language version, on .NET 10, so the union types are generated too.
/// </summary>
[TestFixture]
public sealed class UnionOutcomeTests
{
    private static readonly string PetStore = Path.Combine(DerivedEvidenceTests.Paths("petstore").Spec);

    /// <summary>A consumer's own code: an exhaustive switch over the cases, with no default arm, which only a union allows.</summary>
    private const string Consumer = """
        namespace Petstore.Api;

        public static class Describe
        {
            public static string Error(GetPetError error) => error switch
            {
                GetPetNotFound notFound => $"not found: {notFound.Body.Title}",
            };

            public static string Made() => Error(GetPetError.NotFound(new global::Microsoft.AspNetCore.Mvc.ProblemDetails { Title = "no pet 7" }));
        }
        """;

    [Test]
    public void On_CSharp_15_each_operation_s_outcomes_are_a_union_the_compiler_checks()
    {
        var (assembly, _, sources, warnings) = GeneratedServer.BuildWithWarnings("Petstore", PetStore, [], Consumer, withRules: false, csharp15: true);

        warnings.Select(w => w.ToString()).Should().BeEmpty();
        GeneratedServer.LastSourceWarnings!.Should().BeEmpty("the switch with no default arm is exhaustive");
        var error = assembly.GetType("Petstore.Api.GetPetError")!;
        error.IsValueType.Should().BeTrue("a union declaration is a struct");
        error.GetCustomAttributesData().Select(a => a.AttributeType.FullName).Should().Contain("System.Runtime.CompilerServices.UnionAttribute");
        sources.Select(s => s.Hint).Should().Contain("Matchmark.Unions.g.cs", ".NET 10 has no union types, so they're generated");
        assembly.GetType("Petstore.Api.Describe")!.GetMethod("Made")!.Invoke(null, null).Should().Be("not found: no pet 7");
    }

    [Test]
    public void A_switch_that_misses_a_case_is_a_warning_on_CSharp_15()
    {
        // The proof the compiler checks: drop a case from a union with two, and it says so.
        const string Missing = """
            namespace Petstore.Api;

            public static class Describe
            {
                public static string Error(CreatePetError error) => error switch
                {
                    CreatePetConflict => "conflict",
                };
            }
            """;
        GeneratedServer.Build("Petstore", PetStore, [], Missing, withRules: false, csharp15: true);
        GeneratedServer.LastSourceWarnings!.Select(d => d.Id).Should().Contain("CS8509", "CreatePetBadRequest isn't handled");

        // And the same switch, complete, has no warning.
        GeneratedServer.Build("Petstore", PetStore, [], Missing.Replace("CreatePetConflict => \"conflict\",", "CreatePetConflict => \"conflict\", CreatePetBadRequest => \"bad\",", StringComparison.Ordinal), withRules: false, csharp15: true);
        GeneratedServer.LastSourceWarnings!.Should().BeEmpty();
    }

    [Test]
    public void On_CSharp_14_outcomes_stay_plain_classes()
    {
        var (assembly, _, sources, _) = GeneratedServer.BuildWithWarnings("Petstore", PetStore, [], string.Empty, withRules: false);

        assembly.GetType("Petstore.Api.GetPetError")!.IsClass.Should().BeTrue();
        sources.Select(s => s.Hint).Should().NotContain("Matchmark.Unions.g.cs");
    }

    [Test]
    public void MatchmarkOutcomes_Plain_keeps_classes_on_CSharp_15()
    {
        var (assembly, _, sources, _) = GeneratedServer.BuildWithWarnings("Petstore", PetStore, [], string.Empty, withRules: false, outcomes: "Plain", csharp15: true);

        assembly.GetType("Petstore.Api.GetPetError")!.IsClass.Should().BeTrue();
        sources.Select(s => s.Hint).Should().NotContain("Matchmark.Unions.g.cs");
    }

}
