using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using RuntimeKnowledge.Api;

namespace RuntimeKnowledge.Tests.OpenApi;

/// <summary>A Docker-free API host for the OpenApi test category. Nothing it serves reaches the database.</summary>
internal sealed class OpenApiHost : IAsyncDisposable
{
    public const string Issuer = "https://issuer.example.test/realms/workspace";
    public const string Audience = "workspace-api";
    public const string ClientId = "workspace-api";

    public static readonly OpenIdConnectConfiguration Discovery = new()
    {
        Issuer = Issuer,
        AuthorizationEndpoint = Issuer + "/protocol/openid-connect/auth",
        TokenEndpoint = Issuer + "/protocol/openid-connect/token"
    };

    private readonly WebApplicationFactory<ApiProgram> _factory;
    private HttpClient? _client;

    private OpenApiHost(WebApplicationFactory<ApiProgram> factory) => _factory = factory;

    public HttpClient Client => _client ??= _factory.CreateClient();

    public static OpenApiHost Start(IReadOnlyDictionary<string, string?>? settings = null,
        IConfigurationManager<OpenIdConnectConfiguration>? discovery = null)
    {
        var merged = new Dictionary<string, string?>
        {
            ["Workspace:Identity:Provider"] = "Keycloak",
            ["Workspace:Identity:Issuer"] = Issuer,
            ["Workspace:Identity:Audience"] = Audience,
            ["Workspace:Identity:ClientId"] = ClientId
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>()) merged[key] = value;
        var manager = discovery ?? new StaticConfigurationManager<OpenIdConnectConfiguration>(Discovery);
        var factory = new WebApplicationFactory<ApiProgram>().WithWebHostBuilder(builder =>
        {
            // Every deployment runs as Production. Development would turn binding failures into 500s.
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(merged));
            builder.ConfigureTestServices(services =>
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                    options => options.ConfigurationManager = manager));
        });
        return new OpenApiHost(factory);
    }

    public HttpClient CreateClient(WebApplicationFactoryClientOptions options) => _factory.CreateClient(options);

    public async Task<JsonNode> SpecAsync() =>
        JsonNode.Parse(await Client.GetStringAsync("/api/openapi/v1.json"))!;

    public IReadOnlyList<RouteEndpoint> Endpoints()
    {
        _ = Client; // starts the host
        return _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToArray();
    }

    public static JsonNode CommittedSpec() => JsonNode.Parse(File.ReadAllText(RepositoryPaths.Spec))!;

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        await _factory.DisposeAsync();
    }
}
