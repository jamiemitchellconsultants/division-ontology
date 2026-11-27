using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Web.Pages;
using RuntimeKnowledge.Web.Services;
using Xunit;
using SavedAssessmentView = RuntimeKnowledge.Web.Pages.Assessment;

namespace RuntimeKnowledge.Tests;

public sealed class AssessmentUiTests
{
    private const string Snapshot = "example-snapshot";
    private static ImpactKey Key(string name) => new("example-claims", $"https://example.test/claims/graph#{name}");
    private static readonly JsonObject Pins = new()
    {
        ["snapshotId"] = new JsonObject { ["catalogCommit"] = new string('a', 40), ["catalogSha256"] = new string('b', 64),
            ["sources"] = new JsonArray(new JsonObject { ["id"] = "example-claims", ["commit"] = new string('c', 40) }) },
        ["masterList"] = new JsonObject { ["status"] = "none" },
        ["registry"] = new JsonObject { ["commit"] = new string('e', 40), ["digests"] = new JsonObject { ["mappings"] = "m", ["bindings"] = "b" } }
    };

    private sealed class Api(Func<string, string, JsonObject, (HttpStatusCode Status, object Body)> respond) : HttpMessageHandler
    {
        public List<(string Method, string Path, JsonObject Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.PathAndQuery.TrimStart('/');
            var body = request.Content is null ? new JsonObject() : JsonNode.Parse(await request.Content.ReadAsStringAsync(token))!.AsObject();
            Requests.Add((request.Method.Method, path, body));
            var (status, response) = respond(request.Method.Method, path, body);
            return new HttpResponseMessage(status) { Content = JsonContent.Create(response, options: WorkspaceJson.Options) };
        }
    }

