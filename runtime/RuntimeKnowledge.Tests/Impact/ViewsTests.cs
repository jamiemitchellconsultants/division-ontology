using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact;
using RuntimeKnowledge.Impact.Bundle;
using RuntimeKnowledge.Impact.Engine;
using RuntimeKnowledge.Impact.Json;
using RuntimeKnowledge.Impact.Model;
using RuntimeKnowledge.Impact.Render;
using Xunit;

namespace RuntimeKnowledge.Tests.Impact;

public sealed class ViewsTests
{
    private static readonly (string, string) AlphaSystem = ("fixture/Alpha", "https://alpha.example/#system");
    private static readonly (string, string) AlphaContainer = ("fixture/Alpha", "https://alpha.example/#container");
    private static readonly (string, string) AlphaComponent = ("fixture/Alpha", "https://alpha.example/#component");

    [Fact]
    public void An_unmapped_focus_is_refused_with_no_c4_mapping()          // B5
    {
        var v = Views.Project(TestModels.MakeModel(), ("fixture/Beta", "https://beta.example/#system"), "container");
        var gap = Assert.Single(v["gaps"]!.AsArray(), g => g!["code"]!.GetValue<string>() == "no-c4-mapping");
        Assert.Equal("https://beta.example/#system", gap!["nodeIri"]!.GetValue<string>());
        Assert.DoesNotContain(v["gaps"]!.AsArray(), g => g!["code"]!.GetValue<string>() == "unsupported-focus");
        Assert.Empty(v["items"]!.AsArray());
    }

    [Fact]
    public void A_focus_mapped_at_another_level_keeps_unsupported_focus()
    {
        var v = Views.Project(TestModels.MakeModel(), ("fixture/Alpha", "https://alpha.example/#container"), "container");
        Assert.Contains(v["gaps"]!.AsArray(), g => g!["code"]!.GetValue<string>() == "unsupported-focus");
        Assert.DoesNotContain(v["gaps"]!.AsArray(), g => g!["code"]!.GetValue<string>() == "no-c4-mapping");
    }

    [Fact]
    public void All_four_levels_retain_focus_and_reviewed_boundaries()
    {
        var model = TestModels.MakeModel();
        foreach (var (level, focus) in new[]
        {
            ("context", AlphaSystem),
            ("container", AlphaSystem),
            ("component", AlphaContainer),
            ("code", AlphaComponent)
        })
        {
            var v = Views.Project(model, focus, level);
            Assert.Equal(level, v["level"]!.GetValue<string>());
            Assert.NotNull(v["boundary"]);
            Assert.True(v["items"]!.AsArray().All(i => i!["supported"]!.GetValue<bool>() && i!["evidenceIris"]!.AsArray().Any()));
            Assert.True(v["items"]!.AsArray().Any());
        }
    }

    [Fact]
    public void Missing_code_mapping_is_an_explicit_gap()
    {
        var model = TestModels.MakeModel(input =>
        {
            input["registry"]!["mappings"] = new JsonArray(
                input["registry"]!["mappings"]!.AsArray().Select(m => m!["level"]!.GetValue<string>() != "code" ? m.DeepClone()! : null).Where(m => m != null).ToArray());
        });
        var v = Views.Project(model, AlphaComponent, "code");
        Assert.Empty(v["items"]!.AsArray());
        Assert.True(v["gaps"]!.AsArray().Any());
    }

    [Fact]
    public void External_context_is_evidenced_and_retains_source_identity()
    {
        var v = Views.Project(TestModels.MakeModel(), AlphaSystem, "context");
        var items = v["items"]!.AsArray().Select(i => i!["key"]!.AsObject()).ToList();
        Assert.Contains(items, k => k["nodeIri"]!.GetValue<string>() == "https://alpha.example/#external");
        Assert.Contains(v["edges"]!.AsArray(), e => e!["basis"]!.GetValue<string>() == "inferred");
        Assert.All(items, k => Assert.NotEqual("fixture/Unjoined", k["sourceId"]!.GetValue<string>()));
    }

    [Fact]
    public void Unsupported_focus_is_a_gap_and_other_boundaries_stay_out()
    {
        var model = TestModels.MakeModel(input =>
        {
            input["registry"]!["mappings"] = new JsonArray(
                input["registry"]!["mappings"]!.AsArray().Select(m =>
                    m!["nodeIri"]!.GetValue<string>() != "https://alpha.example/#container" ? m.DeepClone()! : null).Where(m => m != null).ToArray());
        });
        var v = Views.Project(model, AlphaContainer, "component");
        Assert.Empty(v["items"]!.AsArray());
        Assert.True(v["gaps"]!.AsArray().Any());
    }

