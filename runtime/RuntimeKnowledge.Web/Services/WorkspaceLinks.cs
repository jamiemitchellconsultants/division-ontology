using System.Text.Json;
using RuntimeKnowledge.Contracts;

namespace RuntimeKnowledge.Web.Services;

public static class WorkspaceLinks
{
    public static string Entity(PublishedKey key) => $"entity/{Encode(key)}";
    public static string Relationship(TraversalStep step) => step.RuntimeId is { } id
        ? $"relationship/runtime/{id:D}"
        : $"relationship/published/{Encode(step.Key!)}";

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
