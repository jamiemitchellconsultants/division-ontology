using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact;
using RuntimeKnowledge.Impact.Engine;
using RuntimeKnowledge.Impact.Model;
using Xunit;

namespace RuntimeKnowledge.Tests.Impact;

/// <summary>ADR 0035: a record's citable paths come from the traces it lists.</summary>
public sealed class TracesTests
{
    private const string A = "https://alpha.example/#", K = "https://ontology-kit.dev/schema#";

    // The pilot model with the code node's chain extended two more steps, so a path of five steps exists.
    private static ImpactModel Deep() => TestModels.MakeModel(input =>
    {
        var graph = input["sources"]![0]!["graph"]!.AsObject();
        foreach (var (name, from, to) in new[] { ("five", "code", "x1"), ("six", "x1", "x2") })
        {
            graph["nodes"]!.AsArray().Add(new JsonObject { ["iri"] = A + to, ["kind"] = K + "component", ["label"] = to });
            graph["assertions"]!.AsArray().Add(new JsonObject
            {
                ["iri"] = A + name, ["subject"] = A + from, ["object"] = A + to, ["relationship"] = K + "contains",
                ["basis"] = "direct", ["evidenceIris"] = new JsonArray(A + "evidence")
            });
        }
    });

    private static JsonObject Key(string node) => new() { ["sourceId"] = "fixture/Alpha", ["nodeIri"] = A + node };

    private static JsonObject Record(ImpactModel model, string impactOn, JsonObject path, JsonArray? traces)
    {
        var record = Assessments.New(model, "CR-36", "Deep change");
        record["assessor"] = "Analyst A";
        record["confirmedLeads"] = new JsonArray(Key("system"));
        record["likelyImpacts"] = new JsonArray(new JsonObject
        {
            ["key"] = Key(impactOn), ["reason"] = "Reached",
            ["pathIds"] = new JsonArray(path["id"]!.GetValue<string>()),
            ["evidenceIris"] = new JsonArray(A + "evidence")
        });
        if (traces != null) record["traces"] = traces;
        return record;
    }

    private static JsonObject Trace(string start, int depth) => new() { ["starts"] = new JsonArray(Key(start)), ["maxDepth"] = depth };

    private static string Validate(ImpactModel model, JsonObject record) =>
        Assessments.Validate(record, model, Assessments.CitablePaths(model, record), []);

    private static JsonObject PathTo(ImpactModel model, string start, int depth, string end) =>
        Paths.Read(model, [("fixture/Alpha", A + start)], depth)["paths"]!.AsArray()
            .Select(p => p!.AsObject()).First(p => p["steps"]!.AsArray()[^1]!["object"]?["nodeIri"]?.GetValue<string>() == A + end);

    [Fact]
    public void A_path_from_a_depth_5_trace_validates_when_the_record_lists_that_trace()
    {
        var model = Deep();
        var path = PathTo(model, "system", 5, "x2");
        Assert.Equal(5, path["steps"]!.AsArray().Count);
        Assert.Equal("unresolved path", Assert.Throws<ImpactException>(() => Validate(model, Record(model, "x2", path, null))).Message);
        Assert.Matches("^[0-9a-f]{64}$", Validate(model, Record(model, "x2", path, new JsonArray(Trace("system", 5)))));
    }

    [Fact]
    public void A_path_from_an_unconfirmed_start_validates_when_the_record_lists_that_trace()
    {
        var model = Deep();
        var path = PathTo(model, "container", 4, "code");
        Assert.Equal("unresolved path", Assert.Throws<ImpactException>(() => Validate(model, Record(model, "code", path, null))).Message);
        Assert.Matches("^[0-9a-f]{64}$", Validate(model, Record(model, "code", path, new JsonArray(Trace("system", 4), Trace("container", 4)))));
    }

    [Fact]
    public void Without_traces_the_confirmed_leads_at_depth_4_are_traced()
    {
        var model = Deep();
        var record = Record(model, "code", PathTo(model, "system", 4, "code"), null);
        var citable = Assessments.CitablePaths(model, record);
        Assert.Equal(Paths.Read(model, [("fixture/Alpha", A + "system")], 4)["paths"]!.AsArray().Count, citable.Count);
        record["confirmedLeads"] = new JsonArray();
        Assert.Empty(Assessments.CitablePaths(model, record));
    }

    [Fact]
    public void Too_many_traces_or_starts_or_a_depth_out_of_range_is_refused()
    {
        var model = Deep();
        var path = PathTo(model, "system", 4, "code");
        var tooMany = new JsonArray(Enumerable.Range(0, 21).Select(_ => (JsonNode)Trace("system", 4)).ToArray());
        var wideStarts = new JsonObject { ["starts"] = new JsonArray(Enumerable.Range(0, 21).Select(_ => (JsonNode)Key("system")).ToArray()), ["maxDepth"] = 4 };
        foreach (var traces in new[] { tooMany, new JsonArray(wideStarts), new JsonArray(Trace("system", 7)), new JsonArray(Trace("system", 0)),
                                       new JsonArray(new JsonObject { ["starts"] = new JsonArray(), ["maxDepth"] = 4 }) })
            Assert.Equal("invalid traces", Assert.Throws<ImpactException>(() => Assessments.CitablePaths(model, Record(model, "code", path, traces))).Message);
    }

    [Fact]
    public void The_export_lists_the_traces_a_record_cites()
    {
        var model = Deep();
        var path = PathTo(model, "system", 5, "x2");
        var record = Record(model, "x2", path, new JsonArray(Trace("system", 5)));
        var md = RuntimeKnowledge.Impact.Render.AssessmentMarkdown.Render(
            Assessments.Freeze(record, model, Assessments.CitablePaths(model, record), []));
        Assert.Contains("## Cited traces\n- depth 5 from fixture/Alpha: " + A + "system\n", md);
        Assert.DoesNotContain("## Cited traces", RuntimeKnowledge.Impact.Render.AssessmentMarkdown.Render(
            Assessments.Freeze(Record(model, "code", PathTo(model, "system", 4, "code"), null), model, Assessments.CitablePaths(model, Record(model, "code", path, null)), [])));
    }

    [Fact]
    public void Code_excerpts_are_frozen_but_not_exported()                                // B34, B28 (PR #220 review 6)
    {
        var model = Deep();
        var path = PathTo(model, "system", 4, "code");
        var record = Record(model, "code", path, null);
        var frozen = Assessments.Freeze(record, model, Assessments.CitablePaths(model, record), []);
        var evidence = Assert.Single(frozen["evidence"]!.AsArray())!;
        Assert.Equal("implementation", evidence["role"]!.GetValue<string>());
        Assert.Equal(["export const api = 1;"], evidence["lines"]!.AsArray().Select(l => l!.GetValue<string>()));
        Assert.DoesNotContain("export const api", RuntimeKnowledge.Impact.Render.AssessmentMarkdown.Render(frozen));
    }
}
