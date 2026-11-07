using System.Collections.Concurrent;
using PetStore.Api;
using Resultful;

namespace PetStore;

/// <summary>An in-memory store. The handlers only see Options and Results, never HTTP.</summary>
public sealed class PetRepository
{
    private readonly ConcurrentDictionary<long, Pet> _pets = new();
    private long _nextId;

    public IReadOnlyList<Pet> List(int limit) => _pets.Values.OrderBy(p => p.Id).Take(limit).ToList();

    public Option<Pet> Find(long id) => _pets.TryGetValue(id, out var pet) ? pet : Option<Pet>.None;

    public bool NameTaken(string name, long? exceptId = null)
        => _pets.Values.Any(p => p.Id != exceptId && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public Pet Add(NewPet pet) => Save(Interlocked.Increment(ref _nextId), pet);

    public Pet Save(long id, NewPet pet)
    {
        var saved = new Pet { Id = id, Name = pet.Name, Tag = pet.Tag, Status = PetStatus.Available, Details = pet.Details };
        _pets[id] = saved;
        return saved;
    }

    public bool Remove(long id) => _pets.TryRemove(id, out _);
}
