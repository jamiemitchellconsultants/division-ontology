using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using RuntimeKnowledge.Api;
using RuntimeKnowledge.Contracts;

namespace RuntimeKnowledge.Tests;

internal sealed class ApiFixture : IAsyncDisposable
{
    private const string Issuer = "https://issuer.test";
    private const string Audience = "workspace-api";
    private const string ClientId = "workspace-client";
    private readonly Scenario _scenario;
    private readonly WebApplicationFactory<ApiProgram> _factory;
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _signingKey;
    private readonly string _provider;

    private ApiFixture(Scenario scenario, string provider)
    {
        _scenario = scenario;
        _provider = provider;
        _rsa = RSA.Create(2048);
        _signingKey = new RsaSecurityKey(_rsa) { KeyId = "workspace-fixture" };
        var metadata = new OpenIdConnectConfiguration { Issuer = Issuer };
        metadata.SigningKeys.Add(_signingKey);
        _factory = new WebApplicationFactory<ApiProgram>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Workspace"] = scenario.ConnectionString,
                    ["Workspace:Identity:Provider"] = provider,
                    ["Workspace:Identity:Issuer"] = Issuer,
                    ["Workspace:Identity:Audience"] = Audience,
                    ["Workspace:Identity:ClientId"] = ClientId
                }));
            builder.ConfigureTestServices(services =>
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata)));
        });
    }

    public static async Task<ApiFixture> OpenAsync(string provider)
    {
        var scenario = await Scenario.OpenAsync();
        try
        {
            await scenario.ActivateAsync("initial");
            return new ApiFixture(scenario, provider);
        }
        catch
        {
            await scenario.DisposeAsync();
            throw;
        }
    }

    public HttpClient CreateClient() => _factory.CreateClient();

    public HttpClient ClientWithToken(string[] roles, bool expired = false, string? issuer = null,
        string? audience = null, bool wrongSignature = false, string? keycloakClient = null, bool injectCapability = false)
    {
        var claims = new List<Claim> { new("sub", "fixture-user") };
        if (_provider == "Keycloak")
        {
            var resources = new Dictionary<string, object> { [keycloakClient ?? ClientId] = new { roles } };
            claims.Add(new Claim("resource_access", JsonSerializer.Serialize(resources), JsonClaimValueTypes.Json));
        }
        else
        {
            claims.AddRange(roles.Select(role => new Claim("roles", role)));
        }
        if (injectCapability)
        {
            claims.Add(new Claim("runtime-knowledge:capability", "read"));
            claims.Add(new Claim("runtime-knowledge:capability", "manage"));
        }

        using var wrong = wrongSignature ? RSA.Create(2048) : null;
        var key = wrong is null ? _signingKey : new RsaSecurityKey(wrong) { KeyId = _signingKey.KeyId };
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(issuer ?? Issuer, audience ?? Audience, claims,
            now.AddHours(-2), expired ? now.AddHours(-1) : now.AddHours(1),
            new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }

    public Task<HttpResponseMessage> PostAnnotationAsync(HttpClient client) =>
        client.PostAsJsonAsync("/api/contributions/annotations",
            new CreateAnnotation(_scenario.A, "businessExplanation", "Created by verified fixture contributor."));

    public async Task<int> AnnotationCountAsync()
    {
        var detail = await _scenario.Graph.EntityDetailAsync(_scenario.A, CancellationToken.None);
        return detail.Annotations.Count;
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        _rsa.Dispose();
        await _scenario.DisposeAsync();
    }
}
