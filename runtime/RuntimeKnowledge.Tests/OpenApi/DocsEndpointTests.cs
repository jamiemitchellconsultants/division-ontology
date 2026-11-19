using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace RuntimeKnowledge.Tests.OpenApi;

[Trait("Category", "OpenApi")]
public sealed class DocsEndpointTests
{
    static readonly Dictionary<string, string?> Docs = new() { ["Workspace:Docs:ClientId"] = "workspace-spa", ["Workspace:Docs:Scopes"] = "openid profile" };

    [Fact]
    public async Task DocsRedirectStaysRelative()
    {
        await using var host = OpenApiHost.Start(Docs);
        var response = await host.CreateClient(new() { AllowAutoRedirect = false }).GetAsync("/api/docs");
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("docs/index.html", response.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("/api/docs/index.html", "text/html")]
    [InlineData("/api/docs/oauth2-redirect.html", "text/html")]
    [InlineData("/api/openapi/v1.json", "application/json")]
    public async Task DocsAndSpecNeedNoSignIn(string path, string mediaType)
    {
        await using var host = OpenApiHost.Start(Docs);
        var response = await host.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task UiSignsInWithPkceAndTheWebClient()
    {
        await using var host = OpenApiHost.Start(Docs);
        var script = await host.Client.GetStringAsync("/api/docs/index.js");
        Assert.Contains("\"usePkceWithAuthorizationCodeGrant\":true", script);
        Assert.Contains("\"clientId\":\"workspace-spa\"", script);
        Assert.Contains("\"scopes\":[\"openid\",\"profile\"]", script);
        Assert.Contains("\"url\":\"/api/openapi/v1.json\"", script);
        Assert.DoesNotContain("clientSecret", script);
    }

    [Fact]
    public async Task WithoutDocsSettingsTheUiHasNoSignIn()
    {
        await using var host = OpenApiHost.Start();
        Assert.DoesNotContain("\"clientId\"", await host.Client.GetStringAsync("/api/docs/index.js"));
    }

    [Theory]
    [InlineData("GET", "/api/inventory")]
    [InlineData("GET", "/api/session")]
    [InlineData("POST", "/api/contributions/annotations")]
    public async Task DataStillNeedsAToken(string method, string path)
    {
        await using var host = OpenApiHost.Start(Docs);
        var response = await host.Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("authenticationRequired", (string)JsonNode.Parse(await response.Content.ReadAsStringAsync())!["code"]!);
    }

    [Fact]
    public async Task DocsPathsAreUntouchedByTheRefusalHook()
    {
        await using var host = OpenApiHost.Start(Docs);
        var missing = await host.Client.GetAsync("/api/docs/no-such-asset.js");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Empty(await missing.Content.ReadAsStringAsync());
        Assert.StartsWith("<!", (await host.Client.GetStringAsync("/api/docs/index.html")).TrimStart());
    }
}
