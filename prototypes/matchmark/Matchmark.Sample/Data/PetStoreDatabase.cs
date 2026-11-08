using Matchmark.Conformance;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace PetStore.Data;

public static class PetStoreDatabase
{
    public const string DefaultConnectionString = "Data Source=petstore.db";
    public const string DefaultSeed = "Demo";

    /// <summary>
    /// SQLite through EF Core, with every <see cref="SeedScenario"/> registered and the one named by
    /// <c>PetStore:Seed</c> wired into EF Core's seeding. Configuration is read when services resolve, not at startup,
    /// so test hosts can override it.
    /// </summary>
    public static IServiceCollection AddPetStoreDatabase(this IServiceCollection services)
    {
        services.AddSingleton<SeedScenario, EmptySeed>();
        services.AddSingleton<SeedScenario, DemoSeed>();
        services.AddSingleton<SeedScenario, CrowdedSeed>();

        services.AddSingleton(sp =>
        {
            var name = sp.GetRequiredService<IConfiguration>()["PetStore:Seed"] ?? DefaultSeed;
            var scenarios = sp.GetServices<SeedScenario>().ToList();
            return new ChosenSeed(
                scenarios.SingleOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"No seed scenario '{name}'. Known: {string.Join(", ", scenarios.Select(s => s.Name))}."));
        });

        services.AddSingleton(sp => new SqliteKeepAlive(ConnectionString(sp)));
        services.AddSingleton<SessionDatabases>();
        services.AddHttpContextAccessor();

        services.AddDbContext<PetStoreDb>((sp, options) =>
        {
            // Sessions mode (PetStore:Sessions): each Matchmark session has its own database, created when it starts.
            if (Sessions(sp))
            {
                var session = sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.MatchmarkSession() ?? SessionDatabases.Default;
                options.UseSqlite(sp.GetRequiredService<SessionDatabases>().ConnectionString(session));
                return;
            }

            var seed = sp.GetRequiredService<ChosenSeed>().Scenario;
            options
                .UseSqlite(ConnectionString(sp))
                .UseSeeding((context, _) => seed.Seed(context))
                .UseAsyncSeeding((context, _, cancellationToken) => seed.SeedAsync(context, cancellationToken));
        });

        return services;
    }

    /// <summary>
    /// Creates the schema if needed, which runs the chosen seed scenario. In sessions mode, that is the database of
    /// requests with no session; each session's is created when it starts.
    /// </summary>
    public static async Task EnsurePetStoreDatabaseAsync(this IServiceProvider services)
    {
        if (Sessions(services))
        {
            await services.GetRequiredService<SessionDatabases>().StartAsync(SessionDatabases.Default, services.GetRequiredService<ChosenSeed>().Scenario.Name);
            return;
        }

        services.GetRequiredService<SqliteKeepAlive>();
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PetStoreDb>().Database.EnsureCreatedAsync();
    }

    private static bool Sessions(IServiceProvider sp)
        => string.Equals(sp.GetRequiredService<IConfiguration>()["PetStore:Sessions"], "true", StringComparison.OrdinalIgnoreCase);

    private static string ConnectionString(IServiceProvider sp)
        => sp.GetRequiredService<IConfiguration>().GetConnectionString("PetStore") ?? DefaultConnectionString;
}

/// <summary>The scenario named by <c>PetStore:Seed</c>. Its own type, so resolving it never resolves itself.</summary>
public sealed record ChosenSeed(SeedScenario Scenario);

/// <summary>
/// A shared in-memory SQLite database (<c>Mode=Memory;Cache=Shared</c>) lives only while a connection is open; this
/// holds one open for the app's lifetime. Each DbContext still opens its own connection, so requests run in parallel.
/// </summary>
public sealed class SqliteKeepAlive : IDisposable
{
    private readonly SqliteConnection? _connection;

    public SqliteKeepAlive(string connectionString)
    {
        if (new SqliteConnectionStringBuilder(connectionString).Mode == SqliteOpenMode.Memory)
        {
            _connection = new SqliteConnection(connectionString);
            _connection.Open();
        }
    }

    public void Dispose() => _connection?.Dispose();
}
