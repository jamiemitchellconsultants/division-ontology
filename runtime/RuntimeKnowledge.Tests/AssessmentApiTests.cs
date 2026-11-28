using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Tests.OpenApi;
using Xunit;

namespace RuntimeKnowledge.Tests;

[Trait("Category", "Database")]
public sealed class AssessmentApiTests
{
    private const string P = "https://example.test/policies/graph#";
    private static readonly string[] Read = ["ReadKnowledgeGraph"];
    private static readonly string[] Manage = ["ReadKnowledgeGraph", "ManageKnowledgeGraph"];

    private static async Task<(ApiFixture Api, string Snapshot)> OpenAsync(string corpus = "confluence")
    {
        string? snapshot = null;
        var api = await ApiFixture.OpenAsync("Keycloak", async scenario =>
        {
            snapshot = scenario.Bundles.AddCorpusBundle(corpus);
            await scenario.Store.StageAsync(snapshot, CancellationToken.None);
            await scenario.Store.ActivateAsync(snapshot, CancellationToken.None);
        });
        return (api, snapshot!);
    }

    // The confluence corpus's schema 2 draft, whose pins equal the corpus bundle's model.
    private static JsonObject Draft() => JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryPaths.Root,
        "test", "impact-corpus", "confluence", "inputs", "007", "assessment-input.json")))!.AsObject();

    private static async Task<string> BodyAsync(HttpResponseMessage response) => await response.Content.ReadAsStringAsync();

    [Fact]
    public async Task Preview_validates_returns_markdown_and_stores_nothing()                 // B17
    {
        var (api, snapshot) = await OpenAsync();
        await using var _ = api;
        var response = await api.ClientWithToken(Read).PostAsJsonAsync("api/assessments/preview", new AssessmentDraftRequest(snapshot, Draft()));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content.ReadFromJsonAsync<AssessmentPreview>(WorkspaceJson.Options);
        Assert.StartsWith("# Impact assessment: CR-9\n", preview!.Markdown);
        Assert.Equal(preview.Digest, (string)preview.Frozen["digest"]!);
        await using var connection = await api.Scenario.Db.OpenAsync(CancellationToken.None);
        await using var count = new Npgsql.NpgsqlCommand("SELECT count(*) FROM workspace_projection.assessments", connection);
        Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task A_draft_built_in_the_browser_from_a_search_has_the_engine_pins()        // B32
    {
        var (api, snapshot) = await OpenAsync();
        await using var _ = api;
        var reader = api.ClientWithToken(Read);
        var search = await (await reader.PostAsJsonAsync("api/impact/search", new ImpactSearchRequest("expense", null)))
            .Content.ReadFromJsonAsync<ImpactSearchResult>(WorkspaceJson.Options);
        var draft = new RuntimeKnowledge.Web.Services.AssessmentDraft();
        draft.Start(search!.SnapshotId, "CR-10", "Raise the expense approval threshold\n", "Analyst A",
            RuntimeKnowledge.Web.Services.AssessmentDraft.Pins(search));
        draft.Confirm(new ImpactKey("fixture/policies", P + "expense-policy"));
        var response = await reader.PostAsJsonAsync("api/assessments/preview", new AssessmentDraftRequest(snapshot, draft.Record!));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content.ReadFromJsonAsync<AssessmentPreview>(WorkspaceJson.Options);
        Assert.Contains("C4 registry: " + search.Registry!.Commit, preview!.Markdown);
    }

    [Fact]
    public async Task A_browser_draft_on_a_listed_master_list_previews_and_saves()           // B32
    {
        var (api, snapshot) = await OpenAsync("meaning");
        await using var _ = api;
        var search = await (await api.ClientWithToken(Read).PostAsJsonAsync("api/impact/search", new ImpactSearchRequest("claim", null)))
            .Content.ReadFromJsonAsync<ImpactSearchResult>(WorkspaceJson.Options);
        Assert.Equal("listed", search!.MasterList.Status);
        Assert.NotNull(search.MasterList.Sha256);
        var draft = new RuntimeKnowledge.Web.Services.AssessmentDraft();
        draft.Start(search.SnapshotId, "CR-21", "Change how claims affect cases\n", "Analyst A",
            RuntimeKnowledge.Web.Services.AssessmentDraft.Pins(search));
        draft.Confirm(new ImpactKey("fixture/claims", "https://example.test/claims/graph#claim"));
        var preview = await api.ClientWithToken(Read).PostAsJsonAsync("api/assessments/preview", new AssessmentDraftRequest(snapshot, draft.Record!));
        Assert.True(preview.StatusCode == HttpStatusCode.OK, await BodyAsync(preview));
        var save = await api.ClientWithToken(Manage).PostAsJsonAsync("api/assessments", new AssessmentDraftRequest(snapshot, draft.Record!));
        Assert.True(save.StatusCode == HttpStatusCode.Created, await BodyAsync(save));
    }

    [Fact]
    public async Task Preview_refuses_an_impact_not_in_a_cited_path()                         // B17
    {
        var (api, snapshot) = await OpenAsync();
        await using var _ = api;
        var draft = Draft();
        draft["likelyImpacts"]![0]!["key"]!["nodeIri"] = P + "orphan";
        var response = await api.ClientWithToken(Read).PostAsJsonAsync("api/assessments/preview", new AssessmentDraftRequest(snapshot, draft));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Contains("invalidAssessment", body);
        Assert.Contains("impact is not present in a cited path", body);
    }

    [Fact]
    public async Task A_manager_saves_and_another_reader_opens_the_same_content()             // B18, B20, B32
    {
        var (api, snapshot) = await OpenAsync();
        await using var _ = api;
        var preview = await (await api.ClientWithToken(Read).PostAsJsonAsync("api/assessments/preview", new AssessmentDraftRequest(snapshot, Draft())))
            .Content.ReadFromJsonAsync<AssessmentPreview>(WorkspaceJson.Options);
        var created = await api.ClientWithToken(Manage).PostAsJsonAsync("api/assessments", new AssessmentDraftRequest(snapshot, Draft()));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var saved = await created.Content.ReadFromJsonAsync<AssessmentSaved>(WorkspaceJson.Options);
        Assert.Equal($"/api/assessments/{saved!.Id:D}", created.Headers.Location!.OriginalString);

        var reader = api.ClientWithToken(Read, subject: "reader-2");
        var read = await reader.GetFromJsonAsync<SavedAssessment>($"api/assessments/{saved.Id:D}", WorkspaceJson.Options);
        Assert.Equal(preview!.Frozen.ToJsonString(), read!.Frozen.ToJsonString());
        Assert.Equal(preview.Markdown, read.Markdown);
        Assert.NotNull(read.Frozen["pins"]);
        Assert.Equal("fixture-user", read.Author.Subject);
        var markdown = await reader.GetAsync($"api/assessments/{saved.Id:D}/markdown");
        Assert.Equal("text/markdown", markdown.Content.Headers.ContentType!.MediaType);
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(preview.Markdown), await markdown.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task A_reader_cannot_save_and_gets_403()                                     // B19
    {
        var (api, snapshot) = await OpenAsync();
        await using var _ = api;
        var response = await api.ClientWithToken(Read).PostAsJsonAsync("api/assessments", new AssessmentDraftRequest(snapshot, Draft()));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Approval_fields_are_refused_at_the_api()                                // B31
    {
        var (api, snapshot) = await OpenAsync();
        await using var _ = api;
        var draft = Draft();
        draft["approved"] = true;
        var response = await api.ClientWithToken(Manage).PostAsJsonAsync("api/assessments", new AssessmentDraftRequest(snapshot, draft));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("approvalNotSupported", await BodyAsync(response));
    }

    [Fact]
    public async Task Draft_actions_on_another_snapshot_are_snapshot_changed()                // B36
    {
        var (api, _) = await OpenAsync();
        await using var __ = api;
        var other = new string('0', 64);
        foreach (var route in new[] { "api/assessments/preview", "api/assessments" })
        {
            var response = await api.ClientWithToken(Manage).PostAsJsonAsync(route, new AssessmentDraftRequest(other, Draft()));
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("snapshotChanged", await BodyAsync(response));
        }
    }

    [Fact]
    public async Task A_manager_edits_on_the_same_snapshot_and_history_shows_who_changed_it() // B22
    {
        var (api, snapshot) = await OpenAsync();
        await using var _ = api;
        var saved = await (await api.ClientWithToken(Manage).PostAsJsonAsync("api/assessments", new AssessmentDraftRequest(snapshot, Draft())))
            .Content.ReadFromJsonAsync<AssessmentSaved>(WorkspaceJson.Options);
        var edited = Draft();
        edited["description"] = "Raise the expense approval threshold to 750\n";
        var editor = api.ClientWithToken(Manage, subject: "second-manager");
        var response = await editor.PutAsJsonAsync($"api/assessments/{saved!.Id:D}", new AssessmentEditRequest(snapshot, 1, edited));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var read = await editor.GetFromJsonAsync<SavedAssessment>($"api/assessments/{saved.Id:D}", WorkspaceJson.Options);
        Assert.Equal(2, read!.Version);
        Assert.DoesNotContain("revisions", await editor.GetStringAsync($"api/assessments/{saved.Id:D}"));
        var first = await editor.GetFromJsonAsync<AssessmentRevisionPage>($"api/assessments/{saved.Id:D}/revisions?limit=1", WorkspaceJson.Options);
        Assert.Equal("fixture-user", Assert.Single(first!.Revisions).Editor.Subject);
        var second = await editor.GetFromJsonAsync<AssessmentRevisionPage>(
            $"api/assessments/{saved.Id:D}/revisions?limit=1&cursor={Uri.EscapeDataString(first.NextCursor!)}", WorkspaceJson.Options);
        Assert.Equal("second-manager", Assert.Single(second!.Revisions).Editor.Subject);
        Assert.Null(second.NextCursor);
        Assert.Contains("750", read.Markdown);
        var v1 = await editor.GetFromJsonAsync<AssessmentRevision>($"api/assessments/{saved.Id:D}/revisions/1", WorkspaceJson.Options);
        Assert.DoesNotContain("750", v1!.Markdown);
        Assert.Equal(HttpStatusCode.NotFound, (await editor.GetAsync($"api/assessments/{saved.Id:D}/revisions/3")).StatusCode);
        var otherCursor = await editor.GetAsync($"api/assessments/{Guid.NewGuid():D}/revisions?cursor={Uri.EscapeDataString(first.NextCursor!)}");
        Assert.Contains("invalidCursor", await BodyAsync(otherCursor));
        var stale = await editor.PutAsJsonAsync($"api/assessments/{saved.Id:D}", new AssessmentEditRequest(snapshot, 1, edited));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Contains("versionConflict", await BodyAsync(stale));
    }

    [Fact]
    public async Task An_edit_cannot_change_the_request_id_or_the_assessment_it_started_from() // B22, B35 (PR #220 review 12)
    {
        var (api, snapshot) = await OpenAsync();
        await using var _ = api;
        var manager = api.ClientWithToken(Manage);
        var earlier = (await (await manager.PostAsJsonAsync("api/assessments", new AssessmentDraftRequest(snapshot, Draft())))
            .Content.ReadFromJsonAsync<AssessmentSaved>(WorkspaceJson.Options))!.Id;
        var successor = Draft();
        successor["startedFrom"] = new JsonObject { ["assessmentId"] = earlier.ToString("D") };
        var id = (await (await manager.PostAsJsonAsync("api/assessments", new AssessmentDraftRequest(snapshot, successor)))
            .Content.ReadFromJsonAsync<AssessmentSaved>(WorkspaceJson.Options))!.Id;

        var renamed = successor.DeepClone().AsObject();
        renamed["requestId"] = "CR-99";
        var unlinked = successor.DeepClone().AsObject();
        unlinked.Remove("startedFrom");
        var relinked = successor.DeepClone().AsObject();
        relinked["startedFrom"] = new JsonObject { ["assessmentId"] = Guid.NewGuid().ToString("D") };
        foreach (var record in new[] { renamed, unlinked, relinked })
        {
            var response = await manager.PutAsJsonAsync($"api/assessments/{id:D}", new AssessmentEditRequest(snapshot, 1, record));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Contains("invalidAssessment", await BodyAsync(response));
        }
        var read = await manager.GetFromJsonAsync<SavedAssessment>($"api/assessments/{id:D}", WorkspaceJson.Options);
        Assert.Equal(1, read!.Version);
        Assert.Equal(earlier, read.StartedFrom);
    }

    [Fact]
    public async Task Edits_after_a_new_snapshot_are_snapshot_mismatch()                      // B33
    {
        var (api, snapshot) = await OpenAsync();
        await using var _ = api;
        var saved = await (await api.ClientWithToken(Manage).PostAsJsonAsync("api/assessments", new AssessmentDraftRequest(snapshot, Draft())))
            .Content.ReadFromJsonAsync<AssessmentSaved>(WorkspaceJson.Options);
        var newer = await api.Scenario.ActivateAsync("changed");
        var response = await api.ClientWithToken(Manage).PutAsJsonAsync($"api/assessments/{saved!.Id:D}",
            new AssessmentEditRequest(newer, 1, Draft()));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("snapshotMismatch", await BodyAsync(response));
        var read = await api.ClientWithToken(Read).GetFromJsonAsync<SavedAssessment>($"api/assessments/{saved.Id:D}", WorkspaceJson.Options);
        Assert.True(read!.PinnedToEarlierSnapshot);
    }

    private static HttpRequestMessage Request(string method, string route) =>
        new(new HttpMethod(method), route) { Content = JsonContent.Create(new { }) };

    [Theory]                                                                                  // Q2
    [InlineData("POST", "api/assessments/preview")]
    [InlineData("POST", "api/assessments")]
    [InlineData("GET", "api/assessments/00000000-0000-0000-0000-000000000001")]
    [InlineData("PUT", "api/assessments/00000000-0000-0000-0000-000000000001")]
    public async Task Every_assessment_route_refuses_unauthenticated_calls(string method, string route)
    {
        var (api, _) = await OpenAsync();
        await using var _ = api;
        var response = await api.CreateClient().SendAsync(Request(method, route));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]                                                                                  // Q2
    [InlineData("POST", "api/assessments")]
    [InlineData("PUT", "api/assessments/00000000-0000-0000-0000-000000000001")]
    public async Task Save_and_edit_refuse_readers_without_manage(string method, string route)
    {
        var (api, _) = await OpenAsync();
        await using var _ = api;
        var response = await api.ClientWithToken(Read).SendAsync(Request(method, route));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
