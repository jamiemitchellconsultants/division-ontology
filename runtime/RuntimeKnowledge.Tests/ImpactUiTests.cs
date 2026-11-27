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
        Func<string, JsonObject, int, (HttpStatusCode, object)> respond)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var handler = new Api(respond);
        context.Services.AddSingleton(new WorkspaceClient(new HttpClient(handler) { BaseAddress = new Uri("https://workspace.test") }));
        context.Services.AddSingleton<AssessmentDraft>();
        return (context, context.Render<ImpactPage>(), handler);
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
        Assert.StartsWith(Key("a").NodeIri, steps[0]);
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
