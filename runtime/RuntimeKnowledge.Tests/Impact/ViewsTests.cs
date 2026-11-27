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
    public void Context_view_retains_same_source_mappings_and_edges()
    {
        var model = TestModels.MakeModel();
        var v = Views.Project(model, AlphaSystem, "context");
        // Context view only includes same-source context mappings (Alpha system + external)
        var items = v["items"]!.AsArray().Select(i => i!["key"]!.AsObject()["nodeIri"]!.GetValue<string>()).ToList();
        Assert.Contains("https://alpha.example/#system", items);
        Assert.Contains("https://alpha.example/#external", items);
        // No Beta (different source)
        Assert.DoesNotContain("https://beta.example/#system", items);
        // The alpha-external assertion produces an edge
        Assert.True(v["edges"]!.AsArray().Any(e => e!["assertionIri"]!.GetValue<string>() == "https://alpha.example/#assertion"));
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

    [Fact]
    public void Verified_source_line_is_visibly_architect_inspected()
    {
        var model = TestModels.MakeModel();
        var supplement = new JsonObject
        {
            ["sourceId"] = "fixture/Alpha",
            ["path"] = "src/app.ts",
            ["startLine"] = 1,
            ["endLine"] = 1,
            ["blobId"] = "129fbfa125de7a8b2b24c3dbc34ef5d0e0570747",
            ["component"] = new JsonObject
            {
                ["sourceId"] = "fixture/Alpha",
                ["nodeIri"] = "https://alpha.example/#component"
            },
            ["reason"] = "Inspected code",
            ["role"] = "implementation"
        };
        var v = Views.Project(model, AlphaComponent, "code", supplement);
        var inspected = v["items"]!.AsArray().First(i => i!["basis"]?.GetValue<string>() == "architect-inspected");
        Assert.Equal("src/app.ts", inspected!["path"]!.GetValue<string>());
    }

    [Fact]
    public void Wrong_blob_focus_and_line_range_fail()
    {
        var model = TestModels.MakeModel();
        var good = new JsonObject
        {
            ["sourceId"] = "fixture/Alpha",
            ["path"] = "src/app.ts",
            ["startLine"] = 1,
            ["endLine"] = 1,
            ["blobId"] = "129fbfa125de7a8b2b24c3dbc34ef5d0e0570747",
            ["component"] = new JsonObject
            {
                ["sourceId"] = "fixture/Alpha",
                ["nodeIri"] = "https://alpha.example/#component"
            },
            ["reason"] = "Inspected code",
            ["role"] = "implementation"
        };

        Assert.Throws<ImpactException>(
            () => CodeSupplement.Verify(model, AlphaComponent, new JsonObject { ["sourceId"] = "fixture/Alpha", ["path"] = "src/app.ts", ["startLine"] = 1, ["endLine"] = 1, ["blobId"] = new string('0', 40), ["component"] = new JsonObject { ["sourceId"] = "fixture/Alpha", ["nodeIri"] = "https://alpha.example/#component" }, ["reason"] = "Inspected code", ["role"] = "implementation" }))
            .Message.Contains("blob");

        Assert.Throws<ImpactException>(
            () => CodeSupplement.Verify(model, AlphaComponent, new JsonObject { ["sourceId"] = "fixture/Alpha", ["path"] = "src/app.ts", ["startLine"] = 1, ["endLine"] = 9999, ["blobId"] = "129fbfa125de7a8b2b24c3dbc34ef5d0e0570747", ["component"] = new JsonObject { ["sourceId"] = "fixture/Alpha", ["nodeIri"] = "https://alpha.example/#component" }, ["reason"] = "Inspected code", ["role"] = "implementation" }))
            .Message.Contains("line");

        var focusEx = Assert.Throws<ImpactException>(() => CodeSupplement.Verify(model, AlphaSystem, good));
        Assert.True(focusEx.Message.Contains("focus") || focusEx.Message.Contains("component"));
    }

    [Fact]
    public void Deployment_evidence_must_match_its_configured_selector()
    {
        var model = TestModels.MakeModel(input =>
        {
            input["sources"]![0]!["evidenceSelectors"]!["deployment"] = new JsonArray("**/*.tf");
        });
        var supplement = new JsonObject
        {
            ["sourceId"] = "fixture/Alpha",
            ["path"] = "src/app.ts",
            ["startLine"] = 1,
            ["endLine"] = 1,
            ["blobId"] = "129fbfa125de7a8b2b24c3dbc34ef5d0e0570747",
            ["component"] = new JsonObject
            {
                ["sourceId"] = "fixture/Alpha",
                ["nodeIri"] = "https://alpha.example/#component"
            },
            ["reason"] = "Inspected code",
            ["role"] = "deployment"
        };
        var depEx = Assert.Throws<ImpactException>(() => CodeSupplement.Verify(model, AlphaComponent, supplement));
        Assert.True(depEx.Message.Contains("role") || depEx.Message.Contains("selector"));
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
