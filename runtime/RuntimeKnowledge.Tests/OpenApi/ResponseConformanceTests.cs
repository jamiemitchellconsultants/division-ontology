using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace RuntimeKnowledge.Tests.OpenApi;

[Trait("Category", "OpenApi")]
public sealed class ResponseConformanceTests
{
    static readonly JsonNode Spec = JsonNode.Parse("""{"paths":{"/api/search":{"post":{"responses":{"200":{},"401":{},"503":{}}}}}}""")!;

    [Fact]
    public void DeclaredStatusesPass() =>
        Assert.Empty(ResponseConformance.Undeclared([new("POST", "/api/search", 200), new("POST", "/api/search", 503)], Spec));

    [Fact]
    public void AnUndeclaredStatusIsNamed() =>
        Assert.Equal(["POST /api/search 422"], ResponseConformance.Undeclared([new("POST", "/api/search", 422), new("POST", "/api/search", 422)], Spec));

    [Fact]
    public void AnUnknownRouteIsNamed() =>
        Assert.Equal(["GET /api/other: not in the spec"], ResponseConformance.Undeclared([new("GET", "/api/other", 200)], Spec));

    [Fact]
    public void TransportStatusesAreNotOperationStatuses() =>
        Assert.Empty(ResponseConformance.Undeclared([new("POST", "/api/search", 405), new("POST", "/api/search", 413), new("POST", "/api/search", 415)], Spec));

    [Fact]
    public async Task TheRecorderSeesRefusalsWrittenBeforeTheHandler()
    {
        var observed = new ConcurrentBag<ObservedResponse>();
        await using var host = OpenApiHost.Start(services: s => s.AddSingleton<IStartupFilter>(new ResponseConformance.Recorder(observed)));
        // An anonymous request to a protected route: authentication writes the 401 and the refusal hook its body.
        await host.Client.GetAsync("/api/inventory?limit=0");
        await host.Client.GetAsync("/api/no-such-operation");
        Assert.Contains(new ObservedResponse("GET", "/api/inventory", 401), observed);
        Assert.DoesNotContain(observed, o => o.Status == 404); // no endpoint was selected
    }
}
