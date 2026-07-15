// Chapter 25 — Vertical Slice Architecture variant.
//
// Same retrieval pipeline as SmartDocs.Api but organized by feature folder
// instead of by horizontal layer. Each "feature" (Ask, Health) owns its
// own request, response, handler, and endpoint mapper. Suitable for
// smaller projects (1-3 engineers) where the Clean / DDD layering of
// SmartDocs.Api adds more ceremony than value.

using RagInDotNet.Samples.Ch25_VerticalSliceVariant.Features.Ask;
using RagInDotNet.Samples.Ch25_VerticalSliceVariant.Features.Health;
using SmartDocs.Core.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSmartDocsCore(builder.Configuration);
AskFeature.RegisterServices(builder.Services);

var app = builder.Build();

HealthFeature.MapEndpoints(app);
AskFeature.MapEndpoints(app);

app.Run();

namespace RagInDotNet.Samples.Ch25_VerticalSliceVariant
{
    public partial class Program;
}
