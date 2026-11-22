using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Web.Pages;
using RuntimeKnowledge.Web.Services;
using Xunit;
using static RuntimeKnowledge.Tests.GraphExplorerNavigationTests;

namespace RuntimeKnowledge.Tests;

public sealed class GraphExplorerContributionTests
{
    private static readonly PublishedKey Commerce = new("fixture/ecommerce", "concept", "https://example.test/ecommerce#Order");
    private static readonly PublishedKey Warehouse = new("fixture/warehouse", "concept", "https://example.test/warehouse#Order");
    private static readonly Guid NoteId = Guid.Parse("2d9e15cf-fc79-4bc6-98e3-4def28e95432");
    private static readonly Guid EdgeId = Guid.Parse("9a10cf30-69de-4ade-8ad3-b28b2785e083");
    private static readonly Actor Manager = new("issuer", "fixture-manager");

    [Fact]
    public void ReaderHasNoContributionControls()
    {
        var reader = new WriteHandler { CanManage = false };
        using var readerContext = GraphTestContext.Create(reader);
        var view = Render(readerContext, action: "edit-relationship", id: EdgeId);
        view.WaitForElement("button[data-graph-inspect-focus]").Click();
        view.WaitForAssertion(() =>
        {
            Assert.Contains("Annotations", view.Markup);
            Assert.Empty(view.FindAll("[data-graph-editor]"));
            Assert.Empty(reader.RelationshipReads);
            Assert.Empty(reader.Writes);
        });

        var manager = new WriteHandler();
        using var managerContext = GraphTestContext.Create(manager);
        var editable = Render(managerContext);
        editable.WaitForElement("button[data-graph-inspect-focus]").Click();
        Assert.Equal(4, editable.FindAll("button[data-graph-editor]").Count);
        editable.Find($"button[data-graph-inspect-edge='r:{EdgeId:D}']").Click();
        Assert.Equal(2, editable.FindAll("button[data-graph-editor]").Count);
    }

    [Fact]
    public void CreatesANoteAndDirectedRelationshipFromTheGraph()
    {
        var api = new WriteHandler { HideEdgeAfterWrite = true };
        using var context = GraphTestContext.Create(api);
        var graph = Render(context);
        Open(graph, "add-note");
        graph.WaitForElement("form textarea").Change("Check receiving stock.");
        graph.Find("form").Submit();
        graph.WaitForElement("button[data-graph-saved-record]").Click();
        Assert.Equal(new CreateAnnotation(Commerce, "businessExplanation", "Check receiving stock."), Assert.Single(api.Writes).Body);
        graph.WaitForAssertion(() => Assert.Contains("Check receiving stock.", graph.Markup));

        Open(graph, "add-relationship");
        var choices = graph.WaitForElements("form select", 3);
        choices[0].Change(Warehouse.StorageKey);
        choices[1].Change(Commerce.StorageKey);
        graph.Find("form input").Change("hands off");
        graph.Find("form textarea").Change("Confirmed direction from Warehouse to Ecommerce.");
        graph.Find("form").Submit();
        graph.WaitForElement("button[data-graph-saved-record]").Click();
        graph.WaitForAssertion(() =>
        {
            Assert.Equal(new CreateRelationship(Warehouse, Commerce,
                RelationshipMeaning.Custom("hands off", "Confirmed direction from Warehouse to Ecommerce.")), api.Writes.Last().Body);
            Assert.DoesNotContain(graph.FindAll("li[data-graph-edge]"), row => row.GetAttribute("data-graph-edge") == $"r:{EdgeId:D}");
            Assert.Equal(new RelationshipDetailRequest(RuntimeId: EdgeId), api.RelationshipReads.Last());
            Assert.Contains("Confirmed direction from Warehouse to Ecommerce.", graph.Markup);
        });
    }

    [Fact]
    public async Task SavedRelationshipStaysOpenWhenThePendingReloadFinishes()
    {
        var reload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new WriteHandler { HideEdgeAfterWrite = true, HoldReload = reload.Task };
        using var context = GraphTestContext.Create(api);
        var graph = Render(context);
        Open(graph, "add-relationship");
        graph.WaitForElements("form select", 3)[1].Change(Warehouse.StorageKey);
        graph.Find("form input").Change("Recent handoff");
        graph.Find("form textarea").Change("Confirm stock before dispatch.");
        var saving = graph.Find("form").SubmitAsync(new());
        try
        {
            graph.WaitForAssertion(() => Assert.Equal(2, api.Traversals.Count));
            graph.WaitForElement("button[data-graph-saved-record]").Click();
            Assert.Contains("Recent handoff", graph.Find("section[aria-label='Graph details']").TextContent);
        }
        finally
        {
            reload.SetResult();
            await saving;
        }
        graph.WaitForAssertion(() =>
        {
            var inspector = graph.FindComponent<RuntimeKnowledge.Web.Components.GraphInspector>();
            Assert.Equal($"r:{EdgeId:D}", inspector.Instance.SelectedId);
            Assert.Contains("Recent handoff", inspector.Markup);
            Assert.DoesNotContain("out of date", graph.Markup);
        });
    }

