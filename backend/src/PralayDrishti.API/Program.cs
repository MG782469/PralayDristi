using PralayDrishti.API.Extensions;
using PralayDrishti.Application;
using PralayDrishti.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddPresentation(builder.Configuration);

var app = builder.Build();
await app.UsePresentationAsync();
app.Run();

// exposed for WebApplicationFactory in the integration tests
public partial class Program { }
