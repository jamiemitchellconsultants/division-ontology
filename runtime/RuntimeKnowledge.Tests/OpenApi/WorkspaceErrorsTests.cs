using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using RuntimeKnowledge.Api;
using Xunit;

namespace RuntimeKnowledge.Tests.OpenApi;

[Trait("Category", "OpenApi")]
public sealed class WorkspaceErrorsTests
{
    [Fact]
    public void DeclaringCodesAddsOneProblemResponsePerStatus()
    {
        var app = WebApplication.CreateSlimBuilder().Build();
        app.MapGet("/x", () => "x").ProducesWorkspaceErrors("staleCursor", "entityNotFound", "versionConflict");
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints).Single();
        Assert.Equal(["staleCursor", "entityNotFound", "versionConflict"], endpoint.Metadata.GetRequiredMetadata<WorkspaceErrorCodes>().Codes);
        var problems = endpoint.Metadata.OfType<IProducesResponseTypeMetadata>().Where(m => m.ContentTypes.Contains("application/problem+json"));
        Assert.Equal([404, 409], problems.Select(m => m.StatusCode).Order());
    }

    [Fact]
    public void ConflictsUseTheSchemaWithCurrent()
    {
        Assert.Equal("WorkspaceConflictProblem", WorkspaceOpenApi.ProblemSchemaFor(["versionConflict", "staleCursor"]));
        Assert.Equal("WorkspaceProblem", WorkspaceOpenApi.ProblemSchemaFor(["staleCursor"]));
    }

    [Fact]
    public async Task InventoryDescribesItsCodesAsProblemDetails()
    {
        await using var host = OpenApiHost.Start();
        var spec = await host.SpecAsync();
        var responses = spec["paths"]!["/api/inventory"]!["get"]!["responses"]!;
        foreach (var (status, codes) in new[] { ("409", "[\"staleCursor\"]"), ("422", "[\"invalidCursor\",\"invalidLimit\"]"), ("503", "[\"snapshotUnavailable\"]") })
        {
            Assert.Equal(codes, responses[status]!["x-workspace-codes"]!.ToJsonString());
            Assert.Equal("#/components/schemas/WorkspaceProblem",
                (string)responses[status]!["content"]!["application/problem+json"]!["schema"]!["$ref"]!);
        }
        Assert.Equal("#/components/responses/unreadableRequest", (string)responses["400"]!["$ref"]!);
        var unreadable = spec["components"]!["responses"]!["unreadableRequest"]!;
        Assert.Equal("[\"unreadableRequest\"]", unreadable["x-workspace-codes"]!.ToJsonString());
        Assert.Equal("#/components/schemas/WorkspaceProblem",
            (string)unreadable["content"]!["application/problem+json"]!["schema"]!["$ref"]!);
        var conflict = spec["components"]!["schemas"]!["WorkspaceConflictProblem"]!;
        Assert.Equal("#/components/schemas/RuntimeContribution", (string)conflict["properties"]!["current"]!["$ref"]!);
        Assert.Contains("code", conflict["required"]!.AsArray().Select(n => (string)n!));
    }

    [Fact]
    public async Task EveryReferenceResolvesAndNoFrameworkProblemSchemaRemains()
    {
        await using var host = OpenApiHost.Start();
        var spec = await host.SpecAsync();
        Assert.Null(spec["components"]!["schemas"]!["ProblemDetails"]);
        var text = spec.ToJsonString();
        var references = System.Text.RegularExpressions.Regex.Matches(text, "\"#/components/(\\w+)/(\\w+)\"");
        Assert.True(references.Count > 20, $"Found only {references.Count} references.");
        foreach (System.Text.RegularExpressions.Match reference in references)
            Assert.True(spec["components"]![reference.Groups[1].Value]?[reference.Groups[2].Value] is not null, $"Unresolved {reference.Value}");
    }

    [Theory]
    [InlineData("unreadableRequest")] [InlineData("authenticationRequired")] [InlineData("roleRequired")]
    public void RefusalCodesCannotBeDeclared(string code)
    {
        var app = WebApplication.CreateSlimBuilder().Build();
        Assert.Throws<ArgumentException>(() => app.MapGet("/x", () => "x").ProducesWorkspaceErrors(code));
    }
}
