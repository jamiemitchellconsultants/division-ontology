using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using RuntimeKnowledge.Api;
using Xunit;

namespace RuntimeKnowledge.Tests.OpenApi;

[Trait("Category", "OpenApi")]
public sealed class CommittedSpecTests
{
    [Fact]
    public void BuildWritesTheCommittedSpec()
    {
        Assert.True(File.Exists(RepositoryPaths.Spec), "Run dotnet build runtime/RuntimeKnowledge.Api to write the spec.");
        var spec = OpenApiHost.CommittedSpec();
        Assert.StartsWith("3.1", (string)spec["openapi"]!);
        foreach (var path in new[] { "/health", "/api/session", "/api/inventory", "/api/contributions/{id}" })
            Assert.NotNull(spec["paths"]![path]);
    }

    [Fact]
    public void CommittedSpecIsDeploymentNeutral()
    {
        var text = File.ReadAllText(RepositoryPaths.Spec);
        var spec = JsonNode.Parse(text)!;
        Assert.Null(spec["servers"]);
        Assert.DoesNotContain("workspaceSignIn", text);
        Assert.DoesNotContain("\"oauth2\"", text);
        foreach (Match url in Regex.Matches(text, @"https?://[^""\s]+"))
            Assert.True(new Uri(url.Value).Host == "example.test" ||
                        url.Value.StartsWith(WorkspaceLinks.RelationBase, StringComparison.Ordinal), url.Value);
    }

    [Fact]
    public async Task ServedContractWithoutDocsSettingsMatchesTheCommittedFile()
    {
        await using var host = OpenApiHost.Start();
        var served = (JsonObject)await host.SpecAsync();
        Assert.True(JsonNode.DeepEquals(OpenApiHost.CommittedSpec(), served), "The build and the running API must generate the same contract.");
    }

    [Fact]
    public void ApiStillRefusesToStartWithoutIdentitySettings()
    {
        using var factory = new WebApplicationFactory<ApiProgram>();
        Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
    }
}
