using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using RuntimeKnowledge.Contracts;
using ImpactPage = RuntimeKnowledge.Web.Pages.Impact;
using RuntimeKnowledge.Web.Services;
using Xunit;

namespace RuntimeKnowledge.Tests;

public sealed class ImpactUiTests
{
    private const string Snapshot = "example-snapshot";
    private static readonly PinnedSnapshot Pinned = new(new string('a', 40), new string('b', 64), [new PinnedSource("example-claims", new string('c', 40))]);
    private static readonly MasterListIdentity NoList = new("none");
    private static ImpactKey Key(string name) => new("example-claims", $"https://example.test/claims/graph#{name}");
    private static ImpactLead Lead(string name, bool supported = true) =>
        new(Key(name), $"Claim {name}", "token", new string('c', 40), supported, supported ? "Verified evidence: implementation" : "No verified evidence or supported mapping", "graph");
    private static ImpactSearchResult SearchPage(IReadOnlyList<ImpactLead> leads, string? next = null) =>
        new(Snapshot, Pinned, new string('e', 40), NoList, [], leads, [new ImpactGap("coverage-unknown", "No coverage yet")], next);

    private static ImpactStep AssertionStep(string from, string to, params CitedEvidence[] evidence) =>
        new("assertion", Key(from), Key(to), "outgoing", $"https://example.test/claims/graph#{from}-{to}",
            "https://example.test/rel#next", "direct", evidence.Select(e => e.Iri).ToList(), evidence, new string('c', 40));
    private static ImpactPathsResult PathsResult(params ImpactPath[] paths) =>
        new(Snapshot, Pinned, new string('e', 40), NoList, [], [], paths,
            [new ImpactGap("no-relationship-master-list", "Impact direction is assumed from subject to object")]);

    private sealed class Api(Func<string, JsonObject, int, (HttpStatusCode Status, object Body)> respond) : HttpMessageHandler
    {
        public List<(string Path, JsonObject Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            var body = request.Content is null ? new JsonObject() : JsonNode.Parse(await request.Content.ReadAsStringAsync(token))!.AsObject();
            Requests.Add((path, body));
            var (status, response) = respond(path, body, Requests.Count(r => r.Path == path));
            return new HttpResponseMessage(status) { Content = JsonContent.Create(response, options: WorkspaceJson.Options) };
        }
    }

