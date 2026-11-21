using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using RuntimeKnowledge.Contracts;

namespace RuntimeKnowledge.Store;

internal sealed record TraversalCursorPayload(
    int Version, string Start, string Direction, int MaxDepth, bool IncludeUnavailable,
    string Snapshot, long JournalPosition, TraversalResume Resume);

internal sealed record TraversalResume(
    IReadOnlyList<string> Refs, IReadOnlyList<TraversalPending> Pending, TraversalCurrent? Current);

internal sealed record TraversalPending(IReadOnlyList<int> Path);

internal sealed record TraversalCurrent(IReadOnlyList<int> Path, string Scope, string After);

/// <summary>The traversal continuation token of ADR 0019/0020: dictionary-coded, deflate-compressed, bound to parameters, snapshot and journal.</summary>
internal static class TraversalCursor
{
    internal const int MaxCursorChars = 262144;
    internal const int MaxJsonBytes = 8388608;
    private const string Restart = "Restart the traversal from the first page.";

    internal static string Encode(TraversalCursorPayload payload)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, WorkspaceJson.Options);
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            deflate.Write(json, 0, json.Length);
        var cursor = Convert.ToBase64String(output.ToArray());
        if (cursor.Length > MaxCursorChars)
            throw new InvalidOperationException("The traversal cursor exceeds the maximum issuable size.");
        return cursor;
    }

    internal static TraversalCursorPayload Decode(string cursor, TraversalRequest request, string snapshot, long journalHead)
    {
        var payload = Parse(cursor);
        if (payload.Start != request.Start.StorageKey || payload.Direction != request.Direction ||
            payload.MaxDepth != request.MaxDepth || payload.IncludeUnavailable != request.IncludeUnavailable)
            throw Invalid("The cursor was issued for different traversal parameters. " + Restart);
        if (payload.Snapshot != snapshot || payload.JournalPosition != journalHead)
            throw new WorkspaceException("staleCursor",
                "The published snapshot or workspace data has changed since this cursor was issued. " + Restart);
        return payload;
    }

    private static TraversalCursorPayload Parse(string cursor)
    {
        if (string.IsNullOrEmpty(cursor) || cursor.Length > MaxCursorChars) throw InvalidCursor();
        TraversalCursorPayload? payload;
        try
        {
            var compressed = Convert.FromBase64String(cursor);
            using var input = new MemoryStream(compressed);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, read);
                if (output.Length > MaxJsonBytes) throw InvalidCursor();
            }
            var json = JsonNode.Parse(output.ToArray());
            if (json is not JsonObject fields || fields["version"] is null || fields["start"] is null ||
                fields["direction"] is null || fields["maxDepth"] is null || fields["includeUnavailable"] is null ||
                fields["snapshot"] is null || fields["journalPosition"] is null || fields["resume"] is null)
                throw InvalidCursor();
            payload = json.Deserialize<TraversalCursorPayload>(WorkspaceJson.Options);
        }
        catch (WorkspaceException) { throw; }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or JsonException) { throw InvalidCursor(); }
        if (payload is null || payload.Version != 1 || payload.Resume is null ||
            payload.Resume.Refs is null || payload.Resume.Pending is null) throw InvalidCursor();
        foreach (var reference in payload.Resume.Refs)
            if (reference is null || !(reference.StartsWith("p:", StringComparison.Ordinal) ||
                                       reference.StartsWith("r:", StringComparison.Ordinal)))
                throw InvalidCursor();
        bool BadPath(IReadOnlyList<int>? path) => path is null || path.Any(i => i < 0 || i >= payload.Resume.Refs.Count);
        if (payload.Resume.Pending.Any(pending => pending is null || BadPath(pending.Path))) throw InvalidCursor();
        if (payload.Resume.Current is { } current &&
            (BadPath(current.Path) || current.Scope is not ("snapshot" or "runtime") || current.After is null))
            throw InvalidCursor();
        return payload;
    }

    private static WorkspaceException Invalid(string message) => new("invalidCursor", message);
    private static WorkspaceException InvalidCursor() => Invalid("The traversal cursor is not valid. " + Restart);
}

/// <summary>Test-only examination counts for the traversal walk; the API never passes one.</summary>
internal sealed class TraversalCounters
{
    public int Budget { get; set; } = 1000;
    public int ExaminedSteps { get; set; }
}
