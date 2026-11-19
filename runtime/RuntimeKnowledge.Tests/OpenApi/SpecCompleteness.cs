using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using RuntimeKnowledge.Api;

namespace RuntimeKnowledge.Tests.OpenApi;

/// <summary>The static half of ADR 0016: every mapped operation is fully described (B3).</summary>
internal static partial class SpecCompleteness
{
    /// <summary>Strips constraints, defaults, optional and catch-all markers: <c>{id:guid}</c> becomes <c>{id}</c>.</summary>
    public static string Normalise(string pattern)
    {
        var path = Parameter().Replace(pattern, match => "{" + match.Groups[1].Value + "}");
        return path.StartsWith('/') ? path : "/" + path;
    }

    /// <summary>Returns each gap as <c>"&lt;METHOD&gt; &lt;path&gt;: &lt;gap&gt;"</c>, ordinal-sorted.</summary>
    public static IReadOnlyList<string> Find(IEnumerable<RouteEndpoint> endpoints, JsonNode spec)
    {
        var gaps = new List<string>();
        var mapped = new HashSet<string>(StringComparer.Ordinal);
        foreach (var endpoint in endpoints)
        {
            if (endpoint.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>()?.ExcludeFromDescription == true) continue;
            if (endpoint.Metadata.GetMetadata<IHttpMethodMetadata>() is not { } methods) continue;
            var path = Normalise(endpoint.RoutePattern.RawText!);
            foreach (var method in methods.HttpMethods)
            {
                mapped.Add($"{method} {path}");
                foreach (var gap in Gaps(endpoint, spec["paths"]?[path]?[method.ToLowerInvariant()]))
                    gaps.Add($"{method} {path}: {gap}");
            }
        }

        foreach (var (path, item) in spec["paths"]?.AsObject() ?? [])
            foreach (var (method, _) in item!.AsObject())
                if (!mapped.Contains($"{method.ToUpperInvariant()} {path}"))
                    gaps.Add($"{method.ToUpperInvariant()} {path}: in the spec but not mapped");

        return gaps.Order(StringComparer.Ordinal).ToArray();
    }

    private static IEnumerable<string> Gaps(RouteEndpoint endpoint, JsonNode? operation)
    {
        if (operation is null)
        {
            yield return "not in the spec";
            yield break;
        }

        var responses = operation["responses"]?.AsObject() ?? [];
        if (string.IsNullOrWhiteSpace((string?)operation["summary"])) yield return "no summary";
        if (operation["x-workspace-role"] is null) yield return "no x-workspace-role";

        var accepts = endpoint.Metadata.GetMetadata<IAcceptsMetadata>();
        if (accepts?.RequestType is not null && operation["requestBody"]?["content"]?["application/json"]?["schema"] is null)
            yield return "no request schema";

        if (!responses.Any(r => r.Key.StartsWith('2') && r.Value?["content"]?.AsObject().Any(c => c.Value?["schema"] is not null) == true))
            yield return "no success response schema";

        if (WorkspaceOpenApi.RoleFor(endpoint.Metadata.ToList()) is not null)
        {
            if (!responses.ContainsKey("401")) yield return "no 401 response";
            if (!responses.ContainsKey("403")) yield return "no 403 response";
        }
        if (!responses.ContainsKey("500")) yield return "no 500 response";

        var readsInput = accepts?.RequestType is not null || endpoint.RoutePattern.Parameters.Count > 0 ||
                         operation["parameters"]?.AsArray().Count > 0 || operation["requestBody"] is not null;
        if (readsInput && !responses.ContainsKey("400")) yield return "no 400 response";

        foreach (var code in endpoint.Metadata.OfType<WorkspaceErrorCodes>().SelectMany(c => c.Codes).Distinct())
        {
            var status = ProblemResponses.StatusFor(code).ToString();
            if (!responses.ContainsKey(status)) yield return $"no {status} response for {code}";
        }
    }

    [GeneratedRegex(@"\{\**([^}:=?]+)[^}]*\}")]
    private static partial Regex Parameter();
}
