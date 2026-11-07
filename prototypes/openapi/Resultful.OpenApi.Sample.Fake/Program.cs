using PetStore.Api;

// The whole fake server. Everything it does comes from the spec.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddPetStore();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options => options.AddPetStoreSchemas());
builder.Services.AddPetStoreFakeHub();

var app = builder.Build();
app.UsePetStoreFakeHub();      // sessions at /sessions/{id}/, unexpected calls as 500 problems
app.MapOpenApi();
app.MapPetStore();
app.MapPetStoreFakeControl();  // /__fake: script the default fake, start and verify sessions
app.Run();
