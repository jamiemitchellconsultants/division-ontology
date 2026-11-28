using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact.Engine;
using RuntimeKnowledge.Impact.Model;
using Xunit;

namespace RuntimeKnowledge.Tests.Impact;

public sealed class DirectionsTests
{
    private const string A = "https://alpha.example/#", K = "https://ontology-kit.dev/schema#";

    private static ImpactModel WithList(params (string Iri, string Impact)[] entries) =>
        TestModels.MakeModel(masterList: new JsonObject
        {
            ["schemaVersion"] = 1,
            ["relationships"] = new JsonArray(entries.Select(e => (JsonNode)new JsonObject { ["iri"] = e.Iri, ["impact"] = e.Impact }).ToArray())
        });

    private static IEnumerable<JsonObject> Steps(JsonObject r) =>
        r["paths"]!.AsArray().SelectMany(p => p!["steps"]!.AsArray()).Select(s => s!.AsObject());

    [Fact]
    public void Subject_to_object_is_followed_forwards_only()                                    // B6
    {
        var r = Paths.Read(WithList((K + "contains", "subject-to-object"), (K + "communicates-with", "none")), [("fixture/Alpha", A + "container")]);
        Assert.All(Steps(r).Where(s => s["type"]!.GetValue<string>() == "assertion"), s => Assert.Equal("subject-to-object", s["direction"]!.GetValue<string>()));
        Assert.DoesNotContain(Steps(r), s => s["object"]?["nodeIri"]?.GetValue<string>() == A + "container" && s["direction"]!.GetValue<string>() == "object-to-subject");
        Assert.Contains(Steps(r), s => s["object"]!["nodeIri"]!.GetValue<string>() == A + "component");
    }

    [Fact]
    public void Object_to_subject_steps_from_the_object_and_keeps_the_assertion_orientation()   // B6
    {
        var r = Paths.Read(WithList((K + "contains", "object-to-subject"), (K + "communicates-with", "none")), [("fixture/Alpha", A + "container")]);
        var step = Assert.Single(Steps(r), s => s["direction"]!.GetValue<string>() == "object-to-subject" && s["subject"]!["nodeIri"]!.GetValue<string>() == A + "system");
        Assert.Equal(A + "container", step["object"]!["nodeIri"]!.GetValue<string>());
        Assert.DoesNotContain(Steps(r), s => s["object"]!["nodeIri"]!.GetValue<string>() == A + "component");
    }

    [Fact]
    public void Both_follows_each_way()                                                           // B6
    {
        var r = Paths.Read(WithList((K + "contains", "both"), (K + "communicates-with", "none")), [("fixture/Alpha", A + "container")]);
        Assert.Contains(Steps(r), s => s["direction"]!.GetValue<string>() == "subject-to-object");
        Assert.Contains(Steps(r), s => s["direction"]!.GetValue<string>() == "object-to-subject");
    }

    [Fact]
    public void None_is_counted_and_never_a_gap()                                                 // B7
    {
        var r = Paths.Read(WithList((K + "contains", "subject-to-object"), (K + "communicates-with", "none")), [("fixture/Alpha", A + "system")]);
        var counted = Assert.Single(r["notFollowed"]!.AsArray());
        Assert.Equal(K + "communicates-with", counted!["relationship"]!.GetValue<string>());
        Assert.Equal(1, counted["assertions"]!.GetValue<int>());
        Assert.DoesNotContain(r["gaps"]!.AsArray(), g => g!["code"]!.GetValue<string>() is "unlisted-relationship" or "no-relationship-master-list");
        Assert.DoesNotContain(Steps(r), s => s["relationship"]?.GetValue<string>() == K + "communicates-with");
    }

