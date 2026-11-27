using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact;
using RuntimeKnowledge.Impact.Bundle;
using RuntimeKnowledge.Impact.Engine;
using RuntimeKnowledge.Impact.Model;
using Xunit;

namespace RuntimeKnowledge.Tests.Impact;

public sealed class SupportTests
{
    private const string P = "https://example.test/policies/graph#";
    private static ImpactModel Confluence() =>
        ImpactModel.Build(ImpactBundle.Open(CorpusPaths.Objects, CorpusPaths.FirstManifest("confluence", "search")));
    private static JsonObject Lead(JsonObject result, string iri) =>
        result["leads"]!.AsArray().Select(l => l!.AsObject()).FirstOrDefault(l => l["key"]!["nodeIri"]!.GetValue<string>() == iri)!;

    [Fact]
    public void Evidence_through_an_assertion_makes_an_unmapped_lead_supported()         // B1
    {
        var lead = Lead(Candidates.Find(Confluence(), "expense"), P + "expense-policy");
        Assert.True(lead["supported"]!.GetValue<bool>());
        Assert.Equal("Verified evidence: selected-document", lead["reason"]!.GetValue<string>());
        Assert.Equal("fixture/policies", lead["key"]!["sourceId"]!.GetValue<string>());
        Assert.Equal("phrase", lead["matchKind"]!.GetValue<string>());
        Assert.Matches("^[0-9a-f]{40}$", lead["sourceCommit"]!.GetValue<string>());
    }

    [Fact]
    public void A_node_without_evidence_or_mapping_stays_unsupported()                   // B2
    {
        var lead = Lead(Candidates.Find(Confluence(), "expense"), P + "orphan");
        Assert.False(lead["supported"]!.GetValue<bool>());
        Assert.Equal("No verified evidence or supported mapping", lead["reason"]!.GetValue<string>());
    }

    [Fact]
    public void A_mapped_lead_keeps_the_reviewed_mapping_reason()                       // B37
    {
        var result = Candidates.Find(TestModels.MakeModel(), exactIri: "https://alpha.example/#system");
        Assert.Equal("Reviewed mapping", Lead(result, "https://alpha.example/#system")["reason"]!.GetValue<string>());
    }

    [Fact]
    public void Roles_are_named_once_in_sorted_order()
    {
        var model = TestModels.MakeModel(input =>
        {
            var graph = input["sources"]![0]!["graph"]!;
            graph["evidence"]!.AsArray().Add(new JsonObject { ["iri"] = "t", ["path"] = "src/app.ts", ["startLine"] = 1, ["endLine"] = 1, ["blobId"] = new string('1', 40), ["role"] = "test" });
            graph["assertions"]!.AsArray().Add(new JsonObject { ["iri"] = "x", ["subject"] = "https://alpha.example/#code", ["object"] = "https://alpha.example/#external",
                ["relationship"] = "calls", ["basis"] = "direct", ["evidenceIris"] = new JsonArray("t", "https://alpha.example/#evidence") });
            input["registry"]!["mappings"] = new JsonArray();
        });
        Assert.Equal("Verified evidence: implementation, test", Lead(Candidates.Find(model, "handle"), "https://alpha.example/#code")["reason"]!.GetValue<string>());
    }

    [Fact]
    public void A_node_cited_only_by_its_own_evidence_is_supported()                   // B1, design records
    {
        var model = TestModels.MakeModel(input =>
        {
            var graph = input["sources"]![0]!["graph"]!;
            graph["assertions"] = new JsonArray();
            graph["nodes"]![0]!["evidenceIris"] = new JsonArray("https://alpha.example/#evidence");
            input["registry"]!["mappings"] = new JsonArray();
        });
        var iri = model.Sources[0].Nodes[0]["iri"]!.GetValue<string>();
        var label = model.Sources[0].Nodes[0]["label"]!.GetValue<string>();
        Assert.Equal("Verified evidence: implementation", Lead(Candidates.Find(model, label), iri)["reason"]!.GetValue<string>());
    }
}