    [Fact]
    public void Container_view_includes_an_evidenced_directly_connected_external_system()
    {
        var model = TestModels.MakeModel(input =>
        {
            // Add a "communicates" assertion between container and an external node
            input["sources"]![0]!["graph"]!["assertions"]!.AsArray().Add(new JsonObject
            {
                ["iri"] = "link",
                ["subject"] = "https://alpha.example/#container",
                ["object"] = "https://alpha.example/#external",
                ["relationship"] = "communicates",
                ["basis"] = "direct",
                ["evidenceIris"] = new JsonArray("https://alpha.example/#evidence")
            });
        });
        var v = Views.Project(model, AlphaSystem, "container");
        // External system is directly connected to container via assertion
        var externalItem = v["items"]!.AsArray().First(i => i!["key"]!.AsObject()["nodeIri"]!.GetValue<string>() == "https://alpha.example/#external");
        Assert.False(externalItem!["insideBoundary"]!.GetValue<bool>());
        // Container is inside boundary (parent == focus)
        var containerItem = v["items"]!.AsArray().First(i => i!["key"]!.AsObject()["nodeIri"]!.GetValue<string>() == "https://alpha.example/#container");
        Assert.True(containerItem!["insideBoundary"]!.GetValue<bool>());
        // The link assertion produces an edge
        Assert.True(v["edges"]!.AsArray().Any(e => e!["assertionIri"]!.GetValue<string>() == "link"));
    }

    // Ports of test/c4-code-evidence.test.mjs, against the pilot bundle of the step that passes --supplement.
    private static (ImpactModel Model, JsonObject Supplement) SupplementStep()
    {
        var step = CorpusPaths.Steps("pilot").First(s => s!["argv"]!.AsArray().Any(a => a!.GetValue<string>() == "--supplement"))!;
        var model = ImpactModel.Build(ImpactBundle.Open(CorpusPaths.Objects, step["manifest"]!.GetValue<string>()));
        var supplement = JsonNode.Parse(File.ReadAllText(Path.Combine(CorpusPaths.Corpus, "pilot", "inputs", "011", "supplement.json")))!.AsObject();
        return (model, supplement);
    }

    private static JsonObject With(JsonObject supplement, string key, JsonNode value)
    {
        var copy = supplement.DeepClone().AsObject();
        copy[key] = value;
        return copy;
    }

    [Fact]
    public void Verified_source_line_is_visibly_architect_inspected()
    {
        var (model, good) = SupplementStep();
        var result = CodeSupplement.Verify(model, AlphaComponent, good);
        Assert.Equal("architect-inspected", result["basis"]!.GetValue<string>());
        Assert.Equal(model.Source("fixture/Alpha")!.Commit, result["commit"]!.GetValue<string>());
        Assert.Equal("src/app.ts", result["path"]!.GetValue<string>());
    }

    [Fact]
    public void Wrong_blob_focus_and_line_range_fail()
    {
        var (model, good) = SupplementStep();
        Assert.Matches("(?i)blob", Assert.Throws<ImpactException>(() =>
            CodeSupplement.Verify(model, AlphaComponent, With(good, "blobId", new string('0', 40)))).Message);
        Assert.Matches("(?i)line", Assert.Throws<ImpactException>(() =>
            CodeSupplement.Verify(model, AlphaComponent, With(good, "endLine", 9999))).Message);
        Assert.Matches("(?i)focus|component", Assert.Throws<ImpactException>(() =>
            CodeSupplement.Verify(model, AlphaSystem, good)).Message);
    }

    [Fact]
    public void Deployment_evidence_must_match_its_configured_selector()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("export const api = 1;\n");
        var model = TestModels.MakeModel(input => input["sources"]![0]!["evidenceSelectors"] = new JsonObject
        {
            ["implementation"] = new JsonArray("src/**"),
            ["deployment"] = new JsonArray("**/*.tf")
        });
        var supplement = new JsonObject
        {
            ["sourceId"] = "fixture/Alpha", ["path"] = "src/app.ts", ["startLine"] = 1, ["endLine"] = 1,
            ["blobId"] = RuntimeKnowledge.Impact.Json.GitBlob.Sha1Hex(bytes),
            ["component"] = new JsonObject { ["sourceId"] = "fixture/Alpha", ["nodeIri"] = "https://alpha.example/#component" },
            ["reason"] = "Inspected code", ["role"] = "deployment"
        };
        Assert.Matches("(?i)role|selector", Assert.Throws<ImpactException>(() =>
            CodeSupplement.Verify(model, AlphaComponent, supplement)).Message);
    }

    [Theory]
    [InlineData("pilot")]
    [InlineData("walkthrough")]
    public void View_steps_reproduce_the_baseline_bytes(string scenario)
    {
        var steps = CorpusPaths.Steps(scenario);
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i]!;
            if (step["argv"]![0]!.GetValue<string>() != "view" || step["exit"]!.GetValue<int>() != 0) continue;
            var argv = step["argv"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
            string? Flag(string n) { var at = argv.IndexOf(n); return at < 0 ? null : argv[at + 1]; }
            var focus = Flag("--focus")!; var eq = focus.IndexOf('=');
            var supplementPath = Flag("--supplement");
            JsonObject? supplement = supplementPath is null ? null :
                JsonNode.Parse(File.ReadAllText(Path.Combine(CorpusPaths.Corpus, scenario, "inputs", supplementPath["$input/".Length..])))!.AsObject();
            var model = ImpactModel.Build(ImpactBundle.Open(CorpusPaths.Objects, step["manifest"]!.GetValue<string>()));
            var (json, markdown) = ViewMarkdown.Render(Views.Project(model, (focus[..eq], focus[(eq + 1)..]), Flag("--level")!, supplement));
            var level = Flag("--level");
            Assert.Equal(File.ReadAllText(CorpusPaths.Expected(scenario, i, $"view-{level}.json")), json);
            Assert.Equal(File.ReadAllText(CorpusPaths.Expected(scenario, i, $"view-{level}.md")), markdown);
        }
    }
}