    private static (BunitContext Context, IRenderedComponent<ImpactPage> Page, Api Handler) Render(
        Func<string, JsonObject, int, (HttpStatusCode, object)> respond, AssessmentDraft? draft = null)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var handler = new Api(respond);
        context.Services.AddSingleton(new WorkspaceClient(new HttpClient(handler) { BaseAddress = new Uri("https://workspace.test") }));
        context.Services.AddSingleton(draft ?? new AssessmentDraft());
        return (context, context.Render<ImpactPage>(), handler);
    }

    private static Func<string, JsonObject, int, (HttpStatusCode, object)> SearchThen(ImpactSearchResult search, ImpactPathsResult paths) =>
        (path, _, _) => path == "api/impact/search" ? (HttpStatusCode.OK, search) : (HttpStatusCode.OK, paths);

    private static void Choose(IRenderedComponent<ImpactPage> page, int index, bool on = true) =>
        page.FindAll("li input[type=checkbox]")[index].Change(on);

    private static void Click(IRenderedComponent<ImpactPage> page, string name) =>
        page.FindAll("button").Single(b => b.TextContent.Trim() == name).Click();

    [Fact]
    public void Start_assessment_uses_the_starts_the_shown_paths_were_traced_from()        // B17 (PR #220 review 4)
    {
        var draft = new AssessmentDraft();
        var (context, page, _) = Render(SearchThen(SearchPage([Lead("a"), Lead("b")]), PathsResult(new ImpactPath("p1", [AssertionStep("a", "c")]))), draft);
        using var _ = context;
        SearchFor(page, "claim");
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("li input[type=checkbox]").Count));
        Choose(page, 0);
        Click(page, "Trace impact");
        page.WaitForAssertion(() => Assert.Contains("Impact paths", page.Markup));
        Choose(page, 0, on: false);
        Choose(page, 1);
        page.Find("#assessment-request-id").Change("CR-4");
        page.Find("form.start-assessment").Submit();
        Assert.Equal([Key("a")], draft.Starts);
        Assert.Equal("p1", Assert.Single(draft.Paths).Id);
    }

    [Fact]
    public void A_draft_in_progress_takes_traced_paths_and_keeps_its_backlink()             // B35 (PR #220 review 26)
    {
        var draft = new AssessmentDraft();
        var earlier = Guid.NewGuid();
        draft.Start(Snapshot, "CR-5", "Claim threshold", "Analyst A", AssessmentDraft.Pins(SearchPage([])), earlier);
        var (context, page, _) = Render(SearchThen(SearchPage([Lead("a")]), PathsResult(new ImpactPath("p1", [AssertionStep("a", "c")]))), draft);
        using var _ = context;
        Assert.Equal("Claim threshold", page.Find("#impact-query").GetAttribute("value"));
        page.Find("form").Submit();
        page.WaitForAssertion(() => Assert.Single(page.FindAll("li input[type=checkbox]")));
        Choose(page, 0);
        Click(page, "Trace impact");
        page.WaitForAssertion(() => Assert.Contains("Impact paths", page.Markup));
        Click(page, "Use these paths in assessment CR-5");
        Assert.Equal(earlier.ToString("D"), (string)draft.Record!["startedFrom"]!["assessmentId"]!);
        Assert.Equal("CR-5", draft.RequestId);
        Assert.Equal([Key("a")], draft.Starts);
        Assert.Equal("p1", Assert.Single(draft.Paths).Id);
    }

    [Fact]
    public void A_draft_at_five_traces_does_not_take_paths_from_a_sixth()                    // ADR 0035, amended
    {
        var draft = new AssessmentDraft();
        draft.Start(Snapshot, "CR-6", "Claim threshold", "Analyst A", AssessmentDraft.Pins(SearchPage([])));
        foreach (var depth in Enumerable.Range(1, 5)) Assert.True(draft.UsePaths([], [Key("z")], depth));
        var (context, page, _) = Render(SearchThen(SearchPage([Lead("a")]), PathsResult(new ImpactPath("p1", [AssertionStep("a", "c")]))), draft);
        using var _ = context;
        page.Find("form").Submit();
        page.WaitForAssertion(() => Assert.Single(page.FindAll("li input[type=checkbox]")));
        Choose(page, 0);
        Click(page, "Trace impact");
        page.WaitForAssertion(() => Assert.Contains("Impact paths", page.Markup));
        Click(page, "Use these paths in assessment CR-6");
        Assert.Contains("already cites 5 traces, the most it can. Start another assessment for these paths.", page.Markup);
        Assert.Equal(5, draft.Record!["traces"]!.AsArray().Count);
        Assert.Empty(draft.Paths);
    }

    [Fact]
    public void A_search_for_a_draft_is_pinned_and_a_newer_snapshot_discards_the_draft()      // B36 (re-review 40)
    {
        var draft = new AssessmentDraft();
        draft.Start(Snapshot, "CR-7", "Claim threshold", "Analyst A", AssessmentDraft.Pins(SearchPage([])));
        var (context, page, api) = Render((_, _, _) => (HttpStatusCode.Conflict,
            new { code = "snapshotChanged", title = "The workspace snapshot changed. Start again from a new search." }), draft);
        using var _ = context;
        page.Find("form").Submit();
        page.WaitForAssertion(() => Assert.Contains("the draft in progress can no longer be saved", page.Markup));
        Assert.Equal(Snapshot, (string)api.Requests.Single(r => r.Path == "api/impact/search").Body["snapshotId"]!);
        Assert.Null(draft.Record);
    }

    [Fact]
    public void A_trace_of_more_than_20_starts_is_not_taken_into_an_assessment()             // ADR 0035 (re-review 38)
    {
        var many = Enumerable.Range(0, 21).Select(i => Lead($"l{i}")).ToList();
        var draft = new AssessmentDraft();
        var (context, page, _) = Render(SearchThen(SearchPage(many), PathsResult(new ImpactPath("p1", [AssertionStep("l0", "c")]))), draft);
        using var _ = context;
        SearchFor(page, "claim");
        page.WaitForAssertion(() => Assert.Equal(21, page.FindAll("li input[type=checkbox]").Count));
        for (var i = 0; i < 21; i++) Choose(page, i);
        Click(page, "Trace impact");
        page.WaitForAssertion(() => Assert.Contains("Impact paths", page.Markup));
        page.Find("#assessment-request-id").Change("CR-8");
        page.Find("form.start-assessment").Submit();
        Assert.Contains("at most 20 starts. This one has 21", page.Markup);
        Assert.Null(draft.Record);
    }

    [Fact]
    public void A_graph_lead_and_a_catalog_concept_with_the_same_key_both_render()          // B1 (PR #220 review 24)
    {
        var concept = Lead("a") with { Kind = "concept", SourceCommit = null, Reason = "Catalog concept" };
        var (context, page, _) = Render(SearchThen(SearchPage([Lead("a"), concept]), PathsResult()));
        using var _ = context;
        SearchFor(page, "claim");
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("li input[type=checkbox]").Count));
        Choose(page, 0);
        page.WaitForAssertion(() => Assert.True(page.FindAll("li input[type=checkbox]")[0].HasAttribute("checked")));
    }

    [Fact]
    public void A_lead_shows_its_node_iri_source_commit_and_page_evidence()                  // B1, B25 (PR #220 review 9)
    {
        var lead = Lead("a") with { PageEvidence = [new LeadPageEvidence("https://example.test/claims/graph#ev", "pages/claim.md", 5, 6,
            "Claim policy", "https://confluence.example.test/pages/1", ["A claim affects its case."])] };
        var (context, page, _) = Render(SearchThen(SearchPage([lead]), PathsResult()));
        using var _ = context;
        SearchFor(page, "claim");
        page.WaitForAssertion(() => Assert.Contains(Key("a").NodeIri, page.Find(".lead-identity").TextContent));
        Assert.Contains(new string('c', 12), page.Find(".lead-identity").TextContent);
        Assert.Contains("pages/claim.md:5-6", page.Markup);
        Assert.Equal("https://confluence.example.test/pages/1", page.Find(".impact-leads a").GetAttribute("href"));
        Assert.Equal("A claim affects its case.", page.Find(".impact-leads pre.evidence-lines").TextContent);
    }

    [Fact]
    public void An_object_to_subject_step_is_drawn_from_the_object()                         // B6 (PR #220 review 8)
    {
        var reverse = AssertionStep("a", "b") with { Direction = "object-to-subject" };
        var (context, page) = Traced(PathsResult(new ImpactPath("p1", [reverse])));
        using var _ = context;
        var text = page.Find(".impact-step").TextContent;
        Assert.StartsWith($"example-claims: {Key("b").NodeIri}", text);
        Assert.EndsWith($"example-claims: {Key("a").NodeIri}", text);
    }

    [Fact]
    public void Crossings_and_bindings_show_what_supports_them()                              // B11, B14 (PR #220 review 31)
    {
        var crossing = new ImpactStep("reviewedLinkCrossing", From: Key("a"), To: new ImpactKey("example-ledger", "https://example.test/ledger#claim"),
            SharedTerm: "https://example.test/core#Claim", CatalogCommit: new string('a', 40),
            Links: [new LinkCitation("example-claims", "https://example.test/claims#Claim", "https://example.test/core#Claim", "exact"),
                    new LinkCitation("example-ledger", "https://example.test/ledger#Claim", "https://example.test/core#Claim", "exact")]);
        var binding = new ImpactStep("binding", From: Key("b"), To: Key("c"), RegistryCommit: new string('e', 40),
            IdentityEvidence: new JsonObject { ["sourceId"] = "example-claims", ["path"] = "src/b.ts", ["startLine"] = 2, ["endLine"] = 4, ["reason"] = "reviewed" });
        var (context, page) = Traced(PathsResult(new ImpactPath("p1", [crossing, binding])));
        using var _ = context;
        Assert.Contains("Reviewed link in example-ledger:", page.Markup);
        Assert.Contains("https://example.test/claims#Claim", page.Markup);
        Assert.Contains(new string('a', 12), page.Markup);
        Assert.Contains(new string('e', 12), page.Markup);
        Assert.Contains("example-claims src/b.ts:2-4", page.Markup);
        Assert.Contains("example-ledger: https://example.test/ledger#claim", page.Markup);
    }

    [Fact]
    public void Gaps_name_their_relationship_nodes_and_count_and_not_followed_is_shown()    // B7, B8 (PR #220 review 32)
    {
        var result = PathsResult() with
        {
            NotFollowed = [new NotFollowed("https://example.test/rel#mentions", 3)],
            Gaps = [new ImpactGap("unlisted-relationship", "Impact unknown", Relationship: "https://example.test/rel#archives",
                        Nodes: [new GapNode("example-claims", Key("a").NodeIri)], Assertions: 2),
                    new ImpactGap("unlisted-relationship", "Impact unknown", Relationship: "https://example.test/rel#owns", Assertions: 1)]
        };
        var (context, page) = Traced(result);
        using var _ = context;
        Assert.Contains("Relationship: https://example.test/rel#archives", page.Markup);
        Assert.Contains("Relationship: https://example.test/rel#owns", page.Markup);
        Assert.Contains("Assertions skipped: 2", page.Markup);
        Assert.Contains($"Met at: example-claims: {Key("a").NodeIri}", page.Markup);
        Assert.Contains("https://example.test/rel#mentions", page.Markup);
        Assert.Contains("3 assertions not followed", page.Markup);
    }

    private static void SearchFor(IRenderedComponent<ImpactPage> page, string query)
    {
        page.Find("#impact-query").Change(query);
        page.Find("form").Submit();
    }

    [Fact]
    public void Search_lists_leads_with_support_text_and_loads_more_with_the_cursor()
    {
        var (context, page, api) = Render((path, _, call) => (HttpStatusCode.OK, call == 1
            ? SearchPage([Lead("a"), Lead("b", supported: false)], next: "cursor-1")
            : SearchPage([Lead("c")])));
        using var _ = context;
        SearchFor(page, "claim");
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("li input[type=checkbox]").Count));
        Assert.Contains("Supported", page.Markup);
        Assert.Contains("Not supported", page.Markup);
        Assert.Contains("No verified evidence or supported mapping", page.Markup);

        page.FindAll("button").Single(b => b.TextContent == "Load more").Click();
        page.WaitForAssertion(() => Assert.Equal(3, page.FindAll("li input[type=checkbox]").Count));
        var second = api.Requests[1].Body;
        Assert.Equal("cursor-1", (string)second["cursor"]!);
        Assert.Equal("claim", (string)second["query"]!);
        Assert.DoesNotContain(page.FindAll("button"), b => b.TextContent == "Load more");
    }

    [Fact]
    public void Trace_sends_the_chosen_keys_and_the_snapshot_of_the_first_search()
    {
        var (context, page, api) = Render((path, _, _) => path == "api/impact/search"
            ? (HttpStatusCode.OK, SearchPage([Lead("a"), Lead("b")]))
            : (HttpStatusCode.OK, PathsResult()));
        using var _ = context;
        SearchFor(page, "claim");
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("li input[type=checkbox]").Count));
        var trace = page.FindAll("button").Single(b => b.TextContent == "Trace impact");
        Assert.True(trace.HasAttribute("disabled"));
        page.FindAll("li input[type=checkbox]")[1].Change(true);
        page.FindAll("button").Single(b => b.TextContent == "Trace impact").Click();
        page.WaitForAssertion(() => Assert.Contains(api.Requests, r => r.Path == "api/impact/paths"));
        var body = api.Requests.Single(r => r.Path == "api/impact/paths").Body;
        Assert.Equal(Snapshot, (string)body["snapshotId"]!);
        Assert.Equal(Key("b").NodeIri, (string)body["starts"]![0]!["nodeIri"]!);
        Assert.Single(body["starts"]!.AsArray());
    }

    private static (BunitContext, IRenderedComponent<ImpactPage>) Traced(ImpactPathsResult result)
    {
        var (context, page, _) = Render((path, _, _) => path == "api/impact/search"
            ? (HttpStatusCode.OK, SearchPage([Lead("a")]))
            : (HttpStatusCode.OK, result));
        SearchFor(page, "claim");
        page.WaitForAssertion(() => Assert.Single(page.FindAll("li input[type=checkbox]")));
        page.Find("li input[type=checkbox]").Change(true);
        page.FindAll("button").Single(b => b.TextContent == "Trace impact").Click();
        page.WaitForAssertion(() => Assert.Contains("Limits of this result", page.Markup));
        return (context, page);
    }

    [Fact]
    public void Paths_render_every_step_in_order_with_evidence_lines_as_text()                 // B16, Q4
    {
        var code = new CitedEvidence("https://example.test/claims/graph#ev", "src/a.ts", 3, 3, new string('1', 40), "implementation",
            Lines: ["return <b>x</b>;"]);
        var (context, page) = Traced(PathsResult(new ImpactPath("p1", [AssertionStep("a", "b", code), AssertionStep("b", "c", code)])));
        using var _ = context;
        var steps = page.FindAll(".impact-path ol > li .impact-step").Select(s => s.TextContent).ToList();
        Assert.Equal(2, steps.Count);
        Assert.StartsWith($"example-claims: {Key("a").NodeIri}", steps[0]);
        Assert.EndsWith(Key("c").NodeIri, steps[1]);
        Assert.Contains("(outgoing)", steps[0]);
        Assert.Equal("return <b>x</b>;", page.FindAll("pre.evidence-lines")[0].TextContent);
        Assert.Empty(page.FindAll("pre.evidence-lines b"));
        Assert.Contains("src/a.ts:3-3 · implementation", page.Markup);
    }

    [Fact]
    public void A_page_title_links_to_its_confluence_address_and_a_missing_text_says_unavailable()
    {
        var page1 = new CitedEvidence("https://example.test/claims/graph#ev-page", "pages/p.md", 5, 5, new string('1', 40), "selected-document",
            new PageEvidence("Claim policy", "https://confluence.example.test/pages/1", ["Line"]), ["Line"]);
        var missing = new CitedEvidence("https://example.test/claims/graph#ev-gone", "pages/q.md", 2, 2, new string('2', 40), "selected-document");
        var (context, page) = Traced(PathsResult(new ImpactPath("p1", [AssertionStep("a", "b", page1, missing)])));
        using var _ = context;
        var link = page.Find("a[href='https://confluence.example.test/pages/1']");
        Assert.Equal("Claim policy", link.TextContent);
        Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
        Assert.Contains("Cited text unavailable", page.Markup);
    }

    [Fact]
    public void Snapshot_changed_clears_results_and_shows_the_message()
    {
        var (context, page, _) = Render((path, _, _) => path == "api/impact/search"
            ? (HttpStatusCode.OK, SearchPage([Lead("a")]))
            : (HttpStatusCode.Conflict, new { code = "snapshotChanged", title = "The workspace snapshot changed. Start again from a new search." }));
        using var _ = context;
        SearchFor(page, "claim");
        page.WaitForAssertion(() => Assert.Single(page.FindAll("li input[type=checkbox]")));
        page.Find("li input[type=checkbox]").Change(true);
        page.FindAll("button").Single(b => b.TextContent == "Trace impact").Click();
        page.WaitForAssertion(() => Assert.Contains("The workspace snapshot changed. Start again from a new search.", page.Find("[role=alert]").TextContent));
        Assert.Empty(page.FindAll("li input[type=checkbox]"));
        Assert.DoesNotContain("Limits of this result", page.Markup);
    }

    [Fact]
    public void Controls_are_native_and_labelled()                                             // Q6
    {
        var (context, page, _) = Render((_, _, _) => (HttpStatusCode.OK, SearchPage([Lead("a"), Lead("b")])));
        using var _ = context;
        var input = page.Find("input[type=search]");
        Assert.Equal("Change request description", page.Find($"label[for='{input.Id}']").TextContent);
        SearchFor(page, "claim");
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("input[type=checkbox]").Count));
        Assert.All(page.FindAll("input[type=checkbox]"), box => Assert.NotEmpty(page.Find($"label[for='{box.Id}']").TextContent));
        Assert.All(page.FindAll("button"), button => Assert.Equal("BUTTON", button.TagName));
        Assert.Contains(page.FindAll("button"), b => b.TextContent == "Trace impact");
    }
}