    [Fact]
    public void SuccessfulWritesRefreshFromPageOneWithoutRepeatingTheWrite()
    {
        var api = new WriteHandler { FailRefreshAfterWrite = true };
        using var context = GraphTestContext.Create(api);
        var graph = Render(context);
        graph.WaitForElement("select[aria-label=Direction]").Change("incoming");
        Open(graph, "add-note");
        graph.WaitForElement("form textarea").Change("Saved despite a failed read.");
        graph.Find("form").Submit();
        graph.WaitForAssertion(() =>
        {
            Assert.Contains("Change saved.", graph.Markup);
            Assert.Contains("refreshUnavailable", graph.Markup);
            Assert.Equal(new TraversalRequest(Commerce, "incoming", 1, false), api.Traversals.Last());
            Assert.Single(api.Writes);
        });
        graph.Find("button[data-graph-retry]").Click();
        graph.WaitForAssertion(() => Assert.DoesNotContain("refreshUnavailable", graph.Markup));
        Assert.Single(api.Writes);

        graph.Find($"button[data-graph-inspect-edge='r:{EdgeId:D}']").Click();
        graph.WaitForElement("button[data-graph-editor='edit-relationship']").Click();
        graph.WaitForElement("form textarea").Change("Edited reasoning.");
        graph.Find("form").Submit();
        graph.WaitForAssertion(() =>
        {
            var edit = api.Writes.Last();
            Assert.Equal(EdgeId, edit.Id);
            Assert.Equal(new EditContributionRequest(1, From: Warehouse, To: Commerce,
                Meaning: RelationshipMeaning.Custom("hands off", "Edited reasoning.")), edit.Body);
            Assert.Equal(new TraversalRequest(Commerce, "incoming", 1, false), api.Traversals.Last());
        });

        graph.Find($"button[data-graph-inspect-edge='r:{EdgeId:D}']").Click();
        graph.WaitForElement("button[data-graph-editor='remove-relationship']").Click();
        graph.WaitForElement("form").Submit();
        graph.WaitForAssertion(() =>
        {
            Assert.Equal((EdgeId, (object)new RemoveContributionRequest(2)), api.Writes.Last());
            Assert.Equal(new TraversalRequest(Commerce, "incoming", 1, false), api.Traversals.Last());
            Assert.NotNull(graph.Find($"[data-graph-save-status] a[href='history/{EdgeId:D}']"));
            Assert.DoesNotContain(graph.FindAll("li[data-graph-edge]"), row => row.GetAttribute("data-graph-edge") == $"r:{EdgeId:D}");
        });
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void RejectedSaveKeepsDraftAndVersionConflictRequiresReview(HttpStatusCode status)
    {
        var api = new WriteHandler { WriteFailure = status };
        using var context = GraphTestContext.Create(api);
        var graph = Render(context);
        Open(graph, "edit-note");
        graph.WaitForElement("form textarea").Change("Keep my draft text.");
        graph.Find("form").Submit();
        graph.WaitForAssertion(() =>
        {
            Assert.Equal("Keep my draft text.", graph.Find("form textarea").GetAttribute("value"));
            Assert.Single(api.Writes);
            Assert.Single(api.Traversals);
            Assert.Empty(graph.FindAll("[data-graph-save-status]"));
            Assert.Contains("Keep my draft text.", Assert.IsType<string>(
                context.JSInterop.Invocations["sessionStorage.setItem"].Single().Arguments[1]));
        });
        if (status != HttpStatusCode.Conflict) return;
        Assert.True(graph.Find("button[type=submit]").HasAttribute("disabled"));
        api.WriteFailure = null;
        graph.Find("button[data-review]").Click();
        graph.Find("form").Submit();
        graph.WaitForAssertion(() => Assert.Equal(new EditContributionRequest(2, "businessExplanation", "Keep my draft text."), api.Writes.Last().Body));
    }

    [Fact]
    public void SignInResumeRestoresTheInterruptedEditor()
    {
        var expired = new WriteHandler { WriteFailure = HttpStatusCode.Unauthorized };
        using var context = GraphTestContext.Create(expired);
        var graph = Render(context);
        graph.WaitForElement("button[data-graph-follow]").Click();
        graph.WaitForAssertion(() => Assert.Equal(Warehouse, expired.Traversals.Last().Start));
        graph.WaitForElement("select[aria-label=Direction]").Change("outgoing");
        graph.Find($"button[data-graph-inspect-edge='r:{EdgeId:D}']").Click();
        graph.WaitForElement("button[data-graph-editor='edit-relationship']").Click();
        graph.WaitForElement("form textarea").Change("Retain this unsaved relationship.");
        graph.Find("form").Submit();
        graph.WaitForAssertion(() => Assert.Contains("Sign in again", graph.Markup));
        graph.FindAll("form button").Single(button => button.TextContent.Trim() == "Sign in again").Click();
        var navigation = Assert.IsType<BunitNavigationManager>(context.Services.GetRequiredService<NavigationManager>());
        var state = navigation.History.First().StateFromJson<JsonElement>();
        var expected = $"graph/{WorkspaceLinks.Encode(Warehouse)}?direction=outgoing&editor=edit-relationship&contributionId={EdgeId:D}";
        var returnUrl = state.GetProperty("returnUrl").GetString();
        Assert.Equal(new Uri(new Uri(navigation.BaseUri), expected).AbsoluteUri, returnUrl);
        Assert.DoesNotContain("Retain this", returnUrl);

        var draft = Assert.IsType<string>(context.JSInterop.Invocations["sessionStorage.setItem"].Last().Arguments[1]);
        var reauthenticated = new WriteHandler { HideEdgeAfterWrite = true, EdgeOffFirstPage = true };
        using var resumedContext = GraphTestContext.Create(reauthenticated);
        resumedContext.JSInterop.Setup<string?>("sessionStorage.getItem", _ => true).SetResult(draft);
        var resumed = Render(resumedContext, Warehouse, "outgoing", "edit-relationship", EdgeId);
        resumed.WaitForAssertion(() =>
        {
            Assert.Equal("Retain this unsaved relationship.", resumed.Find("form textarea").GetAttribute("value"));
            Assert.Equal(new RelationshipDetailRequest(RuntimeId: EdgeId), Assert.Single(reauthenticated.RelationshipReads));
            Assert.Equal(new TraversalRequest(Warehouse, "outgoing", 1, false), Assert.Single(reauthenticated.Traversals));
            Assert.Empty(reauthenticated.Writes);
        });

        var missing = new WriteHandler { EdgeRemoved = true };
        using var missingContext = GraphTestContext.Create(missing);
        missingContext.JSInterop.Setup<string?>("sessionStorage.getItem", _ => true).SetResult(draft);
        var unavailable = Render(missingContext, Warehouse, "outgoing", "edit-relationship", EdgeId);
        unavailable.WaitForAssertion(() => Assert.Contains("Your tab draft is still available", unavailable.Markup));
        Assert.Empty(unavailable.FindAll("form"));
        Assert.Empty(missing.Writes);
        Assert.DoesNotContain(missingContext.JSInterop.Invocations, item => item.Identifier == "sessionStorage.setItem");
    }

    private static void Open(IRenderedComponent<GraphExplorer> graph, string action)
    {
        graph.WaitForElement("button[data-graph-inspect-focus]").Click();
        graph.WaitForElement($"button[data-graph-editor='{action}']").Click();
    }

    private static IRenderedComponent<GraphExplorer> Render(BunitContext context, PublishedKey? focus = null,
        string? direction = null, string? action = null, Guid? id = null)
    {
        var key = focus ?? Commerce;
        if (action is not null)
        {
            var navigation = context.Services.GetRequiredService<NavigationManager>();
            navigation.NavigateTo(WorkspaceLinks.GraphResume(key, direction ?? "both", action, id));
        }
        return context.Render<GraphExplorer>(args => args.Add(page => page.Key, WorkspaceLinks.Encode(key)));
    }

    private static PublishedEntity EntityFor(PublishedKey key) => new(key, "Order", null, [], Origin(key));
    private static Provenance Origin(PublishedKey key) => new(key, "commit", "snapshot", null, []);
    private static RuntimeContribution Note(string text, int version = 1) =>
        new(NoteId, version, "annotation", Manager, DateTimeOffset.UnixEpoch, "snapshot", false,
            new AnnotationData(Commerce, "businessExplanation", text, Origin(Commerce)), null, []);
    private static RuntimeContribution Edge(CreateRelationship value, int version = 1) =>
        new(EdgeId, version, "relationship", Manager, DateTimeOffset.UnixEpoch, "snapshot", false, null,
            new(value.From, value.To, value.Meaning, value.Meaning.CustomLabel ?? "hands off",
                Origin(value.From), Origin(value.To), null), []);

    private sealed class WriteHandler : HttpMessageHandler
    {
        private RuntimeContribution note = Note("Original note.");
        private RuntimeContribution edge = Edge(new(Warehouse, Commerce, RelationshipMeaning.Custom("hands off", "Original reason.")));
        private bool failRead;
        public bool CanManage { get; init; } = true;
        public bool HideEdgeAfterWrite { get; init; }
        public bool EdgeOffFirstPage { get; init; }
        public bool EdgeRemoved { get; set; }
        public bool FailRefreshAfterWrite { get; set; }
        public Task? HoldReload { get; init; }
        public HttpStatusCode? WriteFailure { get; set; }
        public ConcurrentQueue<TraversalRequest> Traversals { get; } = new();
        public ConcurrentQueue<RelationshipDetailRequest> RelationshipReads { get; } = new();
        public ConcurrentQueue<(Guid Id, object Body)> Writes { get; } = new();

        private TraversalStep EdgeStep => new(null, EdgeId, edge.Relationship!.From, edge.Relationship.To,
            edge.Relationship.Label, "outgoing", "runtime", null, Contribution: edge);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            async Task<T> Read<T>() => (await request.Content!.ReadFromJsonAsync<T>(WorkspaceJson.Options, token))!;
            switch (request.RequestUri?.AbsolutePath)
            {
                case "/api/session":
                    return Json(new WorkspaceSession(true, CanManage));
                case "/api/inventory":
                    return Json(new InventoryPage("snapshot", [EntityFor(Commerce), EntityFor(Warehouse)], [], [], null, []));
                case "/api/traverse":
                    var query = await Read<TraversalRequest>();
                    Traversals.Enqueue(query);
                    if (!Writes.IsEmpty && HoldReload is not null) await HoldReload.WaitAsync(token);
                    if (failRead)
                    {
                        failRead = false;
                        return Problem(HttpStatusCode.ServiceUnavailable, "refreshUnavailable");
                    }
                    return Json(new TraversalResult("snapshot",
                        EdgeRemoved || EdgeOffFirstPage || (HideEdgeAfterWrite && Writes.Any(write => write.Body is CreateRelationship))
                            ? [] : [new([EdgeStep])], [], false,
                        [new(Commerce, "Order", Origin(Commerce), true, []), new(Warehouse, "Order", Origin(Warehouse), true, [])]));
                case "/api/entities/detail":
                    var key = (await Read<EntityDetailRequest>()).Key;
                    return Json(new EntityDetail("snapshot", EntityFor(key), true, key == Commerce ? [note] : []));
                case "/api/relationships/detail":
                    var relationship = await Read<RelationshipDetailRequest>();
                    RelationshipReads.Enqueue(relationship);
                    return EdgeRemoved ? Problem(HttpStatusCode.NotFound, "relationshipNotFound")
                        : Json(new RelationshipDetail("snapshot", EdgeStep));
                case "/api/contributions/annotations":
                    var annotation = await Read<CreateAnnotation>();
                    Writes.Enqueue((NoteId, annotation));
                    if (Failure(NoteId) is { } noteFailure) return noteFailure;
                    note = Note(annotation.Text);
                    return Saved(NoteId, note.Version);
                case "/api/contributions/relationships":
                    var create = await Read<CreateRelationship>();
                    Writes.Enqueue((EdgeId, create));
                    if (Failure(EdgeId) is { } edgeFailure) return edgeFailure;
                    edge = Edge(create);
                    return Saved(EdgeId, edge.Version);
                case { } path when path == $"/api/contributions/{EdgeId:D}/remove":
                    Writes.Enqueue((EdgeId, await Read<RemoveContributionRequest>()));
                    if (Failure(EdgeId) is { } removalFailure) return removalFailure;
                    EdgeRemoved = true;
                    return Saved(EdgeId, edge.Version + 1);
                case { } path when path == $"/api/contributions/{NoteId:D}" || path == $"/api/contributions/{EdgeId:D}":
                    var id = Guid.Parse(path.Split('/')[^1]);
                    var edit = await Read<EditContributionRequest>();
                    Writes.Enqueue((id, edit));
                    if (Failure(id) is { } editFailure) return editFailure;
                    var version = edit.ExpectedVersion + 1;
                    if (id == NoteId) note = Note(edit.Text!, version);
                    else edge = Edge(new(edit.From!, edit.To!, edit.Meaning!), version);
                    return Saved(id, version);
                default:
                    throw new InvalidOperationException($"Unexpected request for {request.RequestUri?.AbsolutePath}");
            }
        }

        private HttpResponseMessage? Failure(Guid id) => WriteFailure switch
        {
            HttpStatusCode.Conflict => Json(new { code = "versionConflict", detail = "Review the current revision.",
                current = id == NoteId ? Note("Another edit.", 2) : edge with { Version = 2 } }, HttpStatusCode.Conflict),
            { } status => Problem(status, "writeRejected"),
            _ => null
        };

        private HttpResponseMessage Saved(Guid id, int version)
        {
            failRead = FailRefreshAfterWrite;
            FailRefreshAfterWrite = false;
            return Json(new ContributionResult(id, version, Writes.Count));
        }
    }
}
