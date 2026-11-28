using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact.Bundle;
using RuntimeKnowledge.Impact.Engine;
using RuntimeKnowledge.Impact.Model;
using RuntimeKnowledge.Impact.Render;
using Xunit;

namespace RuntimeKnowledge.Tests.Impact;

public sealed class SchemaOneDigestTests
{
    // The digest the Node pilot printed for this record: sha256 of its canonical JSON and a newline.
    private const string PilotDigest = "107de59f11e60f2cc46e8e8449193671e18e607a0f433f3d8aa1a25171fb6d90";

    [Fact]
    public void A_schema_1_record_keeps_the_pilots_digest_in_render_and_feedback()        // B37 (PR #220 review 19)
    {
        var model = ImpactModel.Build(ImpactBundle.Open(CorpusPaths.Objects, CorpusPaths.FirstManifest("pilot", "assessment-render")));
        var record = JsonNode.Parse(File.ReadAllText(Path.Combine(CorpusPaths.Corpus, "pilot", "fixtures", "assessment-v1.json")))!.AsObject();
        var starts = record["confirmedLeads"]!.AsArray().Select(k => (k!["sourceId"]!.GetValue<string>(), k["nodeIri"]!.GetValue<string>())).ToList();
        var paths = starts.Count == 0 ? new JsonArray() : Paths.Read(model, starts)["paths"]!.AsArray();

        Assert.Equal(PilotDigest, Assessments.Validate(record, model, paths, []));
        Assert.Contains("Assessment digest: " + PilotDigest + "\n", AssessmentMarkdownV1.Render(record, model, paths, []));
    }
}
