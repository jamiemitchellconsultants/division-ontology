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
        if (operation.RequestBody is not null || operation.Parameters is { Count: > 0 })
            operation.Responses["400"] = new OpenApiResponseReference("unreadableRequest", document);
        return Task.CompletedTask;
    }

    /// <summary>The problem schema for a status's codes: a version conflict also returns the current record.</summary>
    public static string ProblemSchemaFor(IEnumerable<string> codes) =>
        codes.Contains("versionConflict") ? "WorkspaceConflictProblem" : "WorkspaceProblem";

    private static Task DocumentDescriptions(OpenApiDocument document, OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo { Title = "Runtime knowledge workspace API", Version = "v1" };
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
