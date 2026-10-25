using Shelfy.Features;
using Shelfy.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

builder.Services.AddShelfyInfrastructure(builder.Configuration);
builder.Services.AddShelfyFeatures(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHealthChecks("/health");
app.MapShelfyFeatures();

app.Run();

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program;
