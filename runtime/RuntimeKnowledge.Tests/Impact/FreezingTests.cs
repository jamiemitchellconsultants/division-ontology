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
    public void Evidence_resolves_in_the_source_of_the_cited_step_when_an_iri_is_reused()   // B25 (PR #220 review 3)
    {
        const string A = "https://alpha.example/#", B = "https://beta.example/#";
        var model = TestModels.MakeModel(input =>
        {
            var beta = input["sources"]!.AsArray()[1]!["graph"]!.AsObject();
            beta["nodes"]!.AsArray().Add(new JsonObject { ["iri"] = B + "part", ["kind"] = "https://ontology-kit.dev/schema#component", ["label"] = "Beta part" });
            beta["assertions"]!.AsArray().Add(new JsonObject
            {
                ["iri"] = B + "contains", ["subject"] = B + "system", ["object"] = B + "part",
                ["relationship"] = "https://ontology-kit.dev/schema#contains", ["basis"] = "direct", ["evidenceIris"] = new JsonArray(A + "evidence")
            });
            beta["evidence"]!.AsArray().Add(new JsonObject
            {
                ["iri"] = A + "evidence", ["path"] = "src/beta.ts", ["startLine"] = 3, ["endLine"] = 4,
                ["blobId"] = new string('2', 40), ["role"] = "implementation"
            });
        });
        var paths = Paths.Read(model, [("fixture/Beta", B + "system")])["paths"]!.AsArray();
        var path = paths.Single(p => p!["steps"]!.AsArray().Any(s => s!["assertionIri"]?.GetValue<string>() == B + "contains"))!;
        var record = Assessments.New(model, "CR-3", "x");
        record["confirmedLeads"] = new JsonArray(new JsonObject { ["sourceId"] = "fixture/Beta", ["nodeIri"] = B + "system" });
        record["likelyImpacts"] = new JsonArray(new JsonObject
        {
            ["key"] = new JsonObject { ["sourceId"] = "fixture/Beta", ["nodeIri"] = B + "part" },
            ["reason"] = "Beta part changes",
            ["pathIds"] = new JsonArray(path["id"]!.GetValue<string>()),
            ["evidenceIris"] = new JsonArray(A + "evidence")
        });

        var evidence = Assert.Single(Freezing.Freeze(record, model, paths, [])["evidence"]!.AsArray())!;
        Assert.Equal("fixture/Beta", evidence["sourceId"]!.GetValue<string>());
        Assert.Equal("src/beta.ts", evidence["path"]!.GetValue<string>());
        Assert.Equal(new string('d', 40), evidence["commit"]!.GetValue<string>());
    }

    [Fact]
    public void With_no_confirmed_lead_the_assessment_still_carries_the_master_list_gap()   // B9 (PR #220 review 7)
    {
        var model = Confluence();
        var record = Assessments.New(model, "CR-9", "x");
        record["assessor"] = "Analyst A";
        var gaps = Freezing.Freeze(record, model, [], [])["gaps"]!["traces"]!.AsArray();
        Assert.Single(gaps, g => g!["code"]!.GetValue<string>() == "no-relationship-master-list");
    }

    [Fact]
    public void A_leads_page_gap_is_carried_even_when_no_cited_path_carries_it()            // B30 (PR #220 review 7)
    {
        var model = Confluence();
        foreach (var field in new[] { "confirmedLeads", "rejectedLeads" })
        {
            var record = Assessments.New(model, "CR-40", "x");
            record["assessor"] = "Analyst A";
            var key = new JsonObject { ["sourceId"] = "fixture/policies", ["nodeIri"] = P + "approval" };
            record[field] = new JsonArray(field == "confirmedLeads" ? key : new JsonObject { ["key"] = key, ["reason"] = "Out of scope" });
            var gaps = Freezing.Freeze(record, model, [], [])["gaps"]!["evidence"]!.AsArray();
            Assert.Contains(gaps, g => g!["code"]!.GetValue<string>() == "page-metadata-missing" &&
                                       g["path"]!.GetValue<string>() == "pages/approval-process.md");
        }
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
        Assert.Contains("&lt;script&gt;x&lt;/script&gt; &#124; \\# Heading \\#\\# Fake", md);
        Assert.DoesNotContain("\n## Fake", md);
    }

    [Theory]                                                                          // Q4 (PR #220 review 14)
    [InlineData("# Not a heading\n", "\\# Not a heading ")]
    [InlineData("- not a list\n", "\\- not a list ")]
    [InlineData("1. not a list\n", "1\\. not a list ")]
    [InlineData("    not code\n", "&#32;   not code ")]
    [InlineData("  - not a list\n", "  \\- not a list ")]
    [InlineData("   2. not a list\n", "   2\\. not a list ")]
    [InlineData("*not* [a](link) `code` _x_\n", "\\*not\\* \\[a\\](link) \\`code\\` \\_x\\_ ")]
    public void A_description_stays_literal_text(string description, string line)
    {
        var (record, model, paths) = Draft();
        record["description"] = description;
        var md = AssessmentMarkdown.Render(Freezing.Freeze(record, model, paths, []));
        Assert.Equal(line, md.Split('\n')[2]);
    }

    [Fact]
    public void The_export_shows_source_commits_registry_digests_and_cited_steps()   // B32, B34 (PR #220 review 15)
    {
        var (record, model, paths) = Draft();
        var frozen = Freezing.Freeze(record, model, paths, []);
        var md = AssessmentMarkdown.Render(frozen);
        foreach (var source in frozen["pins"]!["snapshotId"]!["sources"]!.AsArray())
            Assert.Contains($"Source {source!["id"]}: {source["commit"]}\n", md);
        var digests = frozen["pins"]!["registry"]!["digests"]!;
        Assert.Contains($"(mappings sha256 {digests["mappings"]}, bindings sha256 {digests["bindings"]})", md);
        var path = frozen["paths"]!.AsArray().Single()!;
        Assert.Contains($"## Cited paths\n- {path["id"]}\n  1. fixture/policies: {P}expense-policy → ", md);
        Assert.Contains("  2. ", md);
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
        var nested = record.DeepClone().AsObject();
        nested["unresolved"] = new JsonArray(new JsonObject { ["code"] = "owner-unknown", ["reason"] = "Who owns the ledger?", ["signOff"] = "manager" });
        Assert.Equal("approval is not supported", Assert.Throws<ImpactException>(() => Assessments.Validate(nested, model, paths, [])).Message);
        var deep = record.DeepClone().AsObject();
        deep["likelyImpacts"]![0]!["key"]!["approved"] = true;
        Assert.Equal("approval is not supported", Assert.Throws<ImpactException>(() => Assessments.Validate(deep, model, paths, [])).Message);
        var moved = record.DeepClone().AsObject(); moved["pins"]!["masterList"] = new JsonObject { ["status"] = "listed", ["catalogCommit"] = new string('a', 40), ["sha256"] = new string('b', 64) };
        Assert.Equal("master list mismatch", Assert.Throws<ImpactException>(() => Assessments.Validate(moved, model, paths, [])).Message);
    }
}
