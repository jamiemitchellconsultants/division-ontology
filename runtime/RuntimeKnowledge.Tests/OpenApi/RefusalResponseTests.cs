using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using RuntimeKnowledge.Api;
using Xunit;

namespace RuntimeKnowledge.Tests.OpenApi;

[Trait("Category", "OpenApi")]
public sealed class RefusalResponseTests
{
    static async Task<(HttpResponseMessage Response, JsonObject? Body)> Send(HttpClient client, HttpMethod method, string path,
        HttpContent? content = null)
    {
        var response = await client.SendAsync(new HttpRequestMessage(method, path) { Content = content });
        var text = await response.Content.ReadAsStringAsync();
        return (response, text.Length == 0 ? null : JsonNode.Parse(text)!.AsObject());
    }

    static void AssertRefusal(HttpResponseMessage response, JsonObject? body, int status)
    {
        var refusal = ProblemResponses.Refusals[status];
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal((status, refusal.Title, refusal.Code), ((int)body!["status"]!, (string)body["title"]!, (string)body["code"]!));
    }

    static StringContent Json(string text) => new(text, Encoding.UTF8, "application/json");

    [Fact]
    public void RefusalsAreExactlyTheB22Set()
    {
        Assert.Equal([(400, "unreadableRequest"), (401, "authenticationRequired"), (403, "roleRequired")],
            ProblemResponses.Refusals.OrderBy(r => r.Key).Select(r => (r.Key, r.Value.Code)));
        Assert.Equal("The request body or parameters could not be read.", ProblemResponses.Refusals[400].Title);
        Assert.Equal("Sign in to use the workspace API.", ProblemResponses.Refusals[401].Title);
        Assert.Equal("Your account does not have the role this operation needs.", ProblemResponses.Refusals[403].Title);
    }

    [Fact]
    public async Task EveryTokenFailureGetsTheSame401()
    {
        await using var host = OpenApiHost.Start();
        var garbage = host.CreateClient(new());
        garbage.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt");
        var bodies = new List<string>();
        foreach (var client in new[] { host.Client, garbage,
                     host.ClientWithToken(["ReadKnowledgeGraph"], expired: true), host.ClientWithToken(["ReadKnowledgeGraph"], wrongSignature: true) })
        {
            var (response, body) = await Send(client, HttpMethod.Get, "/api/inventory");
            AssertRefusal(response, body, 401);
            Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
            body!.Remove("traceId");
            bodies.Add(body.ToJsonString());
        }
        Assert.Single(bodies.Distinct());
    }

    [Theory]
    [InlineData("GET", "/api/inventory", new string[0])]
    [InlineData("POST", "/api/contributions/annotations", new[] { "ReadKnowledgeGraph" })]
    public async Task AMissingRoleGetsRoleRequired(string method, string path, string[] roles)
    {
        await using var host = OpenApiHost.Start();
        var (response, body) = await Send(host.ClientWithToken(roles), new HttpMethod(method), path,
            method == "POST" ? Json("{}") : null);
        AssertRefusal(response, body, 403);
    }

    [Theory]
    [InlineData("POST", "/api/search", "{")]
    [InlineData("GET", "/api/inventory?limit=many", null)]
    public async Task AnUnreadableRequestGetsUnreadableRequest(string method, string path, string? body)
    {
        await using var host = OpenApiHost.Start();
        var (response, problem) = await Send(host.ClientWithToken(["ReadKnowledgeGraph"]), new HttpMethod(method), path,
            body is null ? null : Json(body));
        AssertRefusal(response, problem, 400);
    }

    [Theory]
    [InlineData("POST", "/api/search", "text/plain", 415)]
    [InlineData("GET", "/api/search", null, 405)]
    [InlineData("GET", "/api/no-such-operation", null, 404)]
    public async Task StatusesGivenBeforeAnOperationRunsStayEmpty(string method, string path, string? mediaType, int status)
    {
        await using var host = OpenApiHost.Start();
        var (response, body) = await Send(host.Client, new HttpMethod(method), path,
            mediaType is null ? null : new StringContent("x", Encoding.UTF8, mediaType));
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Null(body);
    }

    [Fact]
    public async Task AResponseThatAlreadyHasABodyIsLeftAlone()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.UseRefusalResponses();
        app.MapGet("/own", () => Results.Problem(statusCode: 403, title: "Own title"));
        app.MapGet("/bare", () => Results.StatusCode(403));
        await app.StartAsync();
        var client = app.GetTestClient();
        Assert.Equal("Own title", (string)JsonNode.Parse(await (await client.GetAsync("/own")).Content.ReadAsStringAsync())!["title"]!);
        Assert.Equal("roleRequired", (string)JsonNode.Parse(await (await client.GetAsync("/bare")).Content.ReadAsStringAsync())!["code"]!);
    }
}
