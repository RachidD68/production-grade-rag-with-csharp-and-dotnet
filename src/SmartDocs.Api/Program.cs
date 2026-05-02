// SmartDocs.Api — Phase 0 placeholder.
// Endpoints are introduced in Phase 2 (Ch 10): POST /api/ask + /api/ask/stream
// using TypedResults.ServerSentEvents from ASP.NET Core 10.
//
// For now this file just builds an empty host so the project compiles and
// integration tests can spin it up.

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new { status = "phase-0-placeholder" }))
    .WithName("Health");

app.Run();

namespace SmartDocs.Api
{
    /// <summary>Marker type so integration tests can reference the API entrypoint via <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
    public partial class Program;
}
