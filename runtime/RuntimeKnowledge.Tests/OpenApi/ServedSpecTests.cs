using System.Text.Json.Nodes;
using Xunit;

namespace RuntimeKnowledge.Tests.OpenApi;

/// <summary>Removes only what a deployment adds to the served spec: the workspaceSignIn scheme and its requirements.</summary>
internal static class ServedSpec
{
    public static JsonNode StripDeploymentAdditions(JsonNode served)
    {
        var copy = served.DeepClone();
        (copy["components"]?["securitySchemes"] as JsonObject)?.Remove("workspaceSignIn");
        foreach (var (_, item) in copy["paths"]?.AsObject() ?? [])
            foreach (var (_, operation) in item!.AsObject())
                if (operation?["security"] is JsonArray security)
                    foreach (var requirement in security.OfType<JsonObject>()
                                 .Where(r => r.Count == 1 && r.ContainsKey("workspaceSignIn")).ToArray())
                        security.Remove(requirement);
        return copy;
    }
}

[Trait("Category", "OpenApi")]
public sealed class ServedSpecTests
{
    [Fact]
    public async Task ServedSpecIsTheCommittedContractPlusSignInOnly() // B14
    {
        await using var host = OpenApiHost.Start(new Dictionary<string, string?> { ["Workspace:Docs:ClientId"] = "workspace-spa", ["Workspace:Docs:Scopes"] = "openid" });
        var served = await host.SpecAsync();
        Assert.NotNull(served["components"]!["securitySchemes"]!["workspaceSignIn"]);
        Assert.True(JsonNode.DeepEquals(OpenApiHost.CommittedSpec(), ServedSpec.StripDeploymentAdditions(served)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NothingInTheRequestPutsAServerAddressInTheSpec(bool signIn) // B14, B15, ADR 0017
    {
        await using var host = OpenApiHost.Start(signIn ? new Dictionary<string, string?> { ["Workspace:Docs:ClientId"] = "workspace-spa" } : null);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/openapi/v1.json");
        request.Headers.Host = "attacker.example.test";
        request.Headers.Add("X-Forwarded-Host", "attacker.example.test");
        request.Headers.Add("X-Forwarded-Proto", "http");
        var text = await (await host.Client.SendAsync(request)).Content.ReadAsStringAsync();
        Assert.Null(JsonNode.Parse(text)!["servers"]);
        Assert.DoesNotContain("attacker.example.test", text);
    }

    [Fact]
    public async Task ServedSpecRevealsNoSecretOrStorageDetail() // B20
    {
        var secrets = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Workspace"] = "Host=db.private.example.test;Password=fixture-secret-one",
            ["Workspace:Database:SecretArn"] = "arn:aws:secretsmanager:eu-west-2:123456789012:secret:fixture-db",
            ["Workspace:Embeddings:Endpoint"] = "http://embeddings.private.example.test/v1/embeddings",
            ["Workspace:Objects:SecretKey"] = "fixture-secret-two",
            ["Workspace:Docs:ClientId"] = "workspace-spa"
        };
        await using var host = OpenApiHost.Start(secrets);
        var text = (await host.SpecAsync()).ToJsonString();
        foreach (var value in new[] { "fixture-secret-one", "fixture-secret-two", "db.private", "secretsmanager", "embeddings.private" })
            Assert.DoesNotContain(value, text);
    }
}
