using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Xunit;

namespace RuntimeKnowledge.Tests.OpenApi;

[Trait("Category", "OpenApi")]
public sealed class DocsSignInTests
{
    static readonly Dictionary<string, string?> Docs = new() { ["Workspace:Docs:ClientId"] = "workspace-spa", ["Workspace:Docs:Scopes"] = "openid" };
    static JsonNode? SignIn(JsonNode spec) => spec["components"]!["securitySchemes"]!["workspaceSignIn"];

    [Fact]
    public async Task AuthorizationCodeEndpointsComeFromDiscovery()
    {
        await using var host = OpenApiHost.Start(Docs);
        var flow = SignIn(await host.SpecAsync())!["flows"]!["authorizationCode"]!;
        Assert.Equal(OpenApiHost.Discovery.AuthorizationEndpoint, (string)flow["authorizationUrl"]!);
        Assert.Equal(OpenApiHost.Discovery.TokenEndpoint, (string)flow["tokenUrl"]!);
        Assert.Equal(["openid"], flow["scopes"]!.AsObject().Select(p => p.Key));
    }

    [Fact]
    public async Task SignInIsAnAlternativeOnProtectedOperationsOnly()
    {
        await using var host = OpenApiHost.Start(Docs);
        var spec = await host.SpecAsync();
        foreach (var path in spec["paths"]!.AsObject().Where(p => p.Key.StartsWith("/api/")))
            foreach (var op in path.Value!.AsObject())
                Assert.Equal("""[{"bearer":[]},{"workspaceSignIn":["openid"]}]""", op.Value!["security"]!.ToJsonString());
        Assert.Equal("[]", spec["paths"]!["/health"]!["get"]!["security"]!.ToJsonString());
    }

    [Fact]
    public async Task NoDocsClientMeansNoSignIn()
    {
        await using var host = OpenApiHost.Start();
        Assert.Null(SignIn(await host.SpecAsync()));
    }

    public static TheoryData<string> Failing => ["hangs", "throws", "no endpoints"];

    [Theory, MemberData(nameof(Failing))]
    public async Task ASlowOrBrokenProviderStillServesTheSpecWithoutSignIn(string mode)
    {
        IConfigurationManager<OpenIdConnectConfiguration> discovery = mode switch
        {
            "hangs" => new FakeDiscovery(() => new TaskCompletionSource<OpenIdConnectConfiguration>().Task),
            "throws" => new FakeDiscovery(() => Task.FromException<OpenIdConnectConfiguration>(new HttpRequestException("unreachable"))),
            _ => new StaticConfigurationManager<OpenIdConnectConfiguration>(new OpenIdConnectConfiguration { Issuer = OpenApiHost.Issuer })
        };
        await using var host = OpenApiHost.Start(Docs, discovery);
        var timer = Stopwatch.StartNew();
        var response = await host.Client.GetAsync("/api/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5), $"Took {timer.Elapsed}.");
        var spec = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Null(SignIn(spec));
        Assert.Null(spec["servers"]);
    }

    sealed class FakeDiscovery(Func<Task<OpenIdConnectConfiguration>> get) : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel) => get();
        public void RequestRefresh() { }
    }
}
