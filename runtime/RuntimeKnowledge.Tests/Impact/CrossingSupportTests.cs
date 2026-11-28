using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact;
using RuntimeKnowledge.Impact.Engine;
using RuntimeKnowledge.Impact.Model;
using RuntimeKnowledge.Impact.Render;
using Xunit;

namespace RuntimeKnowledge.Tests.Impact;

/// <summary>B39: a reviewed-link crossing substantiates a likely impact on the node it arrives at.</summary>
public sealed class CrossingSupportTests
{
    private const string Core = "https://example.test/core#Claim";
    private const string Ledger = "https://example.test/b/graph#ledger";

    private static ImpactModel Model() => TestModels.TwoSourceModel(
        termA: "https://example.test/a/ontology#Claim", termB: "https://example.test/b/ontology#Claim", shared: Core,
        relationA: "exact", relationB: "exact");

    private static JsonObject Key(string source, string iri) => new() { ["sourceId"] = source, ["nodeIri"] = iri };

    private static (JsonObject Record, ImpactModel Model) Draft(string impactSource, string impactNode, Func<JsonObject, bool> pathFilter)
    {
        var model = Model();
        var record = Assessments.New(model, "CR-39", "Claims cross sources");
        record["assessor"] = "Analyst A";
        record["confirmedLeads"] = new JsonArray(Key("fixture/a", TestModels.NodeA));
        var path = Assessments.CitablePaths(model, record).Select(p => p!.AsObject()).First(pathFilter);
        record["likelyImpacts"] = new JsonArray(new JsonObject
        {
            ["key"] = Key(impactSource, impactNode), ["reason"] = "Shares the claim term",
            ["pathIds"] = new JsonArray(path["id"]!.GetValue<string>()), ["evidenceIris"] = new JsonArray()
        });
        return (record, model);
    }

    private static bool EndsWithCrossingTo(JsonObject path, string nodeIri)
    {
        var last = path["steps"]!.AsArray()[^1]!;
        return last["type"]!.GetValue<string>() == "reviewedLinkCrossing" && last["to"]!["nodeIri"]!.GetValue<string>() == nodeIri;
    }

    [Fact]
    public void An_impact_on_the_node_a_crossing_arrives_at_needs_no_evidence_iri()
    {
        var (record, model) = Draft("fixture/b", TestModels.NodeB, p => EndsWithCrossingTo(p, TestModels.NodeB));
        var paths = Assessments.CitablePaths(model, record);
        Assert.Matches("^[0-9a-f]{64}$", Assessments.Validate(record, model, paths, []));
        var md = AssessmentMarkdown.Render(Assessments.Freeze(record, model, paths, []));
        Assert.Contains("; evidence ; views ; reviewed links through " + Core + " at catalog " + new string('a', 40) + "\n", md);
    }

    [Fact]
    public void An_impact_whose_cited_path_does_not_arrive_by_a_crossing_still_needs_evidence()
    {
        var (record, model) = Draft("fixture/b", Ledger, p => p["steps"]!.AsArray()[^1]!["object"]?["nodeIri"]?.GetValue<string>() == Ledger);
        Assert.Equal("invalid path impact", Assert.Throws<ImpactException>(() =>
            Assessments.Validate(record, model, Assessments.CitablePaths(model, record), [])).Message);
    }
}
