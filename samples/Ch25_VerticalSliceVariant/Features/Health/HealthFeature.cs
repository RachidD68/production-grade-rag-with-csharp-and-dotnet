using Microsoft.Extensions.Options;
using SmartDocs.Core.Configuration;

namespace RagInDotNet.Samples.Ch25_VerticalSliceVariant.Features.Health;

/// <summary>Vertical-Slice "Health" feature: just one endpoint + its response shape.</summary>
public static class HealthFeature
{
    public sealed record HealthResponse(string Status, string Provider);

    public static void MapEndpoints(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapGet("/health", (IOptions<LlmClientOptions> opts) =>
            new HealthResponse("ok", opts.Value.Provider.ToString()))
            .WithName("Health");
    }
}
