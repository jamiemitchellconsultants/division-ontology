using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact;
using RuntimeKnowledge.Impact.Engine;
using RuntimeKnowledge.Impact.Render;
using Xunit;

namespace RuntimeKnowledge.Tests.Impact;

public sealed class AssessmentsV1Tests
{
    private static readonly (string, string) AlphaSystem = ("fixture/Alpha", "https://alpha.example/#system");
    private static readonly (string, string) AlphaContainer = ("fixture/Alpha", "https://alpha.example/#container");
    private static readonly (string, string) AlphaComponent = ("fixture/Alpha", "https://alpha.example/#component");
    private static readonly (string, string) ExternalBeta = ("fixture/Alpha", "https://alpha.example/#external");
    private static readonly (string, string) BetaSystem = ("fixture/Beta", "https://beta.example/#system");

    // assessment-init now writes schema 2; these tests pin schema 1, so they build its record as the pilot did.
    private static JsonObject NewV1(RuntimeKnowledge.Impact.Model.ImpactModel model, string requestId, string description) => new()
    {
        ["schemaVersion"] = 1,
        ["requestId"] = requestId,
        ["description"] = description,
        ["snapshotId"] = model.SnapshotId.DeepClone(),
        ["registryCommit"] = model.Registry.Commit,
        ["registryDigests"] = new JsonObject { ["mappings"] = model.Registry.MappingSha256, ["bindings"] = model.Registry.BindingSha256 },
        ["architect"] = "",
        ["confirmedLeads"] = new JsonArray(),
        ["rejectedLeads"] = new JsonArray(),
        ["likelyImpacts"] = new JsonArray(),
        ["unresolved"] = new JsonArray()
    };

    private static readonly JsonArray PathsFixture = new JsonArray(
        new JsonObject
        {
            ["id"] = "path-1",
            ["steps"] = new JsonArray(
                new JsonObject
                {
                    ["type"] = "assertion",
                    ["subject"] = new JsonObject { ["sourceId"] = "fixture/Alpha", ["nodeIri"] = "https://alpha.example/#system" },
                    ["object"] = new JsonObject { ["sourceId"] = "fixture/Alpha", ["nodeIri"] = "https://alpha.example/#container" },
                    ["assertionIri"] = "https://alpha.example/#one",
                    ["evidenceIris"] = new JsonArray("https://alpha.example/#evidence")
                },
                new JsonObject
                {
                    ["type"] = "assertion",
                    ["subject"] = new JsonObject { ["sourceId"] = "fixture/Alpha", ["nodeIri"] = "https://alpha.example/#container" },
                    ["object"] = new JsonObject { ["sourceId"] = "fixture/Alpha", ["nodeIri"] = "https://alpha.example/#component" },
                    ["assertionIri"] = "https://alpha.example/#two",
                    ["evidenceIris"] = new JsonArray("https://alpha.example/#evidence")
                }
            )
        }
    );

    [Fact]
    public void Architect_findings_render_with_evidence_and_gaps()
    {
        var model = TestModels.MakeModel();
        var a = NewV1(model, "CR-1", "Change Alpha API");
        a["architect"] = "Architect A";
        a["confirmedLeads"] = new JsonArray(
            new JsonObject { ["sourceId"] = "fixture/Alpha", ["nodeIri"] = "https://alpha.example/#system" });
        a["rejectedLeads"] = new JsonArray(
            new JsonObject
            {
                ["key"] = new JsonObject { ["sourceId"] = "fixture/Alpha", ["nodeIri"] = "https://alpha.example/#external" },
                ["reason"] = "Out of scope"
            });
        a["likelyImpacts"] = new JsonArray(
            new JsonObject
            {
                ["key"] = new JsonObject { ["sourceId"] = "fixture/Alpha", ["nodeIri"] = "https://alpha.example/#component" },
                ["reason"] = "Call path",
                ["pathIds"] = new JsonArray("path-1"),
                ["evidenceIris"] = new JsonArray("https://alpha.example/#evidence")
            });
        a["unresolved"] = new JsonArray(
            new JsonObject { ["code"] = "partial-code", ["reason"] = "No graph code coverage" });

        var digest = Assessments.Validate(a, model, PathsFixture, Array.Empty<JsonObject>());
        var markdown = AssessmentMarkdownV1.Render(a, model, PathsFixture, Array.Empty<JsonObject>());

        Assert.Contains("CR-1", markdown);
        Assert.False(a.ContainsKey("approved"));
    }

