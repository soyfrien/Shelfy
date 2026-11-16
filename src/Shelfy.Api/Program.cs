using Microsoft.AspNetCore.HttpOverrides;
using Shelfy.Features;
using Shelfy.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

builder.Services.AddShelfyInfrastructure(builder.Configuration);
builder.Services.AddShelfyFeatures(builder.Configuration);

var app = builder.Build();

// Behind nginx (loopback): trust X-Forwarded-Proto so absolute URLs (link-preview tags) use https.
app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor });

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// The web UI (wwwroot): index.html at "/", shelf template at /s/{id}. HTML/JS/CSS are revalidated on every load so a
// redeploy shows up immediately on phones.
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache"
});

// Accounts (ADR-012): Identity cookie for the pages and the API.
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapShelfyFeatures();

app.Run();

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program;
