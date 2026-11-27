using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.OpenApi;
using RuntimeKnowledge.Contracts;

namespace RuntimeKnowledge.Api;

/// <summary>How the workspace API describes itself (ADR 0013, ADR 0016).</summary>
internal static class WorkspaceOpenApi
{
    public const string DocumentName = "v1";
    public const string RoleExtension = "x-workspace-role";
    public const string CodesExtension = "x-workspace-codes";
    public const string ToolNameExtension = "x-agent-tool-name";
    public const string IdempotentExtension = "x-agent-idempotent";
    public const string RetryableExtension = "x-agent-retryable";
    public const string SideEffectsExtension = "x-agent-side-effects";
    public const string InputExtension = "x-agent-input";
    public const string OutputExtension = "x-agent-output";
    public const string Bearer = "bearer";
    private const string ProblemJson = "application/problem+json";

    public static void AddWorkspaceOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(DocumentName, options =>
        {
            options.AddOperationTransformer(OperationDescriptions);
            options.AddDocumentTransformer(DocumentDescriptions);
            options.AddDocumentTransformer<DocsSignIn>();
            options.AddDocumentTransformer(StableOrder); // must stay last
        });

    /// <summary>The role an operation needs, or null when it is anonymous.</summary>
    public static string? RoleFor(IList<object> endpointMetadata)
    {
        if (endpointMetadata.OfType<IAllowAnonymous>().Any()) return null;
        var policies = endpointMetadata.OfType<IAuthorizeData>().Select(data => data.Policy).ToArray();
        if (policies.Contains(Identity.ManagePolicy)) return "ManageKnowledgeGraph";
        if (policies.Contains(Identity.ReadPolicy)) return "ReadKnowledgeGraph";
        return null;
    }

