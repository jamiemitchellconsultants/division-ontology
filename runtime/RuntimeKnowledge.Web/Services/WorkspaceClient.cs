using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using RuntimeKnowledge.Contracts;

namespace RuntimeKnowledge.Web.Services;

public sealed class WorkspaceClient(HttpClient http) : IDisposable
{
    public Task<WorkspaceSession> Session(CancellationToken cancellationToken = default) =>
        Send<WorkspaceSession>(HttpMethod.Get, "api/session", cancellationToken: cancellationToken);
    public Task<InventoryPage> Inventory(string? cursor = null) =>
        Get<InventoryPage>(cursor is null ? "api/inventory" : $"api/inventory?cursor={Uri.EscapeDataString(cursor)}");
    public Task<EntityDetail> Entity(PublishedKey key, CancellationToken cancellationToken = default) =>
        Post<EntityDetail>("api/entities/detail", new EntityDetailRequest(key), cancellationToken);
    public Task<RelationshipDetail> Relationship(RelationshipDetailRequest request, CancellationToken cancellationToken = default) =>
        Post<RelationshipDetail>("api/relationships/detail", request, cancellationToken);
    public Task<TraversalResult> Traverse(TraversalRequest request, CancellationToken cancellationToken = default) =>
        Post<TraversalResult>("api/traverse", request, cancellationToken);
    public Task<SearchResult> Search(string query) => Post<SearchResult>("api/search", new SearchRequest(query));
    public Task<ImpactSearchResult> ImpactSearch(ImpactSearchRequest request) => Post<ImpactSearchResult>("api/impact/search", request);
    public Task<ImpactPathsResult> ImpactPaths(ImpactPathsRequest request) => Post<ImpactPathsResult>("api/impact/paths", request);
    public Task<C4ViewResult> ImpactView(ImpactViewRequest request) => Post<C4ViewResult>("api/impact/views", request);
    public Task<AssessmentPreview> PreviewAssessment(AssessmentDraftRequest request) => Post<AssessmentPreview>("api/assessments/preview", request);
    public Task<AssessmentSaved> SaveAssessment(AssessmentDraftRequest request) => Post<AssessmentSaved>("api/assessments", request);
    public Task<SavedAssessment> GetAssessment(Guid id) => Get<SavedAssessment>($"api/assessments/{id:D}");
    public Task<AssessmentPage> ListAssessments(string? cursor = null) =>
        Get<AssessmentPage>(cursor is null ? "api/assessments" : $"api/assessments?cursor={Uri.EscapeDataString(cursor)}");
    public Task<AssessmentSaved> EditAssessment(Guid id, AssessmentEditRequest request) =>
        Send<AssessmentSaved>(HttpMethod.Put, $"api/assessments/{id:D}", request);
    public Task<HistoryPage> History(Guid? id, string? cursor = null)
    {
        var filters = new List<string>();
        if (id is not null) filters.Add($"contributionId={id:D}");
        if (cursor is not null) filters.Add($"cursor={Uri.EscapeDataString(cursor)}");
        return Get<HistoryPage>(filters.Count == 0 ? "api/history" : $"api/history?{string.Join('&', filters)}");
    }
    public Task<ContributionResult> Annotate(CreateAnnotation request) =>
        Post<ContributionResult>("api/contributions/annotations", request);
    public Task<ContributionResult> Connect(CreateRelationship request) =>
        Post<ContributionResult>("api/contributions/relationships", request);
    public Task<ContributionResult> Edit(Guid id, EditContributionRequest request) =>
        Send<ContributionResult>(HttpMethod.Put, $"api/contributions/{id:D}", request);
    public Task<ContributionResult> Remove(Guid id, int version) =>
        Post<ContributionResult>($"api/contributions/{id:D}/remove", new RemoveContributionRequest(version));
    public Task<ContributionResult> Associate(Guid id, int version, PublishedKey key) =>
        Post<ContributionResult>($"api/contributions/{id:D}/codifications", new CodificationRequest(version, key));

    public void Dispose() => http.Dispose();

    private Task<T> Get<T>(string path) => Send<T>(HttpMethod.Get, path);
    private Task<T> Post<T>(string path, object body, CancellationToken cancellationToken = default) =>
        Send<T>(HttpMethod.Post, path, body, cancellationToken);

    private async Task<T> Send<T>(HttpMethod method, string path, object? body = null, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: WorkspaceJson.Options);
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
                return (await response.Content.ReadFromJsonAsync<T>(WorkspaceJson.Options, cancellationToken))!;
            Problem? problem = null;
            try
            {
                problem = await response.Content.ReadFromJsonAsync<Problem>(WorkspaceJson.Options, cancellationToken);
            }
            catch (Exception error) when (error is JsonException or NotSupportedException)
            {
                // Authentication challenges and gateway failures can have no JSON body.
            }

            throw new WorkspaceApiException(response.StatusCode, problem?.Code, problem?.Detail ?? problem?.Title, problem?.Current);
        }
        catch (AccessTokenNotAvailableException)
        {
            throw new WorkspaceApiException(HttpStatusCode.Unauthorized);
        }
        catch (HttpRequestException)
        {
            throw new WorkspaceApiException(HttpStatusCode.ServiceUnavailable, detail: "The workspace could not be reached.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new WorkspaceApiException(HttpStatusCode.ServiceUnavailable, detail: "The workspace request timed out.");
        }
    }

    private sealed record Problem(string? Code, string? Detail, string? Title, RuntimeContribution? Current);
}

public sealed class WorkspaceApiException(HttpStatusCode status, string? code = null, string? detail = null,
    RuntimeContribution? current = null) : Exception(detail ?? $"Workspace request failed ({(int)status}).")
{
    public HttpStatusCode Status { get; } = status;
    public string? Code { get; } = code;
    public RuntimeContribution? Current { get; } = current;
}
