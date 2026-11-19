using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace RuntimeKnowledge.Tests.OpenApi;

public sealed record ObservedResponse(string Method, string Route, int Status);

/// <summary>The observation half of ADR 0016: statuses the scenario tests see must be in the spec (B2, B3).</summary>
internal static class ResponseConformance
{
    /// <summary>The framework's own responses before an operation runs. They are not operation statuses (B2, ADR 0018).</summary>
    public static readonly IReadOnlySet<int> TransportStatuses = new HashSet<int> { 405, 413, 415 };

    public static IReadOnlyList<string> Undeclared(IEnumerable<ObservedResponse> observed, JsonNode spec)
    {
        var lines = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var response in observed.Where(o => !TransportStatuses.Contains(o.Status)))
        {
            var operation = spec["paths"]?[response.Route]?[response.Method.ToLowerInvariant()];
            if (operation is null)
                lines.Add($"{response.Method} {response.Route}: not in the spec");
            else if (operation["responses"]?[response.Status.ToString()] is null)
                lines.Add($"{response.Method} {response.Route} {response.Status}");
        }
        return lines.ToArray();
    }

    /// <summary>Records the route and status of every response from a selected, described endpoint.</summary>
    public sealed class Recorder(ConcurrentBag<ObservedResponse> observed) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, inner) =>
            {
                await inner(context);
                // The exception handler clears the endpoint before it writes the problem response.
                var endpoint = context.GetEndpoint() ?? context.Features.Get<IExceptionHandlerFeature>()?.Endpoint;
                if (endpoint is RouteEndpoint route &&
                    route.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>()?.ExcludeFromDescription != true)
                    observed.Add(new ObservedResponse(context.Request.Method,
                        SpecCompleteness.Normalise(route.RoutePattern.RawText!), context.Response.StatusCode));
            });
            next(app);
        };
    }
}

public sealed class UndeclaredResponseException(IReadOnlyList<string> lines)
    : Exception("Responses not declared in the spec:\n" + string.Join('\n', lines));
