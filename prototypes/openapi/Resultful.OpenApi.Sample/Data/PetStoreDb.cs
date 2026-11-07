using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetStore.Api;

namespace PetStore.Data;

/// <summary>A stored pet. Kept apart from the generated <see cref="Pet"/>: the spec owns the API shape, this owns the table.</summary>
public sealed class PetRecord
{
    public long Id { get; set; }

    public required string Name { get; set; }

    public string? Tag { get; set; }

    public PetStatus Status { get; set; }

    public required PetDetails Details { get; set; }

    public Pet ToPet() => new() { Id = Id, Name = Name, Tag = Tag, Status = Status, Details = Details };
}

public sealed class PetStoreDb(DbContextOptions<PetStoreDb> options) : DbContext(options)
{
    public DbSet<PetRecord> Pets => Set<PetRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var pet = modelBuilder.Entity<PetRecord>();
        pet.ToTable("Pets");

        // Names are unique ignoring case, as createPet's 409 says; NOCASE makes '=' in queries ignore case too.
        pet.Property(p => p.Name).UseCollation("NOCASE");
        pet.HasIndex(p => p.Name).IsUnique();

        pet.Property(p => p.Status).HasConversion<string>();

        // The oneOf details are stored as the same JSON the API uses, discriminator included.
        pet.Property(p => p.Details).HasConversion(
            details => JsonSerializer.Serialize(details, JsonSerializerOptions.Default),
            json => JsonSerializer.Deserialize<PetDetails>(json, JsonSerializerOptions.Default)!);
    }
}
