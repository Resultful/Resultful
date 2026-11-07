using PetStore.Api;

// The whole fake server. Everything it does comes from the spec.
var fake = new PetStoreFake();

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddPetStore();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options => options.AddPetStoreSchemas());
builder.Services.AddSingleton(fake);
builder.Services.AddSingleton<IPetsHandlers>(fake);

var app = builder.Build();
app.MapOpenApi();
app.MapPetStore();
app.MapPetStoreFakeControl(fake);
app.Run();
