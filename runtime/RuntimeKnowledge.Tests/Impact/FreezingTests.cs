using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact;
using RuntimeKnowledge.Impact.Bundle;
using RuntimeKnowledge.Impact.Engine;
using RuntimeKnowledge.Impact.Json;
using RuntimeKnowledge.Impact.Model;
using RuntimeKnowledge.Impact.Render;
using Xunit;

namespace RuntimeKnowledge.Tests.Impact;

public sealed class FreezingTests
{
    private const string P = "https://example.test/policies/graph#";
    private static ImpactModel Confluence() =>
        ImpactModel.Build(ImpactBundle.Open(CorpusPaths.Objects, CorpusPaths.FirstManifest("confluence", "assessment-render")));

    private static (JsonObject Record, ImpactModel Model, JsonArray Paths) Draft(string? reason = null)
    {
        var model = Confluence();
        var record = Assessments.New(model, "CR-9", "Raise the expense approval threshold\n");
        var paths = Paths.Read(model, [("fixture/policies", P + "expense-policy")])["paths"]!.AsArray();
        var path = paths.First(p => p!["steps"]!.AsArray().Count == 2)!;
        record["assessor"] = "Analyst A";
        record["confirmedLeads"] = new JsonArray(new JsonObject { ["sourceId"] = "fixture/policies", ["nodeIri"] = P + "expense-policy" });
        record["likelyImpacts"] = new JsonArray(new JsonObject
        {
            ["key"] = new JsonObject { ["sourceId"] = "fixture/policies", ["nodeIri"] = P + "ledger" },
            ["reason"] = reason ?? "Ledger supports approvals",
            ["pathIds"] = new JsonArray(path["id"]!.GetValue<string>()),
            ["evidenceIris"] = new JsonArray(path["steps"]!.AsArray().SelectMany(s => s!["evidenceIris"]!.AsArray()).Select(e => (JsonNode?)e!.GetValue<string>()).ToArray())
        });
        return (record, model, paths);
    }

    [Fact]
    public void Init_writes_schema_2_with_pins()                                     // B32 (command line)
    {
        var record = Assessments.New(Confluence(), "CR-9", "x");
        Assert.Equal(2, record["schemaVersion"]!.GetValue<int>());
        Assert.Equal("none", record["pins"]!["masterList"]!["status"]!.GetValue<string>());
        Assert.False(record.ContainsKey("architect"));
    }

    [Fact]
    public void Rendered_schema_2_shows_pins_page_evidence_and_every_gap_section()   // B25, B30, B32
    {
        var (record, model, paths) = Draft();
        var md = AssessmentMarkdown.Render(Freezing.Freeze(record, model, paths, []));
        Assert.Contains("Relationship master list: none", md);
        Assert.Contains("  Page: Expense policy (https://confluence.example.test/pages/1001)", md);
        Assert.Contains("      Claims over 500 need a manager's approval.", md);
        Assert.Contains("  Page: Approval process (no Confluence address)", md);
        Assert.Contains("## Snapshot coverage gaps", md);
        Assert.Contains("- no-relationship-master-list:", md);
        Assert.Contains("- page-metadata-missing: fixture/policies pages/approval-process.md — Exported page has no Confluence address", md);
        Assert.DoesNotContain("The old ledger still exports", md);          // ordinary document lines are not printed (B38)
    }

    [Fact]
    public void Injected_markup_renders_as_inert_text()                              // Q4
    {
        var (record, model, paths) = Draft("<script>x</script> | # Heading\n## Fake");
        var md = AssessmentMarkdown.Render(Freezing.Freeze(record, model, paths, []));
        Assert.DoesNotContain("<script>", md);
        Assert.Contains("&lt;script&gt;x&lt;/script&gt; &#124; # Heading ## Fake", md);
        Assert.DoesNotContain("\n## Fake", md);
    }

    [Fact]
    public void The_digest_is_the_canonical_digest_of_the_frozen_assessment()        // Q3
    {
        var (record, model, paths) = Draft();
        var frozen = Freezing.Freeze(record, model, paths, []);
        var withoutDigest = frozen.DeepClone().AsObject();
        withoutDigest.Remove("digest");
        Assert.Equal(EcmaJson.CanonicalDigest(withoutDigest), frozen["digest"]!.GetValue<string>());
        Assert.Equal(frozen["digest"]!.GetValue<string>(), Assessments.Validate(record, model, paths, []));
    }

    [Fact]
    public void Sign_off_and_mismatched_pins_are_refused()                           // B31, B33
    {
        var (record, model, paths) = Draft();
        var signed = record.DeepClone().AsObject(); signed["managerSignOff"] = "yes";
        Assert.Equal("approval is not supported", Assert.Throws<ImpactException>(() => Assessments.Validate(signed, model, paths, [])).Message);
        var moved = record.DeepClone().AsObject(); moved["pins"]!["masterList"] = new JsonObject { ["status"] = "listed", ["catalogCommit"] = new string('a', 40), ["sha256"] = new string('b', 64) };
        Assert.Equal("master list mismatch", Assert.Throws<ImpactException>(() => Assessments.Validate(moved, model, paths, [])).Message);
    }
}
