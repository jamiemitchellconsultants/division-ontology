using System.Text.Json.Nodes;
using RuntimeKnowledge.Api;
using Xunit;

namespace RuntimeKnowledge.Tests.OpenApi;

[Trait("Category", "OpenApi")]
public sealed class DocumentDescriptionTests
{
    static async Task<JsonNode> Spec()
    {
        await using var host = OpenApiHost.Start();
        return await host.SpecAsync();
    }

    static IEnumerable<(string Path, string Method, JsonNode Op)> Operations(JsonNode spec) =>
        spec["paths"]!.AsObject().SelectMany(p => p.Value!.AsObject().Select(m => (p.Key, m.Key, m.Value!)));

    [Fact]
    public async Task InfoIsFixed()
    {
        var info = (await Spec())["info"]!;
        Assert.Equal("Runtime knowledge workspace API", (string)info["title"]!);
        Assert.Equal("v1", (string)info["version"]!);
        Assert.Equal(3, info.AsObject().Count);
        Assert.Contains("x-agent-tool-name", (string)info["description"]!);
        Assert.Contains("links", (string)info["description"]!);
    }

    [Fact]
    public async Task ProtectedOperationsNeedABearerTokenAndNameTheirRole()
    {
        var operations = Operations(await Spec()).Where(o => o.Path.StartsWith("/api/")).ToArray();
        Assert.NotEmpty(operations);
        foreach (var (path, method, op) in operations)
        {
            var manages = path.StartsWith("/api/contributions/") ||
                (method, path) is ("post", "/api/assessments") or ("put", "/api/assessments/{id}");
            var expected = manages ? "ManageKnowledgeGraph" : "ReadKnowledgeGraph";
            Assert.Equal(expected, (string)op["x-workspace-role"]!);
            Assert.Equal("""[{"bearer":[]}]""", op["security"]!.ToJsonString());
            Assert.Equal("#/components/responses/unauthorized", (string)op["responses"]!["401"]!["$ref"]!);
            Assert.Equal("#/components/responses/forbidden", (string)op["responses"]!["403"]!["$ref"]!);
        }
    }

    [Fact]
    public async Task HealthIsAnonymous()
    {
        var health = (await Spec())["paths"]!["/health"]!["get"]!;
        Assert.Equal("none", (string)health["x-workspace-role"]!);
        Assert.Equal("[]", health["security"]!.ToJsonString());
        Assert.Null(health["responses"]!["401"]);
    }

    [Fact]
    public async Task EveryOperationDescribesTheGeneric500Once()
    {
        var spec = await Spec();
        Assert.All(Operations(spec), o => Assert.Equal("#/components/responses/internalError", (string)o.Op["responses"]!["500"]!["$ref"]!));
        Assert.Equal("#/components/schemas/Problem",
            (string)spec["components"]!["responses"]!["internalError"]!["content"]!["application/problem+json"]!["schema"]!["$ref"]!);
    }

    [Theory]
    [InlineData("unauthorized", 401)]
    [InlineData("forbidden", 403)]
    public async Task RefusalsAreWorkspaceProblemsWithTheirCode(string name, int status)
    {
        var spec = await Spec();
        var response = spec["components"]!["responses"]![name]!;
        var refusal = ProblemResponses.Refusals[status];
        Assert.Equal($"{refusal.Title} Code: {refusal.Code}", (string)response["description"]!);
        Assert.Equal($"[\"{refusal.Code}\"]", response["x-workspace-codes"]!.ToJsonString());
        Assert.Equal("#/components/schemas/WorkspaceProblem",
            (string)response["content"]!["application/problem+json"]!["schema"]!["$ref"]!);
        Assert.Equal(["code", "status", "title"],
            spec["components"]!["schemas"]!["WorkspaceProblem"]!["required"]!.AsArray().Select(n => (string)n!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task UnauthorizedNamesTheChallengeHeader() =>
        Assert.NotNull((await Spec())["components"]!["responses"]!["unauthorized"]!["headers"]!["WWW-Authenticate"]);

    [Fact]
    public async Task AServed401BodyFitsTheDescribedSchema()
    {
        await using var host = OpenApiHost.Start();
        var schema = (await host.SpecAsync())["components"]!["schemas"]!["WorkspaceProblem"]!;
        var body = JsonNode.Parse(await (await host.Client.GetAsync("/api/session")).Content.ReadAsStringAsync())!.AsObject();
        var properties = schema["properties"]!.AsObject().Select(p => p.Key).ToHashSet();
        Assert.All(body.Select(p => p.Key), key => Assert.Contains(key, properties));
        Assert.All(schema["required"]!.AsArray(), key => Assert.True(body.ContainsKey((string)key!)));
    }

    [Fact]
    public async Task BearerSchemeIsAJwt()
    {
        var bearer = (await Spec())["components"]!["securitySchemes"]!["bearer"]!;
        Assert.Equal(("http", "bearer", "JWT"), ((string)bearer["type"]!, (string)bearer["scheme"]!, (string)bearer["bearerFormat"]!));
    }

    [Fact]
    public async Task KeysAreInOrdinalOrder()
    {
        var spec = await Spec();
        AssertOrdered(spec["paths"]!);
        AssertOrdered(spec["components"]!["schemas"]!);
        AssertOrdered(spec["components"]!["responses"]!);
        foreach (var (_, _, op) in Operations(spec)) AssertOrdered(op["responses"]!);

        static void AssertOrdered(JsonNode node)
        {
            var keys = node.AsObject().Select(p => p.Key).ToArray();
            Assert.Equal(keys.Order(StringComparer.Ordinal), keys);
        }
    }
}
