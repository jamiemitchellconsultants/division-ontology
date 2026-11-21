using System.Net;
using RuntimeKnowledge.Contracts;
using Xunit;

namespace RuntimeKnowledge.Tests;

public sealed class WorkspaceScenarioTests
{
    [Fact]
    public async Task PublishedAndRuntimeKnowledgeSurviveFeederChangesAndRebuild()
    {
        await using var s = await ScenarioRunner.OpenAsync();
        var first = await s.ActivateAsync("initial");
        var inventory = await s.Read<InventoryPage>("/api/inventory");
        Assert.Equal(first, inventory.SnapshotId);
        Assert.Equal(2, inventory.Entities.Where(entity => entity.Label == "Order").Select(entity => entity.Key).Distinct().Count());
        Assert.Empty((await s.Traverse(s.Isolated)).Paths);
        Assert.NotEqual(s.A.SourceId, s.B.SourceId);

        var request = new CreateRelationship(s.A, s.B, RelationshipMeaning.Custom("triggers", "Warehouse picks storefront orders."));
        await s.Send<ContributionResult>(HttpMethod.Post, "/api/contributions/relationships", request, HttpStatusCode.Forbidden, reader: true);
        var edge = (await s.Send<ContributionResult>(HttpMethod.Post, "/api/contributions/relationships", request, HttpStatusCode.Created))!;
        Assert.Contains((await s.Traverse(s.A)).Paths.SelectMany(path => path.Steps), step =>
            step.RuntimeId == edge.Id && step.Origin == "runtime" && step.Contribution?.Relationship?.Meaning.Explanation == request.Meaning.Explanation);

        var note = (await s.Send<ContributionResult>(HttpMethod.Post, "/api/contributions/annotations",
            new CreateAnnotation(s.A, "businessRbac", "Finance approvers reconcile orders."), HttpStatusCode.Created))!;
        await s.IndexAsync();
        var matches = await s.Query<SearchResult>("/api/search", new SearchRequest("Who reconciles orders?"));
        Assert.Contains(matches.Hits, hit => hit.AnnotationId == note.Id && hit.Provenance.SnapshotId == first);

        var edit = new EditContributionRequest(note.Version, "businessRbac", "Warehouse leads reconcile orders.");
        var saved = (await s.Send<ContributionResult>(HttpMethod.Put, $"/api/contributions/{note.Id:D}", edit))!;
        await s.Send<ContributionResult>(HttpMethod.Put, $"/api/contributions/{note.Id:D}", edit with
        {
            Text = "Stale draft"
        }, HttpStatusCode.Conflict);
        var pending = await s.Query<SearchResult>("/api/search", new SearchRequest("Who reconciles orders?"));
        Assert.True(pending.IndexPending);
        Assert.DoesNotContain(pending.Hits, hit => hit.AnnotationId == note.Id);
        await s.IndexAsync();
        var indexed = await s.Query<SearchResult>("/api/search", new SearchRequest("Who reconciles orders?"));
        Assert.Contains(indexed.Hits, hit => hit.AnnotationId == note.Id && hit.Annotation!.Version == saved.Version);

        await s.Send<ContributionResult>(HttpMethod.Post, $"/api/contributions/{edge.Id:D}/codifications",
            new CodificationRequest(edge.Version, s.PublishedEdge));
        var codification = await s.Query<RelationshipDetail>("/api/relationships/detail", new RelationshipDetailRequest(s.PublishedEdge));
        Assert.Contains(codification.Relationship.CodifyingContributions!, contribution =>
            contribution.Id == edge.Id && contribution.Associations.Single().PublishedProvenance.Evidence.Count > 0);
        Assert.Equal("direct", codification.Relationship.Provenance!.Basis);
        await s.Send<ContributionResult>(HttpMethod.Post, $"/api/contributions/{note.Id:D}/remove",
            new RemoveContributionRequest(saved.Version));

        var changed = await s.ActivateAsync("changed");
        var missing = await s.ActivateAsync("missing");
        var current = await s.ReplayAndSwitchAsync();
        Assert.Equal(missing, current.SnapshotId);
        var ordinary = await s.Traverse(s.A);
        Assert.DoesNotContain(ordinary.Paths.SelectMany(path => path.Steps), step => step.RuntimeId == edge.Id);
        Assert.Contains(ordinary.Gaps, gap => gap.SourceId == s.B.SourceId);
        var expanded = await s.Traverse(s.A, includeUnavailable: true);
        Assert.Contains(expanded.Paths.SelectMany(path => path.Steps), step =>
            step.RuntimeId == edge.Id && step.LastKnownSnapshotId == changed && step.ToEndpoint?.Available == false);
        var history = await s.Read<HistoryPage>($"/api/history?contributionId={note.Id:D}");
        Assert.Equal(3, history.Entries.Count);
        Assert.Contains(history.Entries, entry => entry.After.Removed && entry.Before?.Annotation?.Text == edit.Text);
        Assert.All(history.Entries, entry => Assert.Equal("fixture-user", entry.Actor.Subject));

        await s.ActivateAsync("returned");
        Assert.Contains((await s.Traverse(s.A)).Paths.SelectMany(path => path.Steps), step =>
            step.RuntimeId == edge.Id && !step.EndpointUnavailable && step.Contribution!.Associations.Count == 1);
    }

