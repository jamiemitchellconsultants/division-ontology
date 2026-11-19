using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Web.Components;
using RuntimeKnowledge.Web.Services;
using RuntimeKnowledge.Web.Pages;
using Xunit;

namespace RuntimeKnowledge.Tests;

public sealed class WorkspaceUiTests
{
    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public void FailedSaveKeepsDraftAndRequiresDeliberateRetry(HttpStatusCode status)
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var target = new PublishedKey("source-a", "concept", "https://example.test/a");
        var author = new Actor("issuer", "subject");
        var provenance = new Provenance(target, "commit", "snapshot", null, []);
        var current = new RuntimeContribution(Guid.NewGuid(), 1, "annotation", author, DateTimeOffset.UnixEpoch,
            "snapshot", false, new AnnotationData(target, "businessExplanation", "Original text", provenance), null, []);
        using var api = new SaveHandler(status, current with
        {
            Version = 2,
            Annotation = current.Annotation! with { Text = "Another user's change" }
        });
        using var http = new HttpClient(api) { BaseAddress = new Uri("https://workspace.test") };
        context.Services.AddSingleton(new WorkspaceClient(http));
        context.Services.AddScoped<DraftState>();
        var loadDraft = context.JSInterop.Setup<string?>("sessionStorage.getItem", _ => true);
        loadDraft.SetResult(null);
        IRenderedComponent<ContributionEditor> RenderEditor() => context.Render<ContributionEditor>(parameters => parameters
            .Add(component => component.Target, target)
            .Add(component => component.Current, current)
            .Add(component => component.Mode, "annotation"));
        var editor = RenderEditor();
        editor.WaitForElement("textarea").Change("My unsaved change");
        editor.Find("form").Submit();
        editor.WaitForAssertion(() =>
        {
            Assert.Equal("My unsaved change", editor.Find("textarea").GetAttribute("value"));
            Assert.Single(api.Submissions);
            if (status == HttpStatusCode.Conflict)
            {
                Assert.Contains("Another user", editor.Markup);
                Assert.True(editor.Find("button[type=submit]").HasAttribute("disabled"));
            }
            else
            {
                Assert.Contains("Sign in again", editor.Markup);
            }
        });
        if (status == HttpStatusCode.Conflict)
            editor.Find("[data-review]").Click();
        else
        {
            var stored = context.JSInterop.Invocations["sessionStorage.setItem"].Single();
            loadDraft.SetResult((string?)stored.Arguments[1]);
            editor = RenderEditor();
            editor.WaitForAssertion(() =>
                Assert.Equal("My unsaved change", editor.Find("textarea").GetAttribute("value")));
            Assert.Single(api.Submissions);
        }
        editor.Find("form").Submit();
        editor.WaitForAssertion(() =>
        {
            Assert.Equal(2, api.Submissions.Count);
            Assert.Equal(status == HttpStatusCode.Conflict ? 2 : 1, api.Submissions[1].ExpectedVersion);
            Assert.Equal("My unsaved change", api.Submissions[1].Text);
        });
    }

    [Fact]
    public void ReaderSeesProvenanceAndCanRequestUnavailablePaths()
    {
        using var context = new BunitContext();
        var from = new PublishedKey("source-a", "concept", "https://example.test/from");
        var to = new PublishedKey("source-b", "concept", "https://example.test/to");
        var published = new PublishedKey("source-b", "assertion", "https://example.test/edge");
        var provenance = new Provenance(from, "from-commit", "active", null, []);
        var toOrigin = new Provenance(to, "to-commit", "earlier", null, []);
        var edgeOrigin = new Provenance(published, "codification-commit", "active", "Reviewed assertion", []);
        var actor = new Actor("issuer", "contributor");
        var relation = new RuntimeContribution(Guid.NewGuid(), 3, "relationship", actor, DateTimeOffset.UnixEpoch,
            "earlier", false, null, new RelationshipData(from, to, RelationshipMeaning.Custom("uses", "For delivery"),
                "uses", provenance, toOrigin, null),
            [new CodificationAssociation(Guid.NewGuid(), published, edgeOrigin, actor, DateTimeOffset.UnixEpoch)]);
        var step = new TraversalStep(null, relation.Id, from, to, "uses", "incoming", "runtime", null, true, "earlier",
            Contribution: relation, ToEndpoint: new EndpointContext(to, "Target", toOrigin, false));
        TraversalRequest? traversal = null;
        using var http = new HttpClient(new StubHandler(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/traverse")
            {
                traversal = await request.Content!.ReadFromJsonAsync<TraversalRequest>(token);
                return new TraversalResult("active", [new TraversalPath([step])], [], false, []);
            }
            if (request.RequestUri.AbsolutePath == "/api/session") return new WorkspaceSession(true, false);
            return new EntityDetail("active", new PublishedEntity(from, "Start", null, [], provenance), true, []);
        })) { BaseAddress = new Uri("https://workspace.test") };
        context.Services.AddSingleton(new WorkspaceClient(http));
        var view = context.Render<Entity>(parameters => parameters.Add(component => component.Key, WorkspaceLinks.Encode(from)));
        view.WaitForAssertion(() => Assert.Contains("from-commit", view.Markup));
        Assert.DoesNotContain("Add annotation", view.Markup);
        Assert.DoesNotContain("Create a relationship", view.Markup);
        view.Find("select").Change("incoming");
        view.Find("input[type=checkbox]").Change(true);
        view.Find("form").Submit();
        view.WaitForAssertion(() =>
        {
            Assert.Equal(new TraversalRequest(from, "incoming", 2, true), traversal);
            Assert.Contains("Endpoint unavailable", view.Markup);
            Assert.Contains("to-commit", view.Markup);
            Assert.Contains("codification-commit", view.Markup);
            Assert.Contains("Runtime relationship", view.Markup);
            Assert.Contains("Published codification", view.Markup);
        });
    }

    [Fact]
    public void InventoryDistinguishesIdenticalLabelsAndSearchDistinguishesPendingAndEmpty()
    {
        using var context = new BunitContext();
        var from = new PublishedKey("source-a", "concept", "https://example.test/from");
        var to = new PublishedKey("source-b", "concept", "https://example.test/to");
        PublishedEntity MakeEntity(PublishedKey key) =>
            new(key, "Same label", null, [], new Provenance(key, "commit", "snapshot", null, []));
        var searches = 0;
        using var http = new HttpClient(new StubHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath != "/api/inventory")
                return Task.FromResult<object>(new SearchResult("snapshot", [], ++searches == 1));
            Assert.Empty(request.RequestUri.Query);
            var edge = new PublishedKey("source-a", "assertion", "https://example.test/edge");
            var relation = new PublishedEdge(edge, from, to, "Published edge", null,
                new Provenance(edge, "edge-commit", "snapshot", null, []));
            return Task.FromResult<object>(new InventoryPage("snapshot", [MakeEntity(from), MakeEntity(to)], [], [], null, [relation]));
        }))
        { BaseAddress = new Uri("https://workspace.test") };
        context.Services.AddSingleton(new WorkspaceClient(http));
        var view = context.Render<Explore>();
        view.WaitForAssertion(() =>
        {
            var links = view.FindAll("a").Where(link => link.TextContent == "Same label").ToArray();
            Assert.Equal(2, links.Length);
            Assert.NotEqual(links[0].GetAttribute("href"), links[1].GetAttribute("href"));
            Assert.Contains("source-a", view.Markup);
            Assert.Contains("source-b", view.Markup);
            Assert.StartsWith("relationship/published/", view.FindAll("a").Single(link => link.TextContent == "Published edge").GetAttribute("href"));
            Assert.Contains("edge-commit", view.Markup);
        });
        view.Find("input[type=search]").Change("definition");
        view.Find("form").Submit();
        view.WaitForAssertion(() => Assert.Contains("Indexing is pending", view.Markup));
        Assert.DoesNotContain("No matches", view.Markup);
        view.Find("form").Submit();
        view.WaitForAssertion(() => Assert.Contains("No matches", view.Markup));
        Assert.DoesNotContain("Indexing is pending", view.Markup);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "roleRequired", "Your account does not have the role this operation needs.")]
    [InlineData(HttpStatusCode.BadRequest, "unreadableRequest", "The request body or parameters could not be read.")]
    [InlineData(HttpStatusCode.Unauthorized, "authenticationRequired", "Sign in to use the workspace API.")]
    public async Task RefusalsCarryTheApiTitleAndCode(HttpStatusCode status, string code, string title)
    {
        using var http = new HttpClient(new RefusalHandler(status, code, title)) { BaseAddress = new Uri("https://workspace.test") };
        var error = await Assert.ThrowsAsync<WorkspaceApiException>(() => new WorkspaceClient(http).Session());
        Assert.Equal((status, code, title), (error.Status, error.Code, error.Message));

        using var context = new BunitContext();
        var alert = context.Render<ApiError>(parameters => parameters.Add(component => component.Error, error));
        if (status == HttpStatusCode.Unauthorized)
            Assert.Contains("Sign in again", alert.Markup);
        else
            Assert.Contains(title, alert.Markup);
    }

    private sealed class RefusalHandler(HttpStatusCode status, string code, string title) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent($$"""{"type":"https://tools.ietf.org/html/rfc9110","title":"{{title}}","status":{{(int)status}},"code":"{{code}}","traceId":"00-x"}""",
                    System.Text.Encoding.UTF8, "application/problem+json")
            });
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<object>> respond) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(await respond(request, token)) };
    }

    private sealed class SaveHandler(HttpStatusCode firstStatus, RuntimeContribution current) : HttpMessageHandler
    {
        public List<EditContributionRequest> Submissions { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Submissions.Add((await request.Content!.ReadFromJsonAsync<EditContributionRequest>(token))!);
            return Submissions.Count == 1
                ? new HttpResponseMessage(firstStatus) { Content = JsonContent.Create(new { code = "conflict", current }) }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new ContributionResult(current.Id, current.Version + 1, 1))
                };
        }
    }
}
