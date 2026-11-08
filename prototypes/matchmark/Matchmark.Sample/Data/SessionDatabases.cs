using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace PetStore.Data;

/// <summary>
/// An ephemeral database per Matchmark session (docs/reference/sessions.md, service sessions): each session id gets its
/// own in-memory SQLite database (<c>Mode=Memory;Cache=Shared</c>), created and seeded by a named
/// <see cref="SeedScenario"/> when the session starts, and gone when it ends, because an in-memory database lives only
/// while a connection to it is open. The DbContext for a request uses the database of the request's session.
/// </summary>
public sealed class SessionDatabases(IEnumerable<SeedScenario> scenarios) : IDisposable
{
    public const string Default = "default";

    private readonly ConcurrentDictionary<string, SqliteConnection> _open = new(StringComparer.Ordinal);

    // Unique per app, so two apps in one test process never share a session's database.
    private readonly string _instance = Guid.NewGuid().ToString("N");

    /// <summary>The databases open now: one per live session.</summary>
    public IReadOnlyCollection<string> Open => _open.Keys.ToList();

    public string ConnectionString(string session) => $"Data Source=petstore-{_instance}-{session};Mode=Memory;Cache=Shared";

    /// <summary>A fresh database for the session, seeded by the scenario named (Empty when none): any old one is dropped first.</summary>
    public async Task StartAsync(string session, string? seed, CancellationToken cancellationToken = default)
    {
        End(session);
        var scenario = scenarios.SingleOrDefault(s => string.Equals(s.Name, seed ?? "Empty", StringComparison.OrdinalIgnoreCase))
                       ?? throw new ArgumentException($"No seed scenario '{seed}'. Known: {string.Join(", ", scenarios.Select(s => s.Name))}.", nameof(seed));
        var keepAlive = new SqliteConnection(ConnectionString(session));
        await keepAlive.OpenAsync(cancellationToken);
        _open[session] = keepAlive;

        var options = new DbContextOptionsBuilder<PetStoreDb>()
            .UseSqlite(ConnectionString(session))
            .UseSeeding((context, _) => scenario.Seed(context))
            .UseAsyncSeeding((context, _, token) => scenario.SeedAsync(context, token))
            .Options;
        await using var db = new PetStoreDb(options);
        await db.Database.EnsureCreatedAsync(cancellationToken);
    }

    /// <summary>Ends the session's database: closing its last connection discards it.</summary>
    public void End(string session)
    {
        if (_open.TryRemove(session, out var connection))
        {
            connection.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var session in _open.Keys.ToList())
        {
            End(session);
        }
    }
}
