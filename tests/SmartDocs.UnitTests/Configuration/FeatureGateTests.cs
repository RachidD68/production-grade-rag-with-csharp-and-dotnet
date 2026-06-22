using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartDocs.Core.Configuration;

namespace SmartDocs.UnitTests.Configuration;

public sealed class FeatureGateTests
{
    private static IConfiguration Config(params (string Key, string Value)[] pairs)
    {
        var dict = pairs.ToDictionary(p => p.Key, p => (string?)p.Value, StringComparer.Ordinal);
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public void IsEnabled_reads_a_true_flag_from_the_Features_section()
    {
        var gate = new ConfigurationFeatureGate(
            Config(("Features:reranking.enabled", "true")));

        Assert.True(gate.IsEnabled("reranking.enabled"));
    }

    [Fact]
    public void IsEnabled_reads_a_false_flag()
    {
        var gate = new ConfigurationFeatureGate(
            Config(("Features:graph-retrieval.enabled", "false")));

        Assert.False(gate.IsEnabled("graph-retrieval.enabled"));
    }

    [Fact]
    public void IsEnabled_unknown_flag_is_disabled_fail_closed()
    {
        var gate = new ConfigurationFeatureGate(Config());

        Assert.False(gate.IsEnabled("never.defined"));
    }

    [Fact]
    public void GetValue_reads_a_typed_value()
    {
        var gate = new ConfigurationFeatureGate(
            Config(("Features:routing.model", "gpt-4o-mini")));

        Assert.Equal("gpt-4o-mini", gate.GetValue<string>("routing.model"));
    }

    [Fact]
    public void GetValue_returns_the_default_when_absent()
    {
        var gate = new ConfigurationFeatureGate(Config());

        Assert.Equal(20, gate.GetValue("reranking.candidateCount", 20));
    }

    [Fact]
    public void AddFeatureGate_registers_the_configuration_backed_gate()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(Config(("Features:reranking.enabled", "true")));
        services.AddFeatureGate();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var gate = scope.ServiceProvider.GetRequiredService<IFeatureGate>();

        Assert.IsType<ConfigurationFeatureGate>(gate);
        Assert.True(gate.IsEnabled("reranking.enabled"));
    }
}
