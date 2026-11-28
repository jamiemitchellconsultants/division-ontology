using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact;
using RuntimeKnowledge.Impact.Bundle;
using RuntimeKnowledge.Impact.Engine;
using RuntimeKnowledge.Impact.Json;
using RuntimeKnowledge.Impact.Model;
using Xunit;

namespace RuntimeKnowledge.Tests.Impact;

public sealed class CandidatesTests
{
    private static JsonArray Leads(JsonObject result) => result["leads"]!.AsArray();

    [Fact]
    public void Description_gives_leads_without_an_impact_conclusion()
    {
        var r = Candidates.Find(TestModels.MakeModel(), "change Alpha API");
        Assert.Contains(Leads(r), x => x!["key"]!["sourceId"]!.GetValue<string>() == "fixture/Alpha");
        Assert.All(Leads(r), x => Assert.False(x!.AsObject().ContainsKey("impact")));
    }

    [Fact]
    public void Exact_iri_and_no_match_gap()
    {
        var model = TestModels.MakeModel();
        Assert.Equal("https://alpha.example/#system", Leads(Candidates.Find(model, exactIri: "https://alpha.example/#system"))[0]!["key"]!["nodeIri"]!.GetValue<string>());
        var empty = Candidates.Find(model, "unmatched phrase");
        Assert.Empty(Leads(empty));
        Assert.Contains(empty["gaps"]!.AsArray(), g => g!["code"]!.GetValue<string>() == "unknown-search");
    }

    [Fact]
    public void Catalog_only_matches_stay_unsupported_leads() =>
        Assert.Contains(Leads(Candidates.Find(TestModels.MakeModel(), "catalog only")),
            x => x!["key"]!["nodeIri"]!.GetValue<string>() == "https://alpha.example/concept" && !x["supported"]!.GetValue<bool>());

    [Fact]
    public void Equal_labels_remain_separate_source_owned_leads()
    {
        var r = Candidates.Find(TestModels.MakeModel(), "Beta");
        Assert.Contains(Leads(r), x => x!["key"]!["sourceId"]!.GetValue<string>() == "fixture/Alpha");
        Assert.Contains(Leads(r), x => x!["key"]!["sourceId"]!.GetValue<string>() == "fixture/Beta");
    }

    [Fact]
    public void Search_reports_partial_source_coverage_with_results()
    {
        var model = TestModels.MakeModel(input => input["sources"]![0]!["graph"]!["coverage"]!.AsArray().Add(new JsonObject
            { ["iri"] = "gap", ["selector"] = "legacy/**", ["status"] = "excluded", ["priority"] = 5, ["reason"] = "Not reviewed" }));
        var r = Candidates.Find(model, "Alpha");
        Assert.NotEmpty(Leads(r));
        Assert.Contains(r["gaps"]!.AsArray(), g => g!["reason"]?.GetValue<string>() == "Not reviewed");
    }

    [Fact]
    public void A_query_or_exact_iri_is_required() =>
        Assert.Equal("query or exact IRI is required",
            Assert.Throws<ImpactException>(() => Candidates.Find(TestModels.MakeModel(), "  ")).Message);

    [Theory]
    [InlineData("pilot")]
    [InlineData("walkthrough")]
    public void Search_steps_reproduce_the_baseline_bytes(string scenario)
    {
        var steps = CorpusPaths.Steps(scenario);
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i]!;
            if (step["argv"]![0]!.GetValue<string>() != "search" || step["exit"]!.GetValue<int>() != 0) continue;
            var argv = step["argv"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
            string? Flag(string name) { var at = argv.IndexOf(name); return at < 0 ? null : argv[at + 1]; }
            var model = ImpactModel.Build(ImpactBundle.Open(CorpusPaths.Objects, step["manifest"]!.GetValue<string>()));
            var actual = EcmaJson.File(Candidates.Find(model, Flag("--query") ?? "", Flag("--exact-iri")));
            Assert.Equal(File.ReadAllText(CorpusPaths.Expected(scenario, i, "candidates.json")), actual);
        }
    }
}