    private const string RelBase = "urn:jamiemitchellconsultants:division-ontology:link-relations:";

    private static string NextHref(string links)
    {
        var match = System.Text.RegularExpressions.Regex.Match(links, "<(?<href>[^>]+)>; rel=\"next\"");
        Assert.True(match.Success, $"no next link in {links}");
        return match.Groups["href"].Value;
    }

    [Fact]
    public async Task RuntimeResponsesCarryTheSpecLinks()
    {
        await using var s = await ScenarioRunner.OpenAsync();
        await s.ActivateAsync("initial");

        var (_, inventoryLinks) = await s.ReadWithLinks<InventoryPage>("/api/inventory?limit=1");
        Assert.Contains($"</api/search>; rel=\"{RelBase}search\"", inventoryLinks);
        var inventoryNext = NextHref(inventoryLinks);
        Assert.Contains("limit=1", inventoryNext);
        var (secondInventoryPage, _) = await s.ReadWithLinks<InventoryPage>(inventoryNext);
        Assert.Equal(1, secondInventoryPage.Entities.Count + secondInventoryPage.RelationshipTypes.Count +
            secondInventoryPage.Relationships.Count);

        var (_, entityLinks) = await s.SendWithLinks<EntityDetail>(HttpMethod.Post, "/api/entities/detail",
            new EntityDetailRequest(s.A), reader: true);
        Assert.Contains($"</api/traverse>; rel=\"{RelBase}traverse\"", entityLinks);
        Assert.Contains($"</api/contributions/annotations>; rel=\"{RelBase}createAnnotation\"", entityLinks);

        var (_, searchLinks) = await s.SendWithLinks<SearchResult>(HttpMethod.Post, "/api/search",
            new SearchRequest("Order"), reader: true);
        Assert.Contains($"</api/entities/detail>; rel=\"{RelBase}entityDetail\"", searchLinks);

        var (_, traverseLinks) = await s.SendWithLinks<TraversalResult>(HttpMethod.Post, "/api/traverse",
            new TraversalRequest(s.A), reader: true);
        Assert.Contains($"</api/entities/detail>; rel=\"{RelBase}entityDetail\"", traverseLinks);
        Assert.Contains($"</api/relationships/detail>; rel=\"{RelBase}relationshipDetail\"", traverseLinks);

        var (_, relationshipLinks) = await s.SendWithLinks<RelationshipDetail>(HttpMethod.Post, "/api/relationships/detail",
            new RelationshipDetailRequest(s.PublishedEdge), reader: true);
        Assert.Contains($"</api/entities/detail>; rel=\"{RelBase}entityDetail\"", relationshipLinks);

        var (note, _) = await s.SendWithLinks<ContributionResult>(HttpMethod.Post, "/api/contributions/annotations",
            new CreateAnnotation(s.A, "businessRbac", "Finance approvers reconcile orders."), HttpStatusCode.Created);
        var (editedNote, noteEditLinks) = await s.SendWithLinks<ContributionResult>(HttpMethod.Put,
            $"/api/contributions/{note!.Id:D}", new EditContributionRequest(note.Version, "businessRbac", "Warehouse leads reconcile orders."));
        Assert.Contains($"</api/contributions/{note.Id:D}>; rel=\"{RelBase}edit\"", noteEditLinks);
        Assert.DoesNotContain("codifications", noteEditLinks);

        var (edge, edgeLinks) = await s.SendWithLinks<ContributionResult>(HttpMethod.Post, "/api/contributions/relationships",
            new CreateRelationship(s.A, s.B, RelationshipMeaning.Custom("triggers", "Warehouse picks storefront orders.")),
            HttpStatusCode.Created);
        Assert.Contains($"</api/history?contributionId={edge!.Id:D}>; rel=\"{RelBase}history\"", edgeLinks);
        Assert.Contains($"</api/contributions/{edge.Id:D}>; rel=\"{RelBase}edit\"", edgeLinks);
        Assert.Contains($"</api/contributions/{edge.Id:D}/codifications>; rel=\"{RelBase}codifications\"", edgeLinks);

        var (editedEdge, edgeEditLinks) = await s.SendWithLinks<ContributionResult>(HttpMethod.Put,
            $"/api/contributions/{edge.Id:D}", new EditContributionRequest(edge.Version, From: s.A, To: s.B,
                Meaning: RelationshipMeaning.Custom("triggers", "Warehouse picks storefront orders promptly.")));
        Assert.Contains($"</api/contributions/{edge.Id:D}/codifications>; rel=\"{RelBase}codifications\"", edgeEditLinks);

        var (_, codifyLinks) = await s.SendWithLinks<ContributionResult>(HttpMethod.Post,
            $"/api/contributions/{edge.Id:D}/codifications", new CodificationRequest(editedEdge!.Version, s.PublishedEdge));
        Assert.Contains($"</api/history?contributionId={edge.Id:D}>; rel=\"{RelBase}history\"", codifyLinks);
        Assert.Contains($"</api/contributions/{edge.Id:D}>; rel=\"{RelBase}edit\"", codifyLinks);
        Assert.Contains($"</api/contributions/{edge.Id:D}/remove>; rel=\"{RelBase}remove\"", codifyLinks);
        Assert.Contains($"</api/contributions/{edge.Id:D}/codifications>; rel=\"{RelBase}codifications\"", codifyLinks);

        var (firstFiltered, filteredLinks) = await s.ReadWithLinks<HistoryPage>(
            $"/api/history?contributionId={note.Id:D}&limit=1");
        Assert.Single(firstFiltered.Entries);
        Assert.DoesNotContain(RelBase + "edit", filteredLinks);
        var filteredNext = NextHref(filteredLinks);
        Assert.Contains($"contributionId={note.Id:D}", filteredNext);
        Assert.Contains("limit=1", filteredNext);
        var (secondFiltered, _) = await s.ReadWithLinks<HistoryPage>(filteredNext);
        Assert.Single(secondFiltered.Entries);
        Assert.All(secondFiltered.Entries, entry => Assert.Equal(note.Id, entry.ContributionId));
        Assert.True(secondFiltered.Entries[0].Position > firstFiltered.Entries[0].Position);

        var (removed, removeLinks) = await s.SendWithLinks<ContributionResult>(HttpMethod.Post,
            $"/api/contributions/{edge.Id:D}/remove", new RemoveContributionRequest(editedEdge.Version + 1));
        Assert.Equal($"</api/history?contributionId={removed!.Id:D}>; rel=\"{RelBase}history\"", removeLinks);
    }
}
