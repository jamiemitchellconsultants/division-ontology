namespace RuntimeKnowledge.Api;

/// <summary>The workspace error codes a route can raise. Its problem responses are derived from them.</summary>
internal sealed record WorkspaceErrorCodes(IReadOnlyList<string> Codes);

/// <summary>An invented request body shown as the operation's example.</summary>
internal sealed record RequestExample(object Value);

/// <summary>Named invented request bodies shown as the operation's examples.</summary>
internal sealed record RequestExamples(IReadOnlyList<(string Name, object Value)> Values);

internal static class WorkspaceErrors
{
    /// <summary>Declares the workspace codes a route can raise. Statuses come only from <see cref="ProblemResponses.StatusFor"/>.</summary>
    public static RouteHandlerBuilder ProducesWorkspaceErrors(this RouteHandlerBuilder builder, params string[] codes)
    {
        var refusals = ProblemResponses.Refusals.Values.Select(refusal => refusal.Code).ToHashSet(StringComparer.Ordinal);
        foreach (var code in codes)
            if (refusals.Contains(code))
                throw new ArgumentException($"{code} is a refusal code. Refusals are described for every operation, never declared.", nameof(codes));
        builder.WithMetadata(new WorkspaceErrorCodes(codes));
        foreach (var status in codes.Select(ProblemResponses.StatusFor).Distinct().Order())
            builder.ProducesProblem(status);
        return builder;
    }
}
