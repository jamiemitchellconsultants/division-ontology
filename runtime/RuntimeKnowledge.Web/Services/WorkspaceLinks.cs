using System.Text.Json;
using RuntimeKnowledge.Contracts;

namespace RuntimeKnowledge.Web.Services;

public static class WorkspaceLinks
{
    public static string Entity(PublishedKey key) => $"entity/{Encode(key)}";
    public static string Graph(PublishedKey key) => $"graph/{Encode(key)}";
    public static string GraphNodeId(PublishedKey key) => $"n:{Encode(key)}";
    public static string GraphRuntimeId(Guid id) => $"r:{id:D}";
    public static bool CanGraph(PublishedKey key) => key.Kind is "concept" or "node" &&
        !string.IsNullOrWhiteSpace(key.SourceId) && !string.IsNullOrWhiteSpace(key.Identifier);

    public static bool IsGraphDirection(string? direction) => direction is "both" or "incoming" or "outgoing";

    public static string GraphResume(PublishedKey focus, string direction, string action, Guid? contributionId)
    {
        if (!CanGraph(focus) || !IsGraphDirection(direction) || GraphEditorRequest.Parse(action, contributionId?.ToString("D")) is null)
            throw new ArgumentException("A graph resume link requires a published focus, direction, and matching editor action.");

        var route = $"{Graph(focus)}?direction={direction}&editor={action}";
        return contributionId is { } id ? $"{route}&contributionId={id:D}" : route;
    }
    public static string Relationship(TraversalStep step) => step.RuntimeId is { } id
        ? $"relationship/runtime/{id:D}"
        : Relationship(step.Key!);

    public static string Relationship(PublishedKey key) => $"relationship/published/{Encode(key)}";

    public static string Encode(PublishedKey key) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(key, WorkspaceJson.Options))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static PublishedKey? Decode(string value)
    {
        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
            return JsonSerializer.Deserialize<PublishedKey>(Convert.FromBase64String(base64), WorkspaceJson.Options);
        }
        catch (Exception error) when (error is FormatException or JsonException)
        {
            return null;
        }
    }
}

public sealed record GraphEditorRequest(string Action, Guid? ContributionId)
{
    public bool IsRelationship => Action is "add-relationship" or "edit-relationship" or "remove-relationship";
    public bool IsRemoval => Action is "remove-note" or "remove-relationship";
    public string Mode => IsRemoval ? "remove" : IsRelationship ? "relationship" : "annotation";

    public static GraphEditorRequest? Parse(string? action, string? id)
    {
        if (action is "add-note" or "add-relationship")
            return id is null ? new(action, null) : null;
        return action is "edit-note" or "remove-note" or "edit-relationship" or "remove-relationship" &&
            Guid.TryParseExact(id, "D", out var parsed) && parsed != Guid.Empty ? new(action, parsed) : null;
    }
}

public sealed record GraphContributionSaved(ContributionResult Result, bool IsRelationship, bool IsRemoval);
