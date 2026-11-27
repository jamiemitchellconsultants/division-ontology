using RuntimeKnowledge.Impact.Bundle;
using RuntimeKnowledge.Impact.Engine;
using RuntimeKnowledge.Impact.Model;
using Xunit;

namespace RuntimeKnowledge.Tests.Impact;

public sealed class StartsTests
{
    private const string P = "https://example.test/policies/graph#";
    private static ImpactModel Confluence() =>
        ImpactModel.Build(ImpactBundle.Open(CorpusPaths.Objects, CorpusPaths.FirstManifest("confluence", "paths")));

    [Fact]
    public void A_supported_unmapped_start_produces_paths_without_an_unsupported_start_gap()
    {
        var r = Paths.Read(Confluence(), [("fixture/policies", P + "expense-policy")]);
        Assert.NotEmpty(r["paths"]!.AsArray());
        Assert.DoesNotContain(r["gaps"]!.AsArray(), g => g!["code"]!.GetValue<string>() == "unsupported-start");
        Assert.Equal(P + "approval", r["paths"]![0]!["steps"]![0]!["object"]!["nodeIri"]!.GetValue<string>());
    }

    [Fact]
    public void An_unsupported_start_is_skipped_with_the_new_reason()
    {
        var r = Paths.Read(Confluence(), [("fixture/policies", P + "orphan")]);
        var gap = Assert.Single(r["gaps"]!.AsArray(), g => g!["code"]!.GetValue<string>() == "unsupported-start");
        Assert.Equal("Start lacks verified evidence or a supported mapping", gap!["reason"]!.GetValue<string>());
        Assert.Equal(["code", "sourceId", "nodeIri", "reason"], gap.AsObject().Select(p => p.Key));
    }
}
