using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
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
using Microsoft.IdentityModel.Tokens;
using RuntimeKnowledge.Api;

namespace RuntimeKnowledge.Tests.OpenApi;

/// <summary>A Docker-free API host for the OpenApi test category. Nothing it serves reaches the database.</summary>
internal sealed class OpenApiHost : IAsyncDisposable
{
    public const string Issuer = "https://issuer.example.test/realms/workspace";
    public const string Audience = "workspace-api";
    public const string ClientId = "workspace-api";

    private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "openapi-host" };

    public static readonly OpenIdConnectConfiguration Discovery = CreateDiscovery();

    private static OpenIdConnectConfiguration CreateDiscovery()
    {
        var discovery = new OpenIdConnectConfiguration
        {
            Issuer = Issuer,
            AuthorizationEndpoint = Issuer + "/protocol/openid-connect/auth",
            TokenEndpoint = Issuer + "/protocol/openid-connect/token"
        };
        discovery.SigningKeys.Add(SigningKey);
        return discovery;
    }

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

    /// <summary>A client with a Keycloak-shaped token for this host's issuer. Requests it sends must not reach the store.</summary>
    public HttpClient ClientWithToken(string[] roles, bool expired = false, bool wrongSignature = false)
    {
        var resources = new Dictionary<string, object> { [ClientId] = new { roles } };
        Claim[] claims = [new("sub", "openapi-user"), new("resource_access", JsonSerializer.Serialize(resources), JsonClaimValueTypes.Json)];
        using var wrong = wrongSignature ? RSA.Create(2048) : null;
        var key = wrong is null ? SigningKey : new RsaSecurityKey(wrong) { KeyId = SigningKey.KeyId };
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(Issuer, Audience, claims, now.AddHours(-2), expired ? now.AddHours(-1) : now.AddHours(1),
            new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }

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
