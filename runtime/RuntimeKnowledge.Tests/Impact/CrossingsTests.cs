using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact.Engine;
using RuntimeKnowledge.Impact.Model;
using Xunit;

namespace RuntimeKnowledge.Tests.Impact;

public sealed class CrossingsTests
{
    private const string Core = "https://example.test/core#Claim";

    // Two sources, each with one node labelled "Claim" that denotes its own local term.
    private static ImpactModel TwoSources(string? linkA, string? linkB) => TestModels.TwoSourceModel(
        termA: "https://example.test/a/ontology#Claim", termB: "https://example.test/b/ontology#Claim", shared: Core,
        relationA: linkA, relationB: linkB);

    private static IEnumerable<JsonObject> Crossings(JsonObject r) => r["paths"]!.AsArray()
        .SelectMany(p => p!["steps"]!.AsArray()).Select(s => s!.AsObject())
        .Where(s => s["type"]!.GetValue<string>() == "reviewedLinkCrossing");

    // Every path that contains a crossing repeats its step, and NodeA and NodeA2 both denote termA,
    // so these tests look at the distinct crossings made from one node.
    private static IEnumerable<JsonObject> CrossingsFrom(JsonObject r, string nodeIri) => Crossings(r)
        .Where(s => s["from"]!["nodeIri"]!.GetValue<string>() == nodeIri)
        .DistinctBy(s => s.ToJsonString());

    [Fact]
    public void Exact_links_are_crossed_in_both_directions_and_cite_both_links()                 // B11
    {
        var model = TwoSources("exact", "exact");
        var forward = Assert.Single(CrossingsFrom(Paths.Read(model, [("fixture/a", TestModels.NodeA)]), TestModels.NodeA));
        Assert.Equal("fixture/b", forward["to"]!["sourceId"]!.GetValue<string>());
        Assert.Equal(2, forward["links"]!.AsArray().Count);
        Assert.Equal(Core, forward["sharedTerm"]!.GetValue<string>());
        Assert.Matches("^[0-9a-f]{40}$", forward["catalogCommit"]!.GetValue<string>());
        var back = CrossingsFrom(Paths.Read(model, [("fixture/b", TestModels.NodeB)]), TestModels.NodeB);
        Assert.Equal([TestModels.NodeA, TestModels.NodeA2], back.Select(s => s["to"]!["nodeIri"]!.GetValue<string>()));
    }

    [Fact]
    public void Specializes_is_crossed_only_from_general_to_specific()                            // B12
    {
        var model = TwoSources("exact", "specializes");
        Assert.Single(CrossingsFrom(Paths.Read(model, [("fixture/a", TestModels.NodeA)]), TestModels.NodeA));
        var up = Paths.Read(model, [("fixture/b", TestModels.NodeB)]);
        Assert.Empty(Crossings(up));
        var gap = Assert.Single(up["gaps"]!.AsArray(), g => g!["code"]!.GetValue<string>() == "specialization-boundary")!;
        Assert.Equal("https://example.test/b/ontology#Claim", gap["term"]!.GetValue<string>());
        Assert.Equal(Core, gap["sharedTerm"]!.GetValue<string>());
    }

    [Fact]
    public void Identical_labels_without_links_never_cross_and_report_one_gap_per_term()          // B13, U19
    {
        var r = Paths.Read(TwoSources(null, null), [("fixture/a", TestModels.NodeA), ("fixture/a", TestModels.NodeA2)]);
        Assert.Empty(Crossings(r));
        Assert.DoesNotContain(r["paths"]!.AsArray(), p => p!["steps"]!.AsArray().Any(s => s!["to"]?["sourceId"]?.GetValue<string>() == "fixture/b"));
        var gap = Assert.Single(r["gaps"]!.AsArray(), g => g!["code"]!.GetValue<string>() == "unlinked-term" && g["sourceId"]!.GetValue<string>() == "fixture/a")!;
        Assert.Equal([TestModels.NodeA, TestModels.NodeA2], gap["nodes"]!.AsArray().Select(n => n!["nodeIri"]!.GetValue<string>()));
    }

    [Fact]
    public void A_node_without_a_denoted_term_reports_nothing()
    {
        var r = Paths.Read(TestModels.MakeModel(), [("fixture/Alpha", "https://alpha.example/#system")]);
        Assert.DoesNotContain(r["gaps"]!.AsArray(), g => g!["code"]!.GetValue<string>() is "unlinked-term" or "specialization-boundary");
    }

    [Fact]
    public void A_crossing_counts_as_one_step_towards_the_depth_limit()                            // U8
    {
        var r = Paths.Read(TwoSources("exact", "exact"), [("fixture/a", TestModels.NodeA)], maxDepth: 1);
        Assert.All(r["paths"]!.AsArray(), p => Assert.Single(p!["steps"]!.AsArray()));
        Assert.Single(Crossings(r));
    }
}
