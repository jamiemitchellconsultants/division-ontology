using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact;
using RuntimeKnowledge.Impact.Bundle;
using RuntimeKnowledge.Impact.Engine;
using RuntimeKnowledge.Impact.Json;
using RuntimeKnowledge.Impact.Model;
using Xunit;

namespace RuntimeKnowledge.Tests.Impact;

public sealed class PathsTests
{
    private static readonly (string, string) AlphaSystem = ("fixture/Alpha", "https://alpha.example/#system");
    private static IEnumerable<JsonObject> Steps(JsonObject r) => r["paths"]!.AsArray().SelectMany(p => p!["steps"]!.AsArray().Select(s => s!.AsObject()));
    private static JsonArray Gaps(JsonObject r) => r["gaps"]!.AsArray();

    [Fact]
    public void Cross_source_path_has_distinct_inferred_assertion_and_binding()
    {
        var r = Paths.Read(TestModels.MakeModel(), [AlphaSystem], 5);
        var path = r["paths"]!.AsArray().First(p => p!["steps"]!.AsArray().Any(s => s!["type"]!.GetValue<string>() == "binding"))!;
        Assert.Contains(path["steps"]!.AsArray(), s => s!["type"]!.GetValue<string>() == "assertion" && s["basis"]!.GetValue<string>() == "inferred");
        var binding = path["steps"]!.AsArray().First(s => s!["type"]!.GetValue<string>() == "binding")!;
        Assert.Equal(new string('e', 40), binding["registryCommit"]!.GetValue<string>());          // B14
        Assert.Equal("reviewed", binding["identityEvidence"]!["reason"]!.GetValue<string>());
    }

    [Fact]
    public void Equal_labels_without_binding_produce_no_cross_source_step()
    {
        var model = TestModels.MakeModel(input => input["registry"]!["bindings"] = new JsonArray());
        var r = Paths.Read(model, [("fixture/Alpha", "https://alpha.example/#external")]);
        Assert.DoesNotContain(Steps(r), s => s["type"]!.GetValue<string>() == "binding");
        Assert.NotEmpty(Gaps(r));
    }

    [Fact]
    public void Preserves_direction_bounds_cycles_and_checks_depth()
    {
        var model = TestModels.MakeModel(input => input["sources"]![0]!["graph"]!["assertions"]!.AsArray().Add(new JsonObject
        {
            ["iri"] = "reverse", ["subject"] = "https://alpha.example/#container", ["object"] = "https://alpha.example/#system",
            ["relationship"] = "cycle", ["basis"] = "direct", ["evidenceIris"] = new JsonArray("https://alpha.example/#evidence")
        }));
        var r = Paths.Read(model, [AlphaSystem], 6);
        Assert.All(r["paths"]!.AsArray(), p => Assert.True(p!["steps"]!.AsArray().Count <= 6));
        Assert.DoesNotContain(Steps(r), s => s["assertionIri"]?.GetValue<string>() == "reverse");
        Assert.Equal("depth must be from 1 to 6", Assert.Throws<ImpactException>(() => Paths.Read(model, [AlphaSystem], 7)).Message);
        Assert.Equal("at least one start is required", Assert.Throws<ImpactException>(() => Paths.Read(model, [])).Message);
    }

    [Fact]
    public void Stale_bindings_remain_gaps_and_never_become_steps()
    {
        var model = TestModels.MakeModel(input =>
        {
            input["registry"]!["bindings"] = new JsonArray();
            input["registry"]!["gaps"] = new JsonArray(new JsonObject { ["code"] = "stale-binding", ["reason"] = "identity changed" });
        });
        var r = Paths.Read(model, [AlphaSystem]);
        Assert.Contains(Gaps(r), g => g!["code"]!.GetValue<string>() == "stale-binding");
        Assert.DoesNotContain(Steps(r), s => s["type"]!.GetValue<string>() == "binding");
    }

    [Fact]
    public void Without_a_master_list_one_gap_says_direction_is_assumed()     // B9
    {
        var r = Paths.Read(TestModels.MakeModel(), [AlphaSystem]);
        var gap = Assert.Single(Gaps(r), g => g!["code"]!.GetValue<string>() == "no-relationship-master-list");
        Assert.Contains("assumed", gap!["reason"]!.GetValue<string>());
        Assert.All(Steps(r).Where(s => s["type"]!.GetValue<string>() == "assertion"), s => Assert.Equal("outgoing", s["direction"]!.GetValue<string>()));
    }

    [Theory]
    [InlineData("pilot")]
    [InlineData("walkthrough")]
    public void Path_steps_reproduce_the_baseline_bytes(string scenario)
    {
        var steps = CorpusPaths.Steps(scenario);
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i]!;
            if (step["argv"]![0]!.GetValue<string>() != "paths" || step["exit"]!.GetValue<int>() != 0) continue;
            var argv = step["argv"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
            var starts = argv.Select((v, k) => (v, k)).Where(x => x.v == "--start").Select(x => argv[x.k + 1])
                .Select(s => { var at = s.IndexOf('='); return (s[..at], s[(at + 1)..]); }).ToList();
            var depthAt = argv.IndexOf("--max-depth");
            var model = ImpactModel.Build(ImpactBundle.Open(CorpusPaths.Objects, step["manifest"]!.GetValue<string>()));
            var actual = EcmaJson.File(Paths.Read(model, starts, depthAt < 0 ? 4 : int.Parse(argv[depthAt + 1])));
            Assert.Equal(File.ReadAllText(CorpusPaths.Expected(scenario, i, "paths.json")), actual);
        }
    }
}
