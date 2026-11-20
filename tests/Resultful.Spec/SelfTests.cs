using Xunit;

namespace Resultful.Spec;

/// <summary>Each ledger rule and each harness check can fail: seeded fixtures and ledgers in a temporary folder.</summary>
public sealed class SelfTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("spec-self-test-").FullName;
    static readonly Lazy<SpecHarness> Real = new(() => new SpecHarness(Build.Real));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    Fixture Seed(string id, params (string File, string Text)[] files)
    {
        var dir = Path.Combine(_root, id);
        foreach (var (file, text) in files)
        {
            var path = Path.Combine(dir, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        return Fixture.Load(dir);
    }

    const string Passing = """
        using Resultful.Spec.Support;
        public static class Cases
        {
            [SpecCase] public static void Adds() => Check.Equal(4, 2 + 2, "sum");
        }
        """;

    [Fact]
    public void A_pending_fixture_that_passes_fails_the_ledger_rule()
    {
        var outcome = Real.Value.Evaluate(Seed("SEEDED-PASS", ("Case.cs", Passing)));
        Assert.Equal(Status.Pass, outcome.Status);
        var problem = Ledger.CheckOutcome(new LedgerLine("SEEDED-PASS", LedgerLine.Pending, "05", "x", 2), outcome);
        Assert.NotNull(problem);
        Assert.Contains("flip this line", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_done_fixture_that_fails_fails_the_ledger_rule()
    {
        var outcome = Real.Value.Evaluate(Seed("SEEDED-FAIL", ("Case.cs", Passing.Replace("2 + 2", "2 + 3", StringComparison.Ordinal))));
        Assert.Equal(Status.Fail, outcome.Status);
        Assert.NotNull(Ledger.CheckOutcome(new LedgerLine("SEEDED-FAIL", LedgerLine.Done, "05", "x", 2), outcome));
        Assert.Null(Ledger.CheckOutcome(new LedgerLine("SEEDED-FAIL", LedgerLine.Pending, "05", "x", 2), outcome));
    }

    [Fact]
    public void A_changed_fixture_fails_the_hash_rule()
    {
        var fixture = Seed("SEEDED-HASH", ("Case.cs", Passing));
        var line = new LedgerLine("SEEDED-HASH", LedgerLine.Pending, "05", Ledger.Hash(fixture.Directory), 2);
        Assert.Empty(Ledger.CheckStructure([line], Ledger.FixtureHashes(_root)));
        File.AppendAllText(Path.Combine(fixture.Directory, "Case.cs"), "// edited\n");
        Assert.Contains(Ledger.CheckStructure([line], Ledger.FixtureHashes(_root)), p => p.Contains("fixture changed", StringComparison.Ordinal));
    }

    [Fact]
    public void An_orphan_fixture_and_an_orphan_line_fail_the_structure_rule()
    {
        Seed("SEEDED-ORPHAN", ("Case.cs", Passing));
        var problems = Ledger.CheckStructure([new LedgerLine("NO-SUCH-FIXTURE", LedgerLine.Pending, "05", "abc", 2)], Ledger.FixtureHashes(_root));
        Assert.Contains(problems, p => p.Contains("Fixtures/SEEDED-ORPHAN has no line", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("NO-SUCH-FIXTURE has no directory", StringComparison.Ordinal));
    }

    [Fact]
    public void A_line_whose_fixture_is_to_come_may_not_be_done()
    {
        var problems = Ledger.CheckStructure([new LedgerLine("UC99", LedgerLine.Done, "04d", "-", 2)], new Dictionary<string, string>());
        Assert.Contains(problems, p => p.Contains("done but has no fixture", StringComparison.Ordinal));
    }

    [Fact]
    public void A_bad_state_and_a_duplicate_line_are_reported()
    {
        var problems = new List<string>();
        Ledger.Parse([Ledger.Header, "A\tpending\t05\tx", "A\tmaybe\t05\tx"], problems);
        Assert.Contains(problems, p => p.Contains("state must be", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("appears 2 times", StringComparison.Ordinal));
    }

    [Fact]
    public void A_missing_expected_diagnostic_fails_the_markers()
    {
        var outcome = Real.Value.Evaluate(Seed("SEEDED-MISSING", ("Case.cs", "public static class C { public static int M() => 1; } // expect: CS0029\n")));
        Assert.Equal(Status.Fail, outcome.Status);
        Assert.Contains("expected CS0029 at Case.cs:1, not reported", outcome.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unexpected_diagnostic_fails_the_markers()
    {
        var outcome = Real.Value.Evaluate(Seed("SEEDED-EXTRA", ("Case.cs", "public static class C\n{\n    public static int M() => \"x\"; // expect: CS0029\n    public static int N() => \"y\";\n}\n")));
        Assert.Equal(Status.Fail, outcome.Status);
        Assert.Contains("unexpected", outcome.Detail, StringComparison.Ordinal);
        Assert.Contains("Case.cs:4", outcome.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Matching_markers_pass()
    {
        var outcome = Real.Value.Evaluate(Seed("SEEDED-MATCH", ("Case.cs", "public static class C { public static int M() => \"x\"; } // expect: CS0029\n")));
        Assert.True(outcome.Passed, outcome.Detail);
    }

    [Fact]
    public void An_expect_clean_file_fails_on_any_warning()
    {
        var outcome = Real.Value.Evaluate(Seed("SEEDED-CLEAN", ("Case.cs", "// expect-clean\npublic static class C { public static void M() { int unused = 1; } }\n")));
        Assert.Equal(Status.Fail, outcome.Status);
        Assert.Contains("CS0219", outcome.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_throwing_case_fails_and_names_the_case()
    {
        var outcome = Real.Value.Evaluate(Seed("SEEDED-THROW", ("Case.cs", Passing.Replace("Check.Equal(4, 2 + 2, \"sum\")", "throw new System.InvalidOperationException(\"boom\")", StringComparison.Ordinal))));
        Assert.Equal(Status.Fail, outcome.Status);
        Assert.Contains("Cases.Adds: InvalidOperationException: boom", outcome.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fixture_that_checks_nothing_fails()
    {
        var outcome = Real.Value.Evaluate(Seed("SEEDED-EMPTY", ("Case.cs", "public static class C { }\n")));
        Assert.Equal(Status.Fail, outcome.Status);
        Assert.Contains("checks nothing", outcome.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fixture_for_another_target_is_not_applicable()
    {
        var other = SpecPaths.Tfm == "net11.0" ? "net10.0" : "net11.0";
        var outcome = Real.Value.Evaluate(Seed("SEEDED-TFM", ("Case.cs", $"// spec: tfm {other}\n" + Passing)));
        Assert.Equal(Status.NotApplicable, outcome.Status);
    }

    [Fact]
    public void A_fixture_that_needs_an_unbuilt_package_fails()
    {
        var outcome = Real.Value.Evaluate(Seed("SEEDED-PKG", ("Case.cs", "// spec: uses Resultful.NotYet\n" + Passing)));
        Assert.Equal(Status.Fail, outcome.Status);
        Assert.Contains("not built yet: Resultful.NotYet", outcome.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_case_beside_an_expected_error_is_rejected()
    {
        var outcome = Real.Value.Evaluate(Seed("SEEDED-STRANDED", ("Case.cs", Passing + "\npublic static class Bad { static int M() => \"x\"; } // expect: CS0029\n")));
        Assert.Equal(Status.Fail, outcome.Status);
        Assert.Contains("can't run beside expected errors", outcome.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Cases_run_when_the_expected_errors_are_in_their_own_file()
    {
        var failing = Passing.Replace("2 + 2", "2 + 3", StringComparison.Ordinal);
        var outcome = Real.Value.Evaluate(Seed("SEEDED-SPLIT", ("Case.cs", failing), ("Errors.cs", "public static class Bad { static int M() => \"x\"; } // expect: CS0029\n")));
        Assert.Equal(Status.Fail, outcome.Status);
        Assert.Contains("Cases.Adds", outcome.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_marker_scoped_to_another_target_is_ignored()
    {
        var other = SpecPaths.Tfm == "net11.0" ? "net10.0" : "net11.0";
        var outcome = Real.Value.Evaluate(Seed("SEEDED-SCOPED", ("Case.cs", Passing + $"\npublic static class Bad {{ static int M() => \"x\"; }} // expect({SpecPaths.Tfm}): CS0029\n// expect({other}): CS0219\n"), ("More.cs", Passing.Replace("Cases", "More", StringComparison.Ordinal))));
        Assert.Contains("can't run beside expected errors", outcome.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("CS0219", outcome.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_library_swapped_after_compiling_runs_against_the_new_version()
    {
        var outcome = Real.Value.Evaluate(Seed("SEEDED-SWAP",
            ("lib/Lib.cs", "public static class Lib { public static int Version() => 1; }"),
            ("lib.v2/Lib.cs", "public static class Lib { public static int Version() => 2; }"),
            ("Case.cs", "using Resultful.Spec.Support;\npublic static class Cases { [SpecCase] public static void V2() => Check.Equal(2, Lib.Version(), \"version\"); }")));
        Assert.True(outcome.Passed, outcome.Detail);
    }
}
