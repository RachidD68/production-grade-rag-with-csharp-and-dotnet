using System.Diagnostics.Metrics;
using Microsoft.Extensions.AI;
using SmartDocs.Performance.Cost;

namespace SmartDocs.UnitTests.Performance;

public sealed class CostTrackingTests
{
    private static (double total, string? tenant) CaptureCost(Action act)
    {
        double total = 0;
        string? tenant = null;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == CostMeter.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
        {
            if (instrument.Name == "smartdocs.cost.usd")
            {
                total += value;
                foreach (var t in tags)
                {
                    if (t.Key == CostMeter.TenantTag)
                    {
                        tenant = t.Value as string;
                    }
                }
            }
        });
        listener.Start();
        act();
        listener.Dispose();
        return (total, tenant);
    }

    [Fact]
    public async Task ChatClient_records_cost_from_usage_tagged_by_tenant()
    {
        var pricing = new TokenPricing(InputPerToken: 1e-6, OutputPerToken: 2e-6, EmbeddingPerToken: 0);
        var inner = new UsageChatClient(inputTokens: 1_000, outputTokens: 500);
        var client = new CostTrackingChatClient(inner, tenant: "contoso", pricing);

        var (total, tenant) = CaptureCost(() =>
            client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]).GetAwaiter().GetResult());

        // 1000 * 1e-6 + 500 * 2e-6 = 0.001 + 0.001 = 0.002
        Assert.Equal(0.002, total, precision: 9);
        Assert.Equal("contoso", tenant);
        await Task.CompletedTask;
    }

    [Fact]
    public void EmbeddingGenerator_records_cost_from_usage_tagged_by_tenant()
    {
        var pricing = new TokenPricing(InputPerToken: 0, OutputPerToken: 0, EmbeddingPerToken: 5e-6);
        var inner = new UsageEmbeddingGenerator(inputTokens: 2_000);
        var generator = new CostTrackingEmbeddingGenerator(inner, tenant: "fabrikam", pricing);

        var (total, tenant) = CaptureCost(() =>
            generator.GenerateAsync(["a", "b"]).GetAwaiter().GetResult());

        Assert.Equal(0.01, total, precision: 9); // 2000 * 5e-6
        Assert.Equal("fabrikam", tenant);
    }

    private sealed class UsageChatClient : IChatClient
    {
        private readonly long _input;
        private readonly long _output;
        public UsageChatClient(long inputTokens, long outputTokens)
        {
            _input = inputTokens;
            _output = outputTokens;
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
            {
                Usage = new UsageDetails { InputTokenCount = _input, OutputTokenCount = _output },
            };
            return Task.FromResult(response);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }
    }

    private sealed class UsageEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private readonly long _input;
        public UsageEmbeddingGenerator(long inputTokens) => _input = inputTokens;

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            var embeddings = values.Select(_ => new Embedding<float>((float[])[1f, 0f])).ToList();
            var result = new GeneratedEmbeddings<Embedding<float>>(embeddings)
            {
                Usage = new UsageDetails { InputTokenCount = _input, TotalTokenCount = _input },
            };
            return Task.FromResult(result);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }
    }
}