    private static Task OperationDescriptions(OpenApiOperation operation, OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var document = context.Document;
        var role = RoleFor(context.Description.ActionDescriptor.EndpointMetadata);
        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        operation.Extensions[RoleExtension] = new JsonNodeExtension(JsonValue.Create(role ?? "none"));
        operation.Responses ??= new OpenApiResponses();
        if (role is null)
        {
            operation.Security = [];
        }
        else
        {
            operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(Bearer, document)] = [] }];
            operation.Responses["401"] = new OpenApiResponseReference("unauthorized", document);
            operation.Responses["403"] = new OpenApiResponseReference("forbidden", document);
        }
        operation.Responses["500"] = new OpenApiResponseReference("internalError", document);

        AgentDecorations(operation, context);

        var codes = context.Description.ActionDescriptor.EndpointMetadata.OfType<WorkspaceErrorCodes>().SelectMany(c => c.Codes);
        foreach (var group in codes.Distinct().GroupBy(ProblemResponses.StatusFor))
        {
            var sorted = group.Order(StringComparer.Ordinal).ToArray();
            operation.Responses[group.Key.ToString()] = new OpenApiResponse
            {
                Description = $"{ReasonPhrases.GetReasonPhrase(group.Key)}. Codes: {string.Join(", ", sorted)}",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    [ProblemJson] = new() { Schema = new OpenApiSchemaReference(ProblemSchemaFor(sorted), document) }
                },
                Extensions = new Dictionary<string, IOpenApiExtension>
                {
                    [CodesExtension] = new JsonNodeExtension(new JsonArray(sorted.Select(code => (JsonNode)code).ToArray()))
                }
            };
        }
        if (context.Description.ActionDescriptor.EndpointMetadata.OfType<RequestExample>().FirstOrDefault() is { } example &&
            operation.RequestBody?.Content?.TryGetValue("application/json", out var media) == true)
            media.Example = JsonSerializer.SerializeToNode(example.Value, example.Value.GetType(), WorkspaceJson.Options);
        if (context.Description.ActionDescriptor.EndpointMetadata.OfType<RequestExamples>().FirstOrDefault() is { } examples &&
            operation.RequestBody?.Content?.TryGetValue("application/json", out var namedMedia) == true)
            namedMedia.Examples = examples.Values.ToDictionary(entry => entry.Name,
                entry => (IOpenApiExample)new OpenApiExample
                {
                    Value = JsonSerializer.SerializeToNode(entry.Value, entry.Value.GetType(), WorkspaceJson.Options)
                }, StringComparer.Ordinal);
        if (operation.RequestBody is not null || operation.Parameters is { Count: > 0 })
            operation.Responses["400"] = new OpenApiResponseReference("unreadableRequest", document);
        return Task.CompletedTask;
    }

    private sealed record AgentLink(string Name, string Target, string Description,
        IReadOnlyDictionary<string, string>? Parameters = null);

    private sealed record AgentOperation(string OperationId, string ToolName, string SideEffects, bool Idempotent,
        bool Retryable, string Description, IReadOnlyList<AgentLink> Links);

    private static AgentLink Link(string name, string target, string description,
        IReadOnlyDictionary<string, string>? parameters = null) => new(name, target, description, parameters);

    private static IReadOnlyDictionary<string, string> FromBody(string name, string expression) =>
        new Dictionary<string, string> { [name] = expression };

    private static readonly IReadOnlyDictionary<string, AgentOperation> AgentOperations =
        new Dictionary<string, AgentOperation>(StringComparer.OrdinalIgnoreCase)
        {
            ["GET health"] = new("getHealth", "workspace_get_health", "none", true, true,
                "Internal readiness probe. The public ingress forwards only /api/*, so this endpoint is reachable inside the deployment, not from the public origin; do not use it for public discovery (use x-agent-discovery's /api/* URLs instead). Safe to poll internally; returns {\"status\":\"ready\"} when the workspace can serve requests.",
                [Link("session", "getSession", "Check what the caller's token may do next.")]),
            ["GET api/session"] = new("getSession", "workspace_get_session", "none", true, true,
                "Returns whether the caller's bearer token can read (CanRead) and manage (CanManage) the workspace. Call this first to choose which other tools to offer.",
                [Link("inventory", "listInventory", "List the published snapshot's entities, relationship types and relationships."),
                 Link("history", "listHistory", "List contribution history, oldest first.")]),
            ["GET api/inventory"] = new("listInventory", "workspace_list_inventory", "none", true, true,
                "Cursor-paginated listing of the published snapshot. Pass the previous page's nextCursor as cursor; never invent a cursor. Treat nextCursor=null as the end.",
                [Link("next", "listInventory", "Fetch the next page using this page's nextCursor, keeping the same limit.",
                    new Dictionary<string, string> { ["cursor"] = "$response.body#/nextCursor", ["limit"] = "$request.query.limit" }),
                 Link("search", "searchWorkspace", "Search the same snapshot by meaning; supply a SearchRequest body.")]),
            ["POST api/entities/detail"] = new("getEntityDetail", "workspace_get_entity_detail", "none", true, true,
                "Read-only lookup despite POST: the published key is the request body. Returns the entity with its runtime annotations.",
                [Link("traverse", "traverseGraph", "Walk relationships from this entity; supply its key as start in a TraversalRequest body."),
                 Link("createAnnotation", "createAnnotation", "Annotate this entity; supply its key as target in a CreateAnnotation body.")]),
            ["POST api/relationships/detail"] = new("getRelationshipDetail", "workspace_get_relationship_detail", "none", true, true,
                "Read-only lookup despite POST. Identify the relationship by published key or by runtime contribution id in the request body.",
                [Link("entityDetail", "getEntityDetail", "Inspect an endpoint entity; supply its published key as key in an EntityDetailRequest body.")]),
            ["POST api/search"] = new("searchWorkspace", "workspace_search", "none", true, true,
                "Semantic (embedding) search over published entities and annotations despite POST; it changes nothing. Each hit names its entityKey for getEntityDetail.",
                [Link("entityDetail", "getEntityDetail", "Open a hit; supply the hit's entityKey as key in an EntityDetailRequest body.")]),
            ["POST api/traverse"] = new("traverseGraph", "workspace_traverse_graph", "none", true, true,
                "Read-only graph walk despite POST, paged at 100 paths. Start from a published key, follow direction up to maxDepth. When another page exists the response carries nextCursor and truncated=true (the walk is not proven finished); submit nextCursor as cursor with the same start, direction, maxDepth and includeUnavailable to continue. A malformed or parameter-mismatched cursor fails 422 invalidCursor and a moved-on snapshot or workspace fails 409 staleCursor; both tell you to restart from the first page. A walk whose remaining frontier exceeds the cursor size limit fails 422 invalidTraversal and tells you to restart with a smaller maxDepth or a more specific start.",
                [Link("next", "traverseGraph", "Continue the walk: submit this response's nextCursor as cursor with the original start, direction, maxDepth and includeUnavailable.", FromBody("cursor", "$response.body#/nextCursor")),
                 Link("entityDetail", "getEntityDetail", "Inspect an entity on a returned path; supply its key in an EntityDetailRequest body."),
                 Link("relationshipDetail", "getRelationshipDetail", "Inspect a traversed relationship in detail.")]),
            ["POST api/impact/search"] = new("searchImpact", "workspace_search_impact", "none", true, true,
                "Read-only impact lead search despite POST, paged at 100 leads. Matches node labels and catalog concepts in the active snapshot's impact model; each lead says whether it is supported (by a reviewed C4 mapping or verified evidence) and why, so only supported leads can start a trace. The response's snapshotId pins every later call. When nextCursor is set, submit it as cursor with the same query and exactIri; a cursor for another query fails 422 invalidCursor, and a cursor or snapshotId from an earlier snapshot fails 409 snapshotChanged, which means start again from a new search. 503 impactUnavailable means the active snapshot was published without an impact model.",
                [Link("next", "searchImpact", "Fetch the next page: submit this response's nextCursor as cursor with the same query and exactIri.", FromBody("cursor", "$response.body#/nextCursor")),
                 Link("traceImpact", "traceImpact", "Trace impact paths from supported leads; supply this response's snapshotId and the leads' keys as starts.", FromBody("snapshotId", "$response.body#/snapshotId")),
                 Link("viewC4", "viewC4", "Show a C4 view of a mapped lead; supply this response's snapshotId.", FromBody("snapshotId", "$response.body#/snapshotId"))]),
            ["POST api/impact/paths"] = new("traceImpact", "workspace_trace_impact", "none", true, true,
                "Read-only impact trace despite POST. Starts are lead keys from searchImpact on the same snapshotId; unsupported starts are skipped and reported as gaps. Each path lists its steps in order with the cited evidence and lines, so a later step is never shown as a direct relationship of the start. Gaps say what could not be followed: missing relationship meaning, unlinked terms, unreadable evidence text and coverage gaps; an empty result does not prove no impact. A snapshotId that is no longer active fails 409 snapshotChanged.",
                [Link("viewC4", "viewC4", "Show a C4 view of a node on a path; supply the same snapshotId.", FromBody("snapshotId", "$response.body#/snapshotId"))]),
            ["POST api/impact/views"] = new("viewC4", "workspace_view_c4", "none", true, true,
                "Read-only C4 view despite POST: projects the context, container, component or code level around a node that has a reviewed C4 mapping, as the C4 pilot's view JSON together with its Markdown. A node without a mapping returns an empty view with a no-c4-mapping gap. A snapshotId that is no longer active fails 409 snapshotChanged.",
                [Link("traceImpact", "traceImpact", "Trace impact paths from a node in the view; supply the same snapshotId.", FromBody("snapshotId", "$response.body#/snapshotId"))]),
            ["POST api/assessments/preview"] = new("previewAssessment", "workspace_preview_assessment", "none", true, true,
                "Read-only despite POST: validates a schema 2 assessment draft against the active snapshot's impact model, freezes it with everything it cites, and returns the frozen content and its Markdown. Nothing is stored. Every likely impact must name a node on a cited path and evidence on that path, or a verified inspected view. A snapshotId that is no longer active fails 409 snapshotChanged: the draft belongs to an earlier snapshot, so start again from a new search. Approval and sign-off fields fail 422 approvalNotSupported; the workspace records impact, it does not approve it.",
                [Link("saveAssessment", "saveAssessment", "Save the same draft; requires ManageKnowledgeGraph.")]),
            ["POST api/assessments"] = new("saveAssessment", "workspace_save_assessment", "creates-assessment", false, false,
                "Validates, freezes and saves a schema 2 assessment draft as version 1, returning its id. Not idempotent: retrying saves another assessment, so read back the returned id instead of retrying blindly. The same refusals as previewAssessment apply.",
                [Link("assessment", "getAssessment", "Open the saved assessment.", new Dictionary<string, string> { ["id"] = "$response.body#/id" }),
                 Link("assessmentMarkdown", "getAssessmentMarkdown", "Download its Markdown.", new Dictionary<string, string> { ["id"] = "$response.body#/id" })]),
            ["GET api/assessments"] = new("assessmentList", "workspace_list_assessments", "none", true, true,
                "Cursor-paginated list of saved assessments, newest first, at most 50 per page. Pass the previous page's nextCursor as cursor with the same limit; never invent a cursor. Treat nextCursor=null as the end. pinnedToEarlierSnapshot=true marks an assessment saved on an earlier snapshot.",
                [Link("next", "assessmentList", "Fetch the next page using this page's nextCursor, keeping the same limit.",
                    new Dictionary<string, string> { ["cursor"] = "$response.body#/nextCursor", ["limit"] = "$request.query.limit" }),
                 Link("assessment", "getAssessment", "Open a listed assessment; supply its id.")]),
            ["GET api/assessments/{id}"] = new("getAssessment", "workspace_get_assessment", "none", true, true,
                "Returns a saved assessment exactly as it was frozen, with every revision. pinnedToEarlierSnapshot=true means a newer snapshot is active: the assessment still opens unchanged, but it can no longer be edited.",
                [Link("assessmentMarkdown", "getAssessmentMarkdown", "Download its Markdown.", new Dictionary<string, string> { ["id"] = "$request.path.id" }),
                 Link("editAssessment", "editAssessment", "Edit it on the same snapshot; supply its version as expectedVersion.", new Dictionary<string, string> { ["id"] = "$request.path.id" })]),
            ["POST api/assessments/{id}/successor"] = new("startAssessmentFrom", "workspace_start_assessment_from", "none", true, true,
                "Returns a new schema 2 draft on the active snapshot that carries the saved assessment's request id and description forward, with startedFrom naming it. Nothing else is copied, because leads, paths and evidence belong to the earlier snapshot. Nothing is stored: fill in the draft, preview it, and save it with saveAssessment, which links the new assessment back to this one.",
                [Link("saveAssessment", "saveAssessment", "Save the completed draft; requires ManageKnowledgeGraph.")]),
            ["GET api/assessments/{id}/markdown"] = new("getAssessmentMarkdown", "workspace_get_assessment_markdown", "none", true, true,
                "Returns the saved assessment's Markdown, byte for byte as it was rendered when saved.",
                [Link("assessment", "getAssessment", "Open the saved assessment with its frozen content and revisions.", new Dictionary<string, string> { ["id"] = "$request.path.id" })]),
            ["PUT api/assessments/{id}"] = new("editAssessment", "workspace_edit_assessment", "updates-assessment", false, false,
                "Revises a saved assessment on the snapshot it was saved on, keeping every earlier version with its editor and time. Optimistic concurrency: expectedVersion must match the current version, or it fails 409 versionConflict; reload and reapply your changes. An assessment saved on an earlier snapshot fails 409 snapshotMismatch: start a new assessment from it instead.",
                [Link("assessment", "getAssessment", "Read back the edited assessment and its revisions.", new Dictionary<string, string> { ["id"] = "$request.path.id" })]),
            ["GET api/history"] = new("listHistory", "workspace_list_history", "none", true, true,
                "Cursor-paginated contribution history, ordered oldest first by journal position. Filter with contributionId to follow one contribution's revisions; pass nextCursor for the next page. Entries are historical revisions, so there is deliberately no edit follow-on here: edit only from a current contribution record (for example a create/edit response).",
                [Link("next", "listHistory", "Fetch the next page using this page's nextCursor, keeping the same contributionId filter and limit.",
                    new Dictionary<string, string> { ["cursor"] = "$response.body#/nextCursor", ["contributionId"] = "$request.query.contributionId", ["limit"] = "$request.query.limit" })]),
            ["POST api/contributions/annotations"] = new("createAnnotation", "workspace_create_annotation", "creates-contribution", false, false,
                "Creates an annotation contribution and returns its id and version. Not idempotent: retrying creates another annotation. Follow history for the created id instead of retrying blindly.",
                [Link("history", "listHistory", "Read back the created contribution.", FromBody("contributionId", "$response.body#/id")),
                 Link("editContribution", "editContribution", "Revise it; supply id and the returned version as expectedVersion.", FromBody("id", "$response.body#/id")),
                 Link("removeContribution", "removeContribution", "Remove it; supply id and the current version as expectedVersion.", FromBody("id", "$response.body#/id"))]),
            ["POST api/contributions/relationships"] = new("createRelationship", "workspace_create_relationship", "creates-contribution", false, false,
                "Creates a runtime relationship contribution and returns its id and version. Not idempotent: retrying creates another relationship. Follow history for the created id instead of retrying blindly.",
                [Link("history", "listHistory", "Read back the created contribution.", FromBody("contributionId", "$response.body#/id")),
                 Link("editContribution", "editContribution", "Revise it; supply id and the returned version as expectedVersion.", FromBody("id", "$response.body#/id")),
                 Link("removeContribution", "removeContribution", "Remove it; supply id and the current version as expectedVersion.", FromBody("id", "$response.body#/id")),
                 Link("associateCodification", "associateCodification", "Codify it against a published relationship; supply id and the current version.", FromBody("id", "$response.body#/id"))]),
            ["PUT api/contributions/{id}"] = new("editContribution", "workspace_edit_contribution", "updates-contribution", false, false,
                "Edits an annotation or runtime relationship. Optimistic concurrency: expectedVersion must match the current version. On a 409 versionConflict, reconcile your intended changes with the current record returned in the response body's current field, then submit a revised edit using that record's version as expectedVersion; do not merely replace expectedVersion on your original changes, which would silently overwrite the other caller's edits.",
                [Link("history", "listHistory", "Read back the contribution's revisions.", FromBody("contributionId", "$response.body#/id")),
                 Link("editContribution", "editContribution", "Edit it again; supply id and the returned version as expectedVersion.", FromBody("id", "$response.body#/id")),
                 Link("removeContribution", "removeContribution", "Remove it; supply id and the returned version as expectedVersion.", FromBody("id", "$response.body#/id")),
                 Link("associateCodification", "associateCodification", "For a runtime relationship only: codify it against a published relationship; supply id and the returned version.", FromBody("id", "$response.body#/id"))]),
            ["POST api/contributions/{id}/remove"] = new("removeContribution", "workspace_remove_contribution", "removes-contribution", false, false,
                "Removes a contribution at expectedVersion. On a 409 versionConflict, inspect current.removed on the current record in the response body and review that record before deciding whether to retry: it may show another caller's edit to a still-active contribution (retry the removal with its version only if you still intend to remove it), or current.removed may already be true, in which case the removal has happened and no retry is needed. A 404 contributionNotFound is separate: no contribution with that id exists, so do not retry.",
                [Link("history", "listHistory", "Confirm the removal in history.", FromBody("contributionId", "$response.body#/id"))]),
            ["POST api/contributions/{id}/codifications"] = new("associateCodification", "workspace_associate_codification", "updates-contribution", false, false,
                "Associates a runtime relationship with the published relationship that codifies it, at expectedVersion. On a 409 versionConflict, reconcile your intended association with the current record returned in the response body's current field before submitting a revised request; do not merely replace expectedVersion, which could discard the other caller's changes.",
                [Link("history", "listHistory", "Read back the contribution with its codifications.", FromBody("contributionId", "$response.body#/id")),
                 Link("editContribution", "editContribution", "Revise it; supply id and the returned version as expectedVersion.", FromBody("id", "$response.body#/id")),
                 Link("removeContribution", "removeContribution", "Remove it; supply id and the returned version as expectedVersion.", FromBody("id", "$response.body#/id")),
                 Link("associateCodification", "associateCodification", "Associate another codification; supply id and the returned version.", FromBody("id", "$response.body#/id"))]),
        };

    private static void AgentDecorations(OpenApiOperation operation, OpenApiOperationTransformerContext context)
    {
        var description = context.Description;
        var key = $"{description.HttpMethod} {NormalisePath(description.RelativePath)}";
        var agent = AgentOperations.TryGetValue(key, out var known) ? known : Fallback(description, operation);
        operation.OperationId = agent.OperationId;
        if (string.IsNullOrWhiteSpace(operation.Description)) operation.Description = agent.Description;
        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        operation.Extensions[ToolNameExtension] = new JsonNodeExtension(JsonValue.Create(agent.ToolName));
        operation.Extensions[IdempotentExtension] = new JsonNodeExtension(JsonValue.Create(agent.Idempotent));
        operation.Extensions[RetryableExtension] = new JsonNodeExtension(JsonValue.Create(agent.Retryable));
        operation.Extensions[SideEffectsExtension] = new JsonNodeExtension(JsonValue.Create(agent.SideEffects));
        operation.Extensions[InputExtension] = new JsonNodeExtension(JsonValue.Create(InputOf(operation)));
        operation.Extensions[OutputExtension] = new JsonNodeExtension(JsonValue.Create(OutputOf(operation)));

        if (agent.Links.Count == 0) return;
        var success = operation.Responses?
            .Where(r => r.Key.StartsWith('2'))
            .Select(r => r.Value)
            .OfType<OpenApiResponse>()
            .FirstOrDefault();
        if (success is null) return;
        success.Links ??= new Dictionary<string, IOpenApiLink>();
        foreach (var link in agent.Links)
        {
            success.Links[link.Name] = new OpenApiLink
            {
                OperationId = link.Target,
                Description = link.Description,
                Parameters = link.Parameters?.ToDictionary(p => p.Key,
                    p => new RuntimeExpressionAnyWrapper { Expression = RuntimeExpression.Build(p.Value) }, StringComparer.Ordinal)
            };
        }
    }

    private static string NormalisePath(string? relativePath) =>
        string.Join('/', (relativePath ?? "").Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment.StartsWith('{')
                ? "{" + new string(segment.Trim('{', '}').TakeWhile(c => c is not (':' or '?' or '=')).ToArray()) + "}"
                : segment));

    private static AgentOperation Fallback(Microsoft.AspNetCore.Mvc.ApiExplorer.ApiDescription description, OpenApiOperation operation)
    {
        var segments = (description.RelativePath ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(s => !s.StartsWith('{')).ToArray();
        var method = description.HttpMethod ?? "GET";
        var name = method.ToLowerInvariant() + string.Concat(segments.Select(s =>
            string.Concat(s.Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]))));
        var read = HttpMethods.IsGet(method) || HttpMethods.IsHead(method);
        return new AgentOperation(name, "workspace_" + name, read ? "none" : "write", read, read,
            operation.Summary ?? name, []);
    }

    private static string InputOf(OpenApiOperation operation)
    {
        if (operation.RequestBody?.Content?.TryGetValue("application/json", out var media) == true &&
            media.Schema is OpenApiSchemaReference reference)
            return reference.Reference.Id ?? "inline";
        return operation.Parameters is { Count: > 0 } ? "parameters" : "none";
    }

    private static string OutputOf(OpenApiOperation operation)
    {
        var success = operation.Responses?
            .Where(r => r.Key.StartsWith('2'))
            .Select(r => r.Value)
            .OfType<OpenApiResponse>()
            .FirstOrDefault();
        if (success?.Content?.TryGetValue("application/json", out var media) == true &&
            media.Schema is OpenApiSchemaReference reference)
            return reference.Reference.Id ?? "inline";
        return "none";
    }

    /// <summary>The problem schema for a status's codes: a version conflict also returns the current record.</summary>
    public static string ProblemSchemaFor(IEnumerable<string> codes) =>
        codes.Contains("versionConflict") ? "WorkspaceConflictProblem" : "WorkspaceProblem";

    private static Task DocumentDescriptions(OpenApiDocument document, OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Runtime knowledge workspace API",
            Version = "v1",
            Description = "A division knowledge workspace for humans and agents. Use each operation's operationId or " +
                "x-agent-tool-name as its stable tool name, and its x-agent-idempotent, x-agent-retryable and " +
                "x-agent-side-effects flags to decide whether a call may be repeated. Follow a success response's " +
                "links (HATEOAS) for the next operations instead of constructing URLs. Errors are " +
                "application/problem+json with a stable code (see x-workspace-codes); a versionConflict carries the " +
                "current record. Listing endpoints use cursor pagination: pass nextCursor back as cursor and stop " +
                "when it is null. Unless x-workspace-role is none, send a bearer JWT for that role. " +
                "/health is an internal probe only: the public ingress forwards only /api/*."
        };
        document.Tags = new HashSet<OpenApiTag>
        {
            new() { Name = "Health", Description = "Anonymous readiness." },
            new() { Name = "Reading", Description = "Read-only queries over the published snapshot and contribution history (ReadKnowledgeGraph)." },
            new() { Name = "Impact", Description = "Read-only impact search, path traces and C4 views over the active snapshot's impact model (ReadKnowledgeGraph)." },
            new() { Name = "Assessments", Description = "Impact assessments: preview and read (ReadKnowledgeGraph), save and edit (ManageKnowledgeGraph)." },
            new() { Name = "Contributions", Description = "Writes that create, edit, remove or codify runtime contributions (ManageKnowledgeGraph)." }
        };
        document.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        document.Extensions["x-agent-discovery"] = new JsonNodeExtension(new JsonObject
        {
            ["spec"] = DocsUi.SpecPath,
            ["docs"] = "/api/docs",
            ["health"] = new JsonObject
            {
                ["path"] = "/health",
                ["scope"] = "internal only: the public ingress forwards only /api/*, so use the /api/* URLs above from the public origin"
            },
            ["auth"] = "bearer JWT; the required role is each operation's x-workspace-role",
            ["pagination"] = "cursor: pass nextCursor as cursor and keep the original contributionId and limit, stop when null",
            ["errors"] = "application/problem+json with a stable code per operation's x-workspace-codes",
            ["hateoas"] = "follow each success response's links and the runtime Link response headers",
            ["linkRelations"] = new JsonObject
            {
                ["next"] = "listInventory, listHistory, assessmentList, traverseGraph or searchImpact: the next page of the same listing, walk or search (registered relation)",
                ["service-desc"] = "this OpenAPI document at /api/openapi/v1.json (registered relation)",
                ["service-doc"] = "the interactive docs at /api/docs (registered relation)",
                [WorkspaceLinks.RelationBase + "session"] = "getSession",
                [WorkspaceLinks.RelationBase + "inventory"] = "listInventory",
                [WorkspaceLinks.RelationBase + "history"] = "listHistory",
                [WorkspaceLinks.RelationBase + "search"] = "searchWorkspace",
                [WorkspaceLinks.RelationBase + "traverse"] = "traverseGraph",
                [WorkspaceLinks.RelationBase + "createAnnotation"] = "createAnnotation",
                [WorkspaceLinks.RelationBase + "entityDetail"] = "getEntityDetail",
                [WorkspaceLinks.RelationBase + "relationshipDetail"] = "getRelationshipDetail",
                [WorkspaceLinks.RelationBase + "edit"] = "editContribution",
                [WorkspaceLinks.RelationBase + "remove"] = "removeContribution",
                [WorkspaceLinks.RelationBase + "codifications"] = "associateCodification",
                [WorkspaceLinks.RelationBase + "traceImpact"] = "traceImpact",
                [WorkspaceLinks.RelationBase + "viewC4"] = "viewC4",
                [WorkspaceLinks.RelationBase + "saveAssessment"] = "saveAssessment",
                [WorkspaceLinks.RelationBase + "assessment"] = "getAssessment",
                [WorkspaceLinks.RelationBase + "assessmentMarkdown"] = "getAssessmentMarkdown"
            }
        });
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[Bearer] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "An access token from the deployment's identity provider, with the workspace API audience."
        };

        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        // ProducesProblem metadata makes the framework add ProblemDetails; every such response is replaced above.
        document.Components.Schemas.Remove("ProblemDetails");
        document.Components.Schemas["Problem"] = ProblemSchema(withCode: false);
        document.Components.Schemas["WorkspaceProblem"] = ProblemSchema(withCode: true);
        var conflict = ProblemSchema(withCode: true);
        conflict.Properties!["current"] = new OpenApiSchemaReference("RuntimeContribution", document);
        document.Components.Schemas["WorkspaceConflictProblem"] = conflict;

        document.Components.Responses ??= new Dictionary<string, IOpenApiResponse>();
        var unauthorized = RefusalResponse(document, StatusCodes.Status401Unauthorized);
        unauthorized.Headers = new Dictionary<string, IOpenApiHeader>
        {
            ["WWW-Authenticate"] = new OpenApiHeader
            {
                Description = "Bearer",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            }
        };
        document.Components.Responses["unauthorized"] = unauthorized;
        document.Components.Responses["forbidden"] = RefusalResponse(document, StatusCodes.Status403Forbidden);
        document.Components.Responses["unreadableRequest"] = RefusalResponse(document, StatusCodes.Status400BadRequest);
        document.Components.Responses["internalError"] = new OpenApiResponse
        {
            Description = "The workspace could not complete the request.",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                [ProblemJson] = new() { Schema = new OpenApiSchemaReference("Problem", document) }
            }
        };
        return Task.CompletedTask;
    }

    /// <summary>A component response for one of the fixed refusal bodies of B22.</summary>
    internal static OpenApiResponse RefusalResponse(OpenApiDocument document, int status)
    {
        var refusal = ProblemResponses.Refusals[status];
        return new OpenApiResponse
        {
            Description = $"{refusal.Title} Code: {refusal.Code}",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                [ProblemJson] = new() { Schema = new OpenApiSchemaReference("WorkspaceProblem", document) }
            },
            Extensions = new Dictionary<string, IOpenApiExtension>
            {
                [CodesExtension] = new JsonNodeExtension(new JsonArray(refusal.Code))
            }
        };
    }

    internal static OpenApiSchema ProblemSchema(bool withCode)
    {
        var properties = new Dictionary<string, IOpenApiSchema>
        {
            ["type"] = new OpenApiSchema { Type = JsonSchemaType.String },
            ["title"] = new OpenApiSchema { Type = JsonSchemaType.String },
            ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
            ["detail"] = new OpenApiSchema { Type = JsonSchemaType.String },
            ["instance"] = new OpenApiSchema { Type = JsonSchemaType.String },
            ["traceId"] = new OpenApiSchema { Type = JsonSchemaType.String }
        };
        var required = new HashSet<string> { "status", "title" };
        if (withCode)
        {
            properties["code"] = new OpenApiSchema { Type = JsonSchemaType.String };
            required.Add("code");
        }
        return new OpenApiSchema { Type = JsonSchemaType.Object, Properties = properties, Required = required };
    }

    private static Task StableOrder(OpenApiDocument document, OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        var paths = new OpenApiPaths();
        foreach (var (key, item) in document.Paths.OrderBy(path => path.Key, StringComparer.Ordinal))
        {
            paths.Add(key, item);
            foreach (var operation in item.Operations?.Values ?? Enumerable.Empty<OpenApiOperation>())
            {
                if (operation.Responses is null) continue;
                var responses = new OpenApiResponses();
                foreach (var (status, response) in operation.Responses.OrderBy(r => r.Key, StringComparer.Ordinal))
                    responses.Add(status, response);
                operation.Responses = responses;
            }
        }
        document.Paths = paths;
        if (document.Components is { } components)
        {
            if (components.Schemas is not null)
                components.Schemas = new SortedDictionary<string, IOpenApiSchema>(components.Schemas, StringComparer.Ordinal);
            if (components.Responses is not null)
                components.Responses = new SortedDictionary<string, IOpenApiResponse>(components.Responses, StringComparer.Ordinal);
        }
        return Task.CompletedTask;
    }
}