    [Fact]
    public void Stale_snapshot_paths_and_evidence_fail_validation()
    {
        var model = TestModels.MakeModel();
        var a = NewV1(model, "CR-1", "Change");
        a["snapshotId"]!["catalogCommit"] = new string('0', 40);
        Assert.Contains("snapshot", Assert.Throws<ImpactException>(() => Assessments.Validate(a, model, PathsFixture, Array.Empty<JsonObject>())).Message);

        var b = NewV1(model, "CR-1", "Change");
        b["likelyImpacts"] = new JsonArray(
            new JsonObject
            {
                ["key"] = new JsonObject { ["sourceId"] = "fixture/Alpha", ["nodeIri"] = "https://alpha.example/#component" },
                ["reason"] = "x",
                ["pathIds"] = new JsonArray("missing"),
                ["evidenceIris"] = new JsonArray("missing")
            });
        var ex = Assert.Throws<ImpactException>(() => Assessments.Validate(b, model, PathsFixture, Array.Empty<JsonObject>()));
        Assert.True(ex.Message.Contains("path") || ex.Message.Contains("evidence"));
    }

    [Fact]
    public void Rendering_requires_an_identified_architect()
    {
        var model = TestModels.MakeModel();
        var a = NewV1(model, "CR-1", "Change");
        Assert.Contains("architect", Assert.Throws<ImpactException>(() => AssessmentMarkdownV1.Render(a, model, PathsFixture, Array.Empty<JsonObject>())).Message);
    }

    [Fact]
    public void Unrelated_paths_cannot_support_an_impacted_element()
    {
        var model = TestModels.MakeModel();
        var a = NewV1(model, "CR-1", "Change");
        a["likelyImpacts"] = new JsonArray(
            new JsonObject
            {
                ["key"] = new JsonObject { ["sourceId"] = "fixture/Beta", ["nodeIri"] = "https://beta.example/#system" },
                ["reason"] = "unrelated",
                ["pathIds"] = new JsonArray("path-1"),
                ["evidenceIris"] = new JsonArray("https://alpha.example/#evidence")
            });
        var ex = Assert.Throws<ImpactException>(() => Assessments.Validate(a, model, PathsFixture, Array.Empty<JsonObject>()));
        Assert.True(ex.Message.Contains("path") && ex.Message.Contains("impact"));
    }

    [Fact]
    public void Approval_fields_are_refused()
    {
        var model = TestModels.MakeModel();
        var record = NewV1(model, "CR-1", "Change");
        record["approved"] = true;
        Assert.Equal("approval is not supported",
            Assert.Throws<ImpactException>(() => Assessments.Validate(record, model, new JsonArray(), Array.Empty<JsonObject>())).Message);
    }

    [Fact]
    public void A_nested_sign_off_is_refused_in_schema_1_too()                              // B31 (re-review 13)
    {
        var model = TestModels.MakeModel();
        var record = NewV1(model, "CR-1", "Change");
        record["unresolved"] = new JsonArray(new JsonObject { ["code"] = "owner-unknown", ["reason"] = "Who owns it?", ["signOff"] = "manager" });
        Assert.Equal("approval is not supported",
            Assert.Throws<ImpactException>(() => Assessments.Validate(record, model, new JsonArray(), Array.Empty<JsonObject>())).Message);
    }

    [Fact]
    public void Feedback_separate_qualitative_records_misses_and_uncertainty()
    {
        var assessment = new JsonObject
        {
            ["requestId"] = "CR-1",
            ["digest"] = new string('a', 64)
        };
        var feedback = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["assessmentDigest"] = assessment["digest"]!.DeepClone()!,
            ["architect"] = "Architect A",
            ["reviewer"] = "Reviewer B",
            ["perceivedAccuracy"] = "mostly accurate",
            ["perceivedCompleteness"] = "partial",
            ["confidence"] = "medium",
            ["usefulDiscoveries"] = new JsonArray("Beta dependency"),
            ["suspectedMisses"] = new JsonArray("data store"),
            ["falseLeads"] = new JsonArray("legacy API"),
            ["examples"] = new JsonArray("CR-1 path"),
            ["openQuestions"] = new JsonArray("Who owns the data store?")
        };

        var markdown = FeedbackMarkdown.Render(feedback, assessment);
        Assert.True(System.Text.RegularExpressions.Regex.IsMatch(markdown, @"suspected misses", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        Assert.Contains("data store", markdown);
        Assert.False(feedback.ContainsKey("approvalStatus"));

        var badFeedback = (JsonObject)feedback.DeepClone()!;
        badFeedback["assessmentDigest"] = new string('0', 64);
        Assert.Contains("digest", Assert.Throws<ImpactException>(() => FeedbackMarkdown.Render(badFeedback, assessment)).Message);
    }
}
