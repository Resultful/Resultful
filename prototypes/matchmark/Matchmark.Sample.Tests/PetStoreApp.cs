using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Matchmark.Sample.Tests;

/// <summary>
/// The app over its own in-memory SQLite database, started from a named seed scenario
/// (<c>Empty</c>, <c>Demo</c>, <c>Crowded</c>). Each instance is isolated from every other.
/// </summary>
public sealed class PetStoreApp(string seed = "Empty") : WebApplicationFactory<PetStore.PetsHandlers>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:PetStore", $"Data Source=petstore-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        builder.UseSetting("PetStore:Seed", seed);
    }
}
