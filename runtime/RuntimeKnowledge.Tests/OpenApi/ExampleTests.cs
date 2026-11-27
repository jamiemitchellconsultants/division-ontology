using System.Text.Json;
using System.Text.Json.Nodes;
using RuntimeKnowledge.Api;
using RuntimeKnowledge.Contracts;
using Xunit;

namespace RuntimeKnowledge.Tests.OpenApi;

[Trait("Category", "OpenApi")]
public sealed class ExampleTests
{
    [Fact]
    public void EveryExampleIsAnInventedValue()
    {
        var spec = OpenApiHost.CommittedSpec();
        var allowed = OpenApiExamples.All.Select(v => JsonSerializer.SerializeToNode(v, WorkspaceJson.Options)).ToArray();
        var examples = Descendants(spec).Where(p => p.Key is "example").Select(p => p.Value)
            .Concat(Descendants(spec).Where(p => p.Key is "examples")
                .SelectMany(p => p.Value!.AsObject().Select(entry => entry.Value!["value"])))
            .ToArray();
        Assert.Equal(9, examples.Length);
        Assert.All(examples, e => Assert.Contains(allowed, a => JsonNode.DeepEquals(a, e)));
        foreach (var text in examples.SelectMany(Strings))
            if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                Assert.Equal("example.test", uri.Host);
        Assert.All(examples.SelectMany(e => Descendants(e!)).Where(p => p.Key == "sourceId"),
            p => Assert.StartsWith("example-", (string)p.Value!));
    }

    static IEnumerable<(string Key, JsonNode? Value)> Descendants(JsonNode node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var (key, value) in o)
                {
                    yield return (key, value);
                    if (value is not null) foreach (var inner in Descendants(value)) yield return inner;
                }
                break;
            case JsonArray a:
                foreach (var item in a)
                    if (item is not null) foreach (var inner in Descendants(item)) yield return inner;
                break;
        }
    }

    static IEnumerable<string> Strings(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue<string>(out var s) => [s],
        JsonObject o => o.SelectMany(p => Strings(p.Value)),
        JsonArray a => a.SelectMany(Strings),
        _ => []
    };
}
