using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RuntimeKnowledge.Api;
using RuntimeKnowledge.Contracts;
using Xunit;

namespace RuntimeKnowledge.Tests.OpenApi;

[Trait("Category", "OpenApi")]
public sealed class ProblemResponseTests
{
    [Theory]
    [InlineData("entityNotFound", 404)] [InlineData("relationshipNotFound", 404)] [InlineData("contributionNotFound", 404)]
    [InlineData("versionConflict", 409)] [InlineData("staleCursor", 409)]
    [InlineData("snapshotUnavailable", 503)] [InlineData("writesPaused", 503)] [InlineData("searchUnavailable", 503)]
    [InlineData("invalidLimit", 422)] [InlineData("invalidCommand", 422)] [InlineData("invalidBundle", 422)]
    public void StatusForKeepsTheExistingMap(string code, int status) => Assert.Equal(status, ProblemResponses.StatusFor(code));

    [Theory]
    [InlineData("entityNotFound")] [InlineData("versionConflict")] [InlineData("writesPaused")] [InlineData("invalidMeaning")]
    public async Task WriteAsyncUsesTheSameMap(string code)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider(),
            Response = { Body = new MemoryStream() }
        };
        context.Features.Set<IExceptionHandlerFeature>(new ExceptionHandlerFeature { Error = new WorkspaceException(code, "Example.") });
        await ProblemResponses.WriteAsync(context);
        Assert.Equal(ProblemResponses.StatusFor(code), context.Response.StatusCode);
        context.Response.Body.Position = 0;
        Assert.Equal(code, (string)JsonNode.Parse(context.Response.Body)!["code"]!);
    }
}
