using System.Text;
using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact.Bundle;
using RuntimeKnowledge.Impact.Engine;
using RuntimeKnowledge.Impact.Model;
using Xunit;

namespace RuntimeKnowledge.Tests.Impact;

public sealed class EvidenceTextTests
{
    private const string P = "https://example.test/policies/graph#";
    private static ImpactModel Confluence() =>
        ImpactModel.Build(ImpactBundle.Open(CorpusPaths.Objects, CorpusPaths.FirstManifest("confluence", "paths")));
    private static JsonObject Evidence(ImpactModel m, string iri) => m.Source("fixture/policies")!.EvidenceNode(P + iri)!;

    [Fact]
    public void An_exported_page_shows_title_address_and_cited_lines()                  // B25
    {
        var m = Confluence();
        var text = EvidenceText.Read(m, "fixture/policies", Evidence(m, "ev-policy"));
        Assert.Equal(new PageMetadata("Expense policy", "https://confluence.example.test/pages/1001"), text.Page);
        Assert.Equal(["Claims over 500 need a manager's approval.", "The approval process records every claim."], text.Lines);
        Assert.Null(text.Gap);
    }

    [Fact]
    public void A_page_without_an_address_reports_page_metadata_missing()               // B26
    {
        var m = Confluence();
        var text = EvidenceText.Read(m, "fixture/policies", Evidence(m, "ev-approval"));
        Assert.Equal("Approval process", text.Page!.Title);
        Assert.Null(text.Page.ConfluenceUrl);
        Assert.Equal("page-metadata-missing", text.Gap!["code"]!.GetValue<string>());
        Assert.Equal("Exported page has no Confluence address", text.Gap["reason"]!.GetValue<string>());
        Assert.Equal(["Managers approve claims in the finance system."], text.Lines);
    }

    [Fact]
    public void An_ordinary_document_has_no_page_and_no_gap()                             // B38
    {
        var m = Confluence();
        var text = EvidenceText.Read(m, "fixture/policies", Evidence(m, "ev-legacy"));
        Assert.Null(text.Page);
        Assert.Null(text.Gap);
        Assert.Equal(["The old ledger still exports claims nightly."], text.Lines);
    }

    [Theory]
    [InlineData("---\ntitle: T\nconfluence_url: http://insecure.test/x\n---\n", "Exported page has no Confluence address")]
    [InlineData("---\nconfluence_url: https://c.test/x\n---\n", "Exported page has no title")]
    [InlineData("---\ntitle: &anchor\n---\n", "Exported page has no title or Confluence address")]
    public void Invalid_or_missing_keys_are_reported(string page, string reason)
    {
        var parsed = FrontMatter.Parse(page)!;
        Assert.Equal(reason, (parsed.Title, parsed.ConfluenceUrl) switch
        {
            (null, null) => "Exported page has no title or Confluence address",
            (null, _) => "Exported page has no title",
            (_, null) => "Exported page has no Confluence address",
            _ => "ok"
        });
    }

    [Fact]
    public void A_file_without_a_closing_marker_is_not_a_page() =>
        Assert.Null(FrontMatter.Parse("---\ntitle: never closed\n"));

    [Fact]
    public void Missing_or_undecodable_text_is_a_gap_and_never_replaced()                // B27
    {
        var model = TestModels.MakeModel(blobOverride: _ => new byte[] { 0xff, 0xfe, 0x0a });
        var evidence = model.Source("fixture/Alpha")!.EvidenceNode("https://alpha.example/#evidence")!;
        var text = EvidenceText.Read(model, "fixture/Alpha", evidence);
        Assert.Null(text.Lines);
        Assert.Equal("evidence-text-unavailable", text.Gap!["code"]!.GetValue<string>());
    }

    [Fact]
    public void Page_text_is_attached_to_steps_and_code_steps_keep_their_ids()
    {
        var r = Paths.Read(Confluence(), [("fixture/policies", P + "expense-policy")]);
        var first = r["paths"]![0]!["steps"]![0]!["evidence"]![0]!.AsObject();
        Assert.Equal("Expense policy", first["page"]!["title"]!.GetValue<string>());
        Assert.Contains(r["gaps"]!.AsArray(), g => g!["code"]!.GetValue<string>() == "page-metadata-missing");
        var pilot = Paths.Read(TestModels.MakeModel(), [("fixture/Alpha", "https://alpha.example/#system")]);
        Assert.DoesNotContain(pilot["paths"]!.AsArray().SelectMany(p => p!["steps"]!.AsArray()),
            s => s!["evidence"] is JsonArray e && e.Any(x => x!.AsObject().ContainsKey("page")));
    }
}
