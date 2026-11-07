// Scaffolded from petstore.yaml by symbolon scaffold. This file is yours: edit it freely, it is never
// regenerated. The contract it implements (the types, unions and handler interfaces) stays generated.
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using PetStore.Api;
using PetStore.Scaffolded;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddPetStoreConventions();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options => options.AddPetStoreSchemas());
builder.Services.AddScoped<IPetsHandlers, PetsHandlers>();

var app = builder.Build();
app.MapOpenApi();
app.MapControllers();
app.Run();
