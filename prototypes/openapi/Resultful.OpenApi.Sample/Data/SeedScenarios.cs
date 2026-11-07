using Microsoft.EntityFrameworkCore;
using PetStore.Api;

namespace PetStore.Data;

/// <summary>
/// The data a database starts with. One is chosen by name (<c>PetStore:Seed</c>) and run through EF Core's
/// <c>UseSeeding</c>/<c>UseAsyncSeeding</c> when the database is created, so tests and environments swap the starting
/// state without touching the app.
/// </summary>
public abstract class SeedScenario
{
    public abstract string Name { get; }

    protected abstract IEnumerable<PetRecord> Pets();

    /// <summary>EF Core runs seeding every time the database is ensured, so this only adds to an empty one.</summary>
    public void Seed(DbContext context)
    {
        var db = (PetStoreDb)context;
        if (!db.Pets.Any())
        {
            db.Pets.AddRange(Pets());
            db.SaveChanges();
        }
    }

    public async Task SeedAsync(DbContext context, CancellationToken cancellationToken)
    {
        var db = (PetStoreDb)context;
        if (!await db.Pets.AnyAsync(cancellationToken))
        {
            db.Pets.AddRange(Pets());
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    protected static PetRecord Dog(string name, PetStatus status = PetStatus.Available)
        => new() { Name = name, Status = status, Details = new Dog { GoodBoy = true } };

    protected static PetRecord Cat(string name, int livesLeft, PetStatus status = PetStatus.Available)
        => new() { Name = name, Status = status, Details = new Cat { LivesLeft = livesLeft } };
}

/// <summary>No pets.</summary>
public sealed class EmptySeed : SeedScenario
{
    public override string Name => "Empty";

    protected override IEnumerable<PetRecord> Pets() => [];
}

/// <summary>A small shelter: one of each kind, one already adopted.</summary>
public sealed class DemoSeed : SeedScenario
{
    public override string Name => "Demo";

    protected override IEnumerable<PetRecord> Pets() =>
    [
        Dog("Rex"),
        Cat("Tom", livesLeft: 7),
        Dog("Bella", PetStatus.Adopted),
    ];
}

/// <summary>More pets than one page (listPets returns 100 by default).</summary>
public sealed class CrowdedSeed : SeedScenario
{
    public override string Name => "Crowded";

    protected override IEnumerable<PetRecord> Pets()
        => Enumerable.Range(1, 150).Select(i => i % 2 == 0 ? Dog($"Dog {i:000}") : Cat($"Cat {i:000}", livesLeft: i % 10));
}
