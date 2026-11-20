using Resultful.Shop.Api;

// The Shop's HTTP surface. Each use case adds its services and endpoints only when it is compiled (Shop.targets).
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddShop();

var app = builder.Build();
app.MapOpenApi();
app.MapShop();
await app.RunAsync();
