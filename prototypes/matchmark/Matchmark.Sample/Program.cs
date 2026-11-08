using Matchmark.Conformance;
using PetStore;
using PetStore.Api;
using PetStore.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPetStore();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options => options.AddPetStoreSchemas());

builder.Services.AddPetStoreDatabase();
builder.Services.AddScoped<PetRepository>();
builder.Services.AddScoped<IPetsHandlers, PetsHandlers>();

var app = builder.Build();

await app.Services.EnsurePetStoreDatabaseAsync();

// Behind Matchmark's session host (PetStore:Sessions), each request uses its session's own database.
app.UseMatchmarkSessionScope();

app.MapOpenApi();
app.MapPetStore();

await app.RunAsync();

public partial class Program;
