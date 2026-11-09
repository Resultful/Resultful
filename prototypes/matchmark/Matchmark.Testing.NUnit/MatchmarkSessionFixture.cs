using NUnit.Framework;

namespace Matchmark.Testing.NUnit;

/// <summary>
/// A base for NUnit fixtures that test a consumer against a session host: before each test it starts a session named
/// after the test (<see cref="MatchmarkSession.IdFor"/>), with the seed, pathways, copy and scenario the fixture gives,
/// and after it ends the session. With <see cref="RequireComplete"/>, a test whose session report isn't complete fails,
/// with the report's reasons.
/// </summary>
public abstract class MatchmarkSessionFixture
{
    private MatchmarkSession? _session;

    /// <summary>The control client: the session host's address.</summary>
    protected abstract HttpClient Control { get; }

    /// <summary>The current test's session.</summary>
    protected MatchmarkSession Session => _session ?? throw new InvalidOperationException("The session starts before each test.");

    /// <summary>The seed for each test's session; none by default.</summary>
    protected virtual object? Seed => null;

    /// <summary>The pathways each test's session requires; none by default.</summary>
    protected virtual IEnumerable<string>? Pathways => null;

    /// <summary>The copy each session uses: the twin by default.</summary>
    protected virtual string Copy => SessionCopy.Twin;

    /// <summary>Expected calls for each test's session; none by default.</summary>
    protected virtual Matchmark.Fakes.Scenario? Scenario => null;

    /// <summary>Whether a test fails when its session's report isn't complete (pathways covered, scenario kept, no invalid calls).</summary>
    protected virtual bool RequireComplete => false;

    [SetUp]
    public async Task StartMatchmarkSession()
        => _session = await MatchmarkSession.StartAsync(Control, MatchmarkSession.IdFor(TestContext.CurrentContext.Test.FullName), Seed, Pathways, Copy, Scenario)
            .ConfigureAwait(false);

    [TearDown]
    public async Task EndMatchmarkSession()
    {
        if (_session is null)
        {
            return;
        }

        try
        {
            if (RequireComplete && TestContext.CurrentContext.Result.Outcome.Status == global::NUnit.Framework.Interfaces.TestStatus.Passed)
            {
                var report = await _session.ReportAsync().ConfigureAwait(false);
                if (!report.Complete)
                {
                    Assert.Fail($"The session '{_session.Id}' isn't complete: {string.Join("; ", report.Reasons)}");
                }
            }
        }
        finally
        {
            await _session.DisposeAsync().ConfigureAwait(false);
            _session = null;
        }
    }
}
