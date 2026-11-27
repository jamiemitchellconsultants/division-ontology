using System.Text;
using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact;
using RuntimeKnowledge.Impact.Bundle;
using RuntimeKnowledge.Impact.Model;
using Xunit;

namespace RuntimeKnowledge.Tests.Impact;

public sealed class ImpactModelTests
{
    private static ImpactBundle Pilot() => ImpactBundle.Open(CorpusPaths.Objects, CorpusPaths.FirstManifest("pilot", "search"));

    [Fact]
    public void Pilot_bundle_builds_a_model_in_loader_order()
    {
        var model = ImpactModel.Build(Pilot());
        Assert.Equal(["fixture/Alpha", "fixture/Beta"], model.Sources.Select(s => s.Id));
        var alpha = model.Source("fixture/Alpha")!;
        Assert.Equal("available", alpha.Status);
        Assert.Equal(5, alpha.Nodes.Count);
        Assert.Equal(4, alpha.Assertions.Count);
        Assert.NotNull(alpha.EvidenceNode("https://alpha.example/#evidence"));
        Assert.Single(alpha.AssertionsFrom("https://alpha.example/#container"));
        Assert.Equal(4, model.Registry!.Mappings.Count);
        Assert.NotNull(model.Registry.BindingFrom("fixture/Alpha", "https://alpha.example/#external"));
    }

    [Fact]
    public void Evidence_bytes_are_read_by_role_source_and_path() =>
        Assert.Equal("export const api = 1;\n",
            Encoding.UTF8.GetString(Pilot().Read("graphEvidence", "fixture/Alpha", "src/app.ts")!));

    [Fact]
    public void A_wrong_manifest_digest_is_refused()
    {
        var id = CorpusPaths.FirstManifest("pilot", "search");
        var bytes = File.ReadAllBytes(Path.Combine(CorpusPaths.Objects, "manifests", id + ".json"));
        var error = Assert.Throws<ImpactException>(() => ImpactBundle.FromObjects(new string('0', 64), bytes, _ => null));
        Assert.StartsWith("invalid impact bundle", error.Message);
    }

    [Fact]
    public void A_tampered_blob_is_refused()
    {
        var id = CorpusPaths.FirstManifest("pilot", "search");
        var bytes = File.ReadAllBytes(Path.Combine(CorpusPaths.Objects, "manifests", id + ".json"));
        var bundle = ImpactBundle.FromObjects(id, bytes, _ => "tampered"u8.ToArray());
        Assert.Throws<ImpactException>(() => ImpactModel.Build(bundle));
    }

    [Fact]
    public void Glob_matches_as_the_pilot_does()
    {
        Assert.True(Glob.Matches("src/**", "src/a/b.ts"));
        Assert.True(Glob.Matches("**/*.tf", "infra/main.tf"));
        Assert.False(Glob.Matches("src/*", "src/a/b.ts"));
        Assert.Throws<ImpactException>(() => Glob.Matches("../x", "x"));
    }

    [Fact]
    public void Test_model_matches_makeModel() =>
        Assert.Equal(6, TestModels.MakeModel().Registry!.Mappings.Count);
}
