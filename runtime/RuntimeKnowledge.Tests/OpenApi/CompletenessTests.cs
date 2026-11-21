using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using RuntimeKnowledge.Api;
using Xunit;

namespace RuntimeKnowledge.Tests.OpenApi;

[Trait("Category", "OpenApi")]
public sealed class CompletenessTests
{
    static RouteEndpoint Endpoint(string method, string pattern, params object[] metadata) =>
        new(_ => Task.CompletedTask, RoutePatternFactory.Parse(pattern), 0,
            new EndpointMetadataCollection([new HttpMethodMetadata([method]), .. metadata]), pattern);

    static JsonNode Spec(string json) => JsonNode.Parse(json)!;

    const string Complete = """
        {"paths":{"/api/x/{id}":{"get":{"summary":"X","description":"X in detail.","operationId":"getX","x-workspace-role":"ReadKnowledgeGraph",
          "x-agent-tool-name":"workspace_get_x","x-agent-idempotent":true,"x-agent-retryable":true,"x-agent-side-effects":"none",
          "x-agent-input":"parameters","x-agent-output":"X",
          "parameters":[{"name":"id","in":"path"}],
          "responses":{"200":{"content":{"application/json":{"schema":{"$ref":"#/components/schemas/X"}}},
            "links":{"self":{"operationId":"getX"}}},
            "400":{},"401":{},"403":{},"404":{},"500":{}}}}}}
        """;

    static readonly object[] Protected = [new AuthorizeAttribute(Identity.ReadPolicy), new WorkspaceErrorCodes(["entityNotFound"])];

    [Fact]
    public void ACompleteOperationHasNoGaps() =>
        Assert.Empty(SpecCompleteness.Find([Endpoint("GET", "/api/x/{id:guid}", Protected)], Spec(Complete)));

    [Fact]
    public void AnUnmappedSpecOperationAndAMissingOperationAreNamed()
    {
        var gaps = SpecCompleteness.Find([Endpoint("POST", "/api/y", Protected)], Spec(Complete));
        Assert.Equal(["GET /api/x/{id}: in the spec but not mapped", "POST /api/y: not in the spec"], gaps);
    }

    [Fact]
    public void MissingSummaryRoleAndDeclaredStatusAreNamed()
    {
        var spec = Spec(Complete);
        var op = spec["paths"]!["/api/x/{id}"]!["get"]!.AsObject();
        op.Remove("summary");
        op.Remove("x-workspace-role");
        op["responses"]!.AsObject().Remove("404");
        Assert.Equal(["GET /api/x/{id}: no 404 response for entityNotFound", "GET /api/x/{id}: no summary", "GET /api/x/{id}: no x-workspace-role"],
            SpecCompleteness.Find([Endpoint("GET", "/api/x/{id:guid}", Protected)], spec));
    }

    [Fact]
    public void MissingAuthResponsesAreNamedForProtectedRoutes()
    {
        var spec = Spec(Complete);
        spec["paths"]!["/api/x/{id}"]!["get"]!["responses"]!.AsObject().Remove("403");
        Assert.Equal(["GET /api/x/{id}: no 403 response"], SpecCompleteness.Find([Endpoint("GET", "/api/x/{id:guid}", Protected)], spec));
    }

    [Fact]
    public void Missing400And500AndSuccessSchemaAreNamed()
    {
        var spec = Spec(Complete);
        var responses = spec["paths"]!["/api/x/{id}"]!["get"]!["responses"]!.AsObject();
        responses.Remove("400");
        responses.Remove("500");
        responses["200"] = new JsonObject();
        Assert.Equal(["GET /api/x/{id}: no 400 response", "GET /api/x/{id}: no 500 response", "GET /api/x/{id}: no success links", "GET /api/x/{id}: no success response schema"],
            SpecCompleteness.Find([Endpoint("GET", "/api/x/{id:guid}", Protected)], spec));
    }

    [Theory]
    [InlineData("/api/contributions/{id:guid}", "/api/contributions/{id}")]
    [InlineData("api/x/{page=1}/{name?}", "/api/x/{page}/{name}")]
    [InlineData("/files/{**rest}", "/files/{rest}")]
    public void RoutePatternsAreNormalised(string pattern, string expected) =>
        Assert.Equal(expected, SpecCompleteness.Normalise(pattern));

    [Fact]
    public async Task TheRunningApiIsFullyDescribed()
    {
        await using var host = OpenApiHost.Start();
        Assert.Empty(SpecCompleteness.Find(host.Endpoints(), await host.SpecAsync()));
    }

    [Fact]
    public async Task TheCommittedSpecDescribesEveryMappedRoute()
    {
        await using var host = OpenApiHost.Start();
        Assert.Empty(SpecCompleteness.Find(host.Endpoints(), OpenApiHost.CommittedSpec()));
    }
}
