using Microsoft.EntityFrameworkCore;
using PetStore.Api;
using PetStore.Data;
using Resultful;

namespace PetStore;

/// <summary>Pets in SQLite. The handlers only see Options and Results, never EF Core or HTTP.</summary>
public sealed class PetRepository(PetStoreDb db)
{
    public async Task<IReadOnlyList<Pet>> List(int limit, CancellationToken cancellationToken)
        => (await db.Pets.AsNoTracking().OrderBy(p => p.Id).Take(limit).ToListAsync(cancellationToken))
            .ConvertAll(p => p.ToPet());

    public async Task<Option<Pet>> Find(long id, CancellationToken cancellationToken)
        => await db.Pets.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) is { } pet
            ? pet.ToPet()
            : Option<Pet>.None;

    /// <summary>Case-insensitive, through the column's NOCASE collation.</summary>
    public Task<bool> NameTaken(string name, long? exceptId, CancellationToken cancellationToken)
        => db.Pets.AnyAsync(p => p.Name == name && p.Id != exceptId, cancellationToken);

    public async Task<Pet> Add(NewPet pet, CancellationToken cancellationToken)
    {
        var record = new PetRecord { Name = pet.Name, Tag = pet.Tag, Status = PetStatus.Available, Details = pet.Details };
        db.Pets.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        return record.ToPet();
    }

    /// <summary>Replaces the pet with this id, or creates it with this id.</summary>
    public async Task<Pet> Save(long id, NewPet pet, CancellationToken cancellationToken)
    {
        var record = await db.Pets.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (record is null)
        {
            record = new PetRecord { Id = id, Name = pet.Name, Details = pet.Details };
            db.Pets.Add(record);
        }

        record.Name = pet.Name;
        record.Tag = pet.Tag;
        record.Details = pet.Details;
        await db.SaveChangesAsync(cancellationToken);
        return record.ToPet();
    }

    public async Task<bool> Remove(long id, CancellationToken cancellationToken)
        => await db.Pets.Where(p => p.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
}
