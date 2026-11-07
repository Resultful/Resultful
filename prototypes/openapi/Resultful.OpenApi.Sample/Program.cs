using Microsoft.AspNetCore.OpenApi;
using PetStore;
using PetStore.Api;
using PetStore.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPetStore();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options =>
    // Name schemas after their types, as the spec does (the default names oneOf members "PetDetailsDog").
    options.CreateSchemaReferenceId = type => type.Type.Namespace == typeof(Pet).Namespace
        ? type.Type.Name
        : OpenApiOptions.CreateDefaultSchemaReferenceId(type));

builder.Services.AddPetStoreDatabase();
builder.Services.AddScoped<PetRepository>();
builder.Services.AddScoped<IPetsHandlers, PetsHandlers>();

var app = builder.Build();

await app.Services.EnsurePetStoreDatabaseAsync();

app.MapOpenApi();
app.MapPetStore();

await app.RunAsync();

public partial class Program;