    [Fact]
    public void An_unlisted_relationship_is_one_gap_per_iri_and_never_a_step()                   // B8
    {
        var r = Paths.Read(WithList((K + "contains", "subject-to-object")), [("fixture/Alpha", A + "system")]);
        var gap = Assert.Single(r["gaps"]!.AsArray(), g => g!["code"]!.GetValue<string>() == "unlisted-relationship")!;
        Assert.Equal(K + "communicates-with", gap["relationship"]!.GetValue<string>());
        Assert.Equal(A + "system", gap["nodes"]![0]!["nodeIri"]!.GetValue<string>());
        Assert.Contains("unknown", gap["reason"]!.GetValue<string>());
        Assert.DoesNotContain(Steps(r), s => s["relationship"]?.GetValue<string>() == K + "communicates-with");
    }

    [Fact]
    public void The_list_identity_is_on_results_and_record_pins()                                // B10
    {
        var model = WithList((K + "contains", "both"));
        var r = Paths.Read(model, [("fixture/Alpha", A + "system")]);
        Assert.Equal("listed", r["masterList"]!["status"]!.GetValue<string>());
        Assert.Equal(["snapshotId", "registryCommit", "masterList", "notFollowed", "coverage", "paths", "gaps"], r.Select(p => p.Key));
        Assert.Equal(r["masterList"]!.ToJsonString(), Assessments.New(model, "CR", "x")["pins"]!["masterList"]!.ToJsonString());
    }

    private const string B = "https://beta.example/#";

    private static ImpactModel CommunicatesOnly(Action<JsonObject> change) =>
        TestModels.MakeModel(change, masterList: new JsonObject
        {
            ["schemaVersion"] = 1,
            ["relationships"] = new JsonArray(new JsonObject { ["iri"] = K + "communicates-with", ["impact"] = "subject-to-object" })
        });

    private static void AddAssertion(JsonObject input, int source, string iri, string subject, string obj, string relationship, string? node = null)
    {
        var graph = input["sources"]!.AsArray()[source]!["graph"]!.AsObject();
        if (node != null)
            graph["nodes"]!.AsArray().Add(new JsonObject { ["iri"] = node, ["kind"] = K + "component", ["label"] = "Added" });
        graph["assertions"]!.AsArray().Add(new JsonObject
        {
            ["iri"] = iri, ["subject"] = subject, ["object"] = obj, ["relationship"] = relationship,
            ["basis"] = "direct", ["evidenceIris"] = new JsonArray()
        });
    }

    [Fact]
    public void An_unlisted_relationship_at_a_node_reached_at_the_depth_limit_is_a_gap()           // B8 (PR #220 review 28)
    {
        var model = CommunicatesOnly(input => AddAssertion(input, 0, A + "dep", A + "external", A + "elsewhere", K + "depends-on", A + "elsewhere"));
        var gaps = Paths.Read(model, [("fixture/Alpha", A + "system")], 1)["gaps"]!.AsArray();
        var gap = Assert.Single(gaps, g => g!["relationship"]?.GetValue<string>() == K + "depends-on")!;
        Assert.Equal(A + "external", Assert.Single(gap["nodes"]!.AsArray())!["nodeIri"]!.GetValue<string>());
        Assert.DoesNotContain(Steps(Paths.Read(model, [("fixture/Alpha", A + "system")], 1)), s => s["assertionIri"]?.GetValue<string>() == A + "dep");
    }

    [Fact]
    public void Skipped_assertions_sharing_an_iri_in_two_sources_are_counted_twice()             // B8 (PR #220 review 27)
    {
        var model = CommunicatesOnly(input => AddAssertion(input, 1, A + "one", B + "system", B + "part", K + "contains", B + "part"));
        var gaps = Paths.Read(model, [("fixture/Alpha", A + "system")])["gaps"]!.AsArray();
        var gap = Assert.Single(gaps, g => g!["relationship"]?.GetValue<string>() == K + "contains")!;
        Assert.Contains(gap["nodes"]!.AsArray(), n => n!["nodeIri"]!.GetValue<string>() == B + "system");
        Assert.Equal(2, gap["assertions"]!.GetValue<int>());
    }
}
