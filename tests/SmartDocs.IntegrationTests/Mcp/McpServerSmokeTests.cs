extern alias McpHttp;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace SmartDocs.IntegrationTests.Mcp;

/// <summary>
/// Offline smoke test for the Streamable-HTTP MCP server
/// (<c>samples/Ch18_McpHttpServer</c>). It boots the host in-process with
/// <see cref="WebApplicationFactory{TEntryPoint}"/>, replaces JWT bearer with an
/// always-authenticated TEST scheme (no real token), connects a real
/// <see cref="McpClient"/> over the in-memory <see cref="HttpClient"/>, and runs
/// MCP <c>server/discover</c> + <c>tools/list</c> (spec 2026-07-28: no session,
/// no initialize handshake) — asserting the server identity came back from
/// discovery and that the full tool set the chapter promises is advertised.
/// <para>
/// The factory entry point is the HTTP sample's <c>Program</c> (exposed via
/// <c>public partial class Program;</c>). Because both the sample and
/// <c>SmartDocs.Api</c> synthesize a global <c>Program</c>, the sample is
/// referenced through the <c>McpHttp</c> extern alias to disambiguate.
/// </para>
/// </summary>
public sealed class McpServerSmokeTests : IClassFixture<McpServerSmokeTests.TestAuthFactory>
{
    private readonly TestAuthFactory _factory;

    public McpServerSmokeTests(TestAuthFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Discover_and_tools_list_advertise_the_full_tool_set()
    {
        var httpClient = _factory.CreateClient();
        httpClient.BaseAddress = new Uri(httpClient.BaseAddress!, "/");

        await using var mcp = await McpClient.CreateAsync(
            new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Endpoint = new Uri(httpClient.BaseAddress!, "mcp"),
                    TransportMode = HttpTransportMode.StreamableHttp,
                },
                httpClient,
                loggerFactory: null,
                ownsHttpClient: false));

        // A discovery-only bootstrap still identifies the server: the SDK fills
        // ServerInfo from the server/discover reply, not from an initialize session.
        Assert.NotNull(mcp.ServerInfo);

        var tools = await mcp.ListToolsAsync();
        var names = tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("search", names);
        Assert.Contains("search_and_rerank", names);
        Assert.Contains("graph_search", names);
        Assert.Contains("get_chunk", names);
        Assert.Contains("ingest", names);
    }

    /// <summary>
    /// Hosts the HTTP MCP sample with an always-authenticated test auth scheme so
    /// the smoke test exercises the <c>MapMcp().RequireAuthorization()</c> path
    /// without a real OAuth server or JWT.
    /// </summary>
    public sealed class TestAuthFactory : WebApplicationFactory<McpHttp::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureServices(services =>
            {
                // Replace whatever authentication the sample registered with a
                // single always-on test scheme that issues a Confidential
                // principal, so RequireAuthorization() passes deterministically.
                services.AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
                services.AddAuthorization(options =>
                {
                    options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(
                        TestAuthHandler.SchemeName).RequireAuthenticatedUser().Build();
                });
            });
        }
    }

    /// <summary>Always-authenticated test handler — issues a fixed Confidential principal.</summary>
    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "Test";

        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, "test-user"),
                new Claim("clearance", "Confidential"),
            };
            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
