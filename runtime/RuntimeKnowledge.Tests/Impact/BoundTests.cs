using System.Security.Cryptography;
using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact.Bundle;
using RuntimeKnowledge.Impact.Engine;
using RuntimeKnowledge.Impact.Model;
using Xunit;

namespace RuntimeKnowledge.Tests.Impact;

/// <summary>B40: a trace returns at most 500 paths, and says when more exist.</summary>
public sealed class BoundTests
{
    private static readonly Lazy<ImpactModel> Large = new(() =>
    {
        var (manifest, blobs) = LargeModel.Bundle();
        return ImpactModel.Build(ImpactBundle.FromObjects(Convert.ToHexStringLower(SHA256.HashData(manifest)), manifest, d => blobs.GetValueOrDefault(d)));
    });

    private static readonly (string, string)[] Start = [(LargeModel.SourceId, LargeModel.Base + "n00042")];

    private static IEnumerable<string> Codes(JsonObject result) => result["gaps"]!.AsArray().Select(g => g!["code"]!.GetValue<string>());

    [Fact]
    public void A_trace_that_finds_more_than_500_paths_returns_the_first_500_and_says_so()
    {
        var bounded = Paths.Read(Large.Value, Start, 6);
        Assert.Equal(500, bounded["paths"]!.AsArray().Count);
        var gap = Assert.Single(bounded["gaps"]!.AsArray(), g => g!["code"]!.GetValue<string>() == "paths-truncated")!;
        Assert.Equal(500, gap["limit"]!.GetValue<int>());
        Assert.Contains("fewer starts or a lower depth", gap["reason"]!.GetValue<string>());

        // The bound is deterministic: the same trace returns the same 500, in the same order.
        Assert.Equal(Paths.Read(Large.Value, Start, 6)["paths"]!.AsArray().Select(p => p!["id"]!.GetValue<string>()),
                     bounded["paths"]!.AsArray().Select(p => p!["id"]!.GetValue<string>()));
    }

    [Fact]
    public void A_trace_of_500_or_fewer_paths_reports_no_truncation()
    {
        var small = Paths.Read(Large.Value, Start, 2);
        Assert.InRange(small["paths"]!.AsArray().Count, 1, 500);
        Assert.DoesNotContain("paths-truncated", Codes(small));
    }
}