    private static (BunitContext Context, AssessmentDraft Draft, Api Handler) Context(bool canManage,
        Func<string, string, JsonObject, (HttpStatusCode, object)> respond)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var handler = new Api((method, path, body) => path == "api/session"
            ? (HttpStatusCode.OK, new WorkspaceSession(true, canManage))
            : respond(method, path, body));
        context.Services.AddSingleton(new WorkspaceClient(new HttpClient(handler) { BaseAddress = new Uri("https://workspace.test") }));
        var draft = new AssessmentDraft();
        context.Services.AddSingleton(draft);
        return (context, draft, handler);
    }

    private static AssessmentPreview Preview(string markdown) =>
        new(Snapshot, new string('d', 64), new JsonObject { ["format"] = 1 }, markdown);

    private static ImpactStep Step(string from, string to, string evidence) =>
        new("assertion", Key(from), Key(to), "outgoing", $"https://example.test/claims/graph#{from}-{to}",
            "https://example.test/rel#next", "direct", [evidence], [], new string('c', 40));

    private static void StartDraft(AssessmentDraft draft)
    {
        draft.Start(Snapshot, "CR-1", "Raise the claim threshold", "Analyst A", Pins);
        draft.UsePaths([new ImpactPath("p1", [Step("a", "b", "https://example.test/claims/graph#e1")]),
                        new ImpactPath("p2", [Step("a", "c", "https://example.test/claims/graph#e2"), Step("c", "b", "https://example.test/claims/graph#e3")])],
            [Key("a")]);
    }

    private static IEnumerable<AngleSharp.Dom.IElement> Buttons(IRenderedComponent<AssessmentDraftPage> page, string name) =>
        page.FindAll("button").Where(b => b.TextContent.Trim() == name);

    [Fact]
    public void The_draft_lives_in_memory_and_requests_carry_its_snapshot()                    // ADR 0034, B36
    {
        var (context, draft, api) = Context(canManage: false, (_, _, _) => (HttpStatusCode.OK, Preview("# Impact assessment: CR-1\n")));
        using var _ = context;
        StartDraft(draft);
        var page = context.Render<AssessmentDraftPage>();
        Buttons(page, "Preview").Single().Click();
        page.WaitForAssertion(() => Assert.Contains(api.Requests, r => r.Path == "api/assessments/preview"));
        var body = api.Requests.Single(r => r.Path == "api/assessments/preview").Body;
        Assert.Equal(Snapshot, (string)body["snapshotId"]!);
        Assert.Equal("CR-1", (string)body["record"]!["requestId"]!);
        Assert.Equal(2, (int)body["record"]!["schemaVersion"]!);
    }

    [Fact]
    public void Reject_needs_a_reason_and_impacts_offer_only_evidence_from_chosen_paths()        // B17
    {
        var (context, draft, _) = Context(canManage: false, (_, _, _) => (HttpStatusCode.OK, new { }));
        using var _ = context;
        StartDraft(draft);
        var page = context.Render<AssessmentDraftPage>();
        Buttons(page, "Reject").Single().Click();
        Assert.Contains("Give a reason before rejecting a lead.", page.Markup);
        Assert.Empty(draft.Record!["rejectedLeads"]!.AsArray());

        page.Find("#reject-reason-0").Change("Out of scope");
        Buttons(page, "Reject").Single().Click();
        Assert.Equal("Out of scope", (string)draft.Record!["rejectedLeads"]![0]!["reason"]!);

        page.Find("#impact-node").Change($"{Key("b").SourceId}\n{Key("b").NodeIri}");
        Assert.Equal(2, page.FindAll("input[id^=impact-path-]").Count);
        Assert.Empty(page.FindAll("input[id^=impact-evidence-]"));
        page.Find("#impact-path-0").Change(true);
        var offered = page.FindAll("label[for^=impact-evidence-]").Select(l => l.TextContent).ToList();
        Assert.Equal(["https://example.test/claims/graph#e1"], offered);
    }

    [Fact]
    public void Preview_shows_markdown_as_text()                                                // Q4
    {
        var (context, draft, _) = Context(canManage: false, (_, _, _) => (HttpStatusCode.OK, Preview("# Impact\n<script>alert(1)</script>\n")));
        using var _ = context;
        StartDraft(draft);
        var page = context.Render<AssessmentDraftPage>();
        Buttons(page, "Preview").Single().Click();
        page.WaitForAssertion(() => Assert.Contains("<script>alert(1)</script>", page.Find("pre.assessment-markdown").TextContent));
        Assert.Empty(page.FindAll("script"));
        Assert.StartsWith("data:text/markdown;charset=utf-8;base64,", page.Find("a[download]").GetAttribute("href"));
        Assert.Equal("CR-1.md", page.Find("a[download]").GetAttribute("download"));
    }

    [Fact]
    public void Save_is_absent_for_readers_and_a_403_keeps_the_draft_and_export()              // B19
    {
        var (readerContext, readerDraft, _) = Context(canManage: false, (_, _, _) => (HttpStatusCode.OK, Preview("# x\n")));
        using (readerContext)
        {
            StartDraft(readerDraft);
            var page = readerContext.Render<AssessmentDraftPage>();
            page.WaitForAssertion(() => Assert.Single(Buttons(page, "Preview")));
            Assert.Empty(Buttons(page, "Save"));
        }

        var (context, draft, _) = Context(canManage: true, (_, path, _) => path == "api/assessments"
            ? (HttpStatusCode.Forbidden, new { code = "roleRequired", title = "Your account does not have the role this operation needs." })
            : (HttpStatusCode.OK, Preview("# x\n")));
        using var _ = context;
        StartDraft(draft);
        var manager = context.Render<AssessmentDraftPage>();
        manager.WaitForAssertion(() => Assert.Single(Buttons(manager, "Save")));
        Buttons(manager, "Preview").Single().Click();
        manager.WaitForAssertion(() => Assert.NotNull(manager.Find("a[download]")));
        Buttons(manager, "Save").Single().Click();
        manager.WaitForAssertion(() => Assert.Contains("Only workspace managers can save. Your draft is kept and can still be exported.", manager.Markup));
        Assert.NotNull(draft.Record);
        Assert.NotNull(manager.Find("a[download]"));
    }

    [Fact]
    public void Snapshot_changed_discards_the_draft_with_the_message()                        // B36
    {
        var (context, draft, _) = Context(canManage: true, (_, _, _) =>
            (HttpStatusCode.Conflict, new { code = "snapshotChanged", title = "The workspace snapshot changed. Start again from a new search." }));
        using var _ = context;
        StartDraft(draft);
        var page = context.Render<AssessmentDraftPage>();
        Buttons(page, "Preview").Single().Click();
        page.WaitForAssertion(() => Assert.Contains(
            "The workspace snapshot changed, so this draft can no longer be saved. Exports you downloaded are unaffected.", page.Markup));
        Assert.Null(draft.Record);
        Assert.Contains("No assessment draft is in progress", page.Markup);
    }

    private static SavedAssessment Saved(bool pinned) => new(Guid.Parse("11111111-1111-1111-1111-111111111111"), 2, Snapshot, "CR-1",
        new Actor("https://issuer.test", "manager"), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, pinned, null,
        new JsonObject
        {
            ["format"] = 1,
            ["record"] = new JsonObject { ["requestId"] = "CR-1", ["description"] = "Raise the claim threshold", ["likelyImpacts"] = new JsonArray() },
            ["pins"] = Pins.DeepClone(),
            ["leads"] = new JsonObject { ["confirmed"] = new JsonArray(), ["rejected"] = new JsonArray() },
            ["paths"] = new JsonArray(),
            ["evidence"] = new JsonArray(),
            ["gaps"] = new JsonObject { ["snapshot"] = new JsonArray(), ["traces"] = new JsonArray(new JsonObject { ["code"] = "no-relationship-master-list", ["reason"] = "Assumed direction" }),
                ["evidence"] = new JsonArray(), ["unresolved"] = new JsonArray() },
            ["digest"] = new string('d', 64)
        },
        "# Impact assessment: CR-1 v2\n",
        [new AssessmentRevision(1, new Actor("https://issuer.test", "manager"), DateTimeOffset.UnixEpoch, new JsonObject(), "# v1\n"),
         new AssessmentRevision(2, new Actor("https://issuer.test", "second-manager"), DateTimeOffset.UnixEpoch, new JsonObject(), "# v2\n")]);

    [Fact]
    public void The_saved_view_shows_pins_banner_history_and_no_approve_control()               // B20, B22, B31, B32, B34
    {
        var (context, _, _) = Context(canManage: true, (_, _, _) => (HttpStatusCode.OK, Saved(pinned: true)));
        using var _ = context;
        var page = context.Render<SavedAssessmentView>(p => p.Add(c => c.Id, Guid.Parse("11111111-1111-1111-1111-111111111111")));
        page.WaitForAssertion(() => Assert.Contains("Pinned to an earlier snapshot", page.Markup));
        Assert.Contains(new string('a', 40), page.Markup);
        Assert.Contains(new string('e', 40), page.Markup);
        Assert.Contains("second-manager", page.Markup);
        Assert.Contains("no-relationship-master-list", page.Markup);
        Assert.DoesNotContain(page.FindAll("button"), b => System.Text.RegularExpressions.Regex.IsMatch(b.TextContent, "approve|sign[- ]?off",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        page.FindAll("button").Single(b => b.TextContent == "Show version 1").Click();
        Assert.Equal("# v1\n", page.Find("pre.assessment-markdown").TextContent);
    }

    [Fact]
    public void Edit_is_hidden_when_pinned_to_an_earlier_snapshot()                            // B33
    {
        foreach (var pinned in new[] { true, false })
        {
            var (context, _, _) = Context(canManage: true, (_, _, _) => (HttpStatusCode.OK, Saved(pinned)));
            using var _ = context;
            var page = context.Render<SavedAssessmentView>(p => p.Add(c => c.Id, Guid.Parse("11111111-1111-1111-1111-111111111111")));
            page.WaitForAssertion(() => Assert.Contains("History", page.Markup));
            Assert.Equal(!pinned, page.FindAll("button").Any(b => b.TextContent == "Edit"));
        }
    }

    [Fact]
    public void Start_from_this_one_is_hidden_for_readers_and_starts_a_draft_with_started_from()   // B35
    {
        var (readerContext, _, _) = Context(canManage: false, (_, _, _) => (HttpStatusCode.OK, Saved(pinned: true)));
        using (readerContext)
        {
            var view = readerContext.Render<SavedAssessmentView>(p => p.Add(c => c.Id, Guid.Parse("11111111-1111-1111-1111-111111111111")));
            view.WaitForAssertion(() => Assert.Contains("History", view.Markup));
            Assert.DoesNotContain(view.FindAll("button"), b => b.TextContent == "Start a new assessment from this one");
        }

        var successor = new AssessmentSuccessor("newer-snapshot", new JsonObject
        {
            ["schemaVersion"] = 2, ["requestId"] = "CR-1", ["description"] = "Raise the claim threshold", ["assessor"] = "",
            ["pins"] = Pins.DeepClone(), ["confirmedLeads"] = new JsonArray(), ["rejectedLeads"] = new JsonArray(),
            ["likelyImpacts"] = new JsonArray(), ["unresolved"] = new JsonArray(),
            ["startedFrom"] = new JsonObject { ["assessmentId"] = "11111111-1111-1111-1111-111111111111" }
        });
        var (context, draft, api) = Context(canManage: true, (_, path, _) => path.EndsWith("/successor")
            ? (HttpStatusCode.OK, successor)
            : (HttpStatusCode.OK, Saved(pinned: true)));
        using var _ = context;
        var page = context.Render<SavedAssessmentView>(p => p.Add(c => c.Id, Guid.Parse("11111111-1111-1111-1111-111111111111")));
        page.WaitForAssertion(() => Assert.Contains(page.FindAll("button"), b => b.TextContent == "Start a new assessment from this one"));
        page.FindAll("button").Single(b => b.TextContent == "Start a new assessment from this one").Click();
        page.WaitForAssertion(() => Assert.NotNull(draft.Record));
        Assert.Equal("newer-snapshot", draft.SnapshotId);
        Assert.Equal("CR-1", draft.RequestId);
        Assert.Equal("11111111-1111-1111-1111-111111111111", (string)draft.Record!["startedFrom"]!["assessmentId"]!);
        Assert.Contains(api.Requests, r => r.Method == "POST" && r.Path == "api/assessments/11111111-1111-1111-1111-111111111111/successor");
    }

    [Fact]
    public void A_successor_links_back_to_the_assessment_it_started_from()                     // B35
    {
        var earlier = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var (context, _, _) = Context(canManage: false, (_, _, _) => (HttpStatusCode.OK, Saved(pinned: false) with { StartedFrom = earlier }));
        using var _ = context;
        var page = context.Render<SavedAssessmentView>(p => p.Add(c => c.Id, Guid.Parse("11111111-1111-1111-1111-111111111111")));
        page.WaitForAssertion(() => Assert.Contains("Started from", page.Markup));
        Assert.Equal($"assessments/{earlier:D}", page.Find("a[href^='assessments/2222']").GetAttribute("href"));
    }

    private static AssessmentSummary Summary(int n, bool pinned = false) =>
        new(Guid.Parse($"00000000-0000-0000-0000-{n:000000000000}"), $"CR-{n}", new Actor("https://issuer.test", "manager"),
            DateTimeOffset.UnixEpoch.AddDays(n), new string('f', 64), pinned);

    [Fact]
    public void The_list_renders_rows_and_load_more_sends_the_cursor()                         // B21
    {
        var (context, _, api) = Context(canManage: false, (_, path, _) => (HttpStatusCode.OK, path == "api/assessments?cursor=next-1"
            ? new AssessmentPage([Summary(0)], null)
            : new AssessmentPage([Summary(2), Summary(1, pinned: true)], "next-1")));
        using var _ = context;
        var page = context.Render<Assessments>();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("tbody tr").Count));
        Assert.Equal("assessments/00000000-0000-0000-0000-000000000002", page.Find("tbody tr a").GetAttribute("href"));
        Assert.Contains("Pinned to an earlier snapshot", page.FindAll("tbody tr")[1].TextContent);
        page.FindAll("button").Single(b => b.TextContent == "Load more").Click();
        page.WaitForAssertion(() => Assert.Equal(3, page.FindAll("tbody tr").Count));
        Assert.Equal(["api/assessments", "api/assessments?cursor=next-1"], api.Requests.Select(r => r.Path));
        Assert.Empty(page.FindAll("button").Where(b => b.TextContent == "Load more"));
    }

    [Fact]
    public void Controls_are_native_and_labelled()                                             // Q6
    {
        var (context, draft, _) = Context(canManage: true, (_, _, _) => (HttpStatusCode.OK, Preview("# x\n")));
        using var _ = context;
        StartDraft(draft);
        var page = context.Render<AssessmentDraftPage>();
        page.WaitForAssertion(() => Assert.Single(Buttons(page, "Save")));
        page.Find("#impact-node").Change($"{Key("b").SourceId}\n{Key("b").NodeIri}");
        foreach (var control in page.FindAll("input, select"))
            Assert.NotEmpty(page.Find($"label[for='{control.Id}']").TextContent);
        Assert.All(page.FindAll("button"), b => Assert.Equal("BUTTON", b.TagName));
    }
}
