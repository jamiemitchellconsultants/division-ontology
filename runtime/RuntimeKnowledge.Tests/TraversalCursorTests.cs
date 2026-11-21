using System.Text.Json;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Store;
using Xunit;

namespace RuntimeKnowledge.Tests;

public sealed class TraversalCursorTests
{
    private static readonly PublishedKey Start = new("example-sales", "concept", "https://example.test/sales#Order");
    private static readonly TraversalRequest Request = new(Start, "outgoing", 2, false);

    private static TraversalCursorPayload Payload(string snapshot = "snapshot-1", long journal = 42) => new(
        1, Start.StorageKey, "outgoing", 2, false, snapshot, journal,
        new TraversalResume(
            ["p:[\"example-sales\",\"concept\",\"https://example.test/sales#Order\"]", "r:11111111-1111-1111-1111-111111111111"],
            [new TraversalPending([0]), new TraversalPending([0, 1])],
            new TraversalCurrent([], "snapshot", "")));

    [Fact]
    public void RoundTripPreservesTheBoundPayload()
    {
        var cursor = TraversalCursor.Encode(Payload());
        Assert.True(cursor.Length <= TraversalCursor.MaxCursorChars);
        Assert.Equal(JsonSerializer.Serialize(Payload(), WorkspaceJson.Options),
            JsonSerializer.Serialize(TraversalCursor.Decode(cursor, Request, "snapshot-1", 42), WorkspaceJson.Options));
    }

    [Fact]
    public void SerializedResultOmitsNextCursorOnTheFinalPage()
    {
        var final = new TraversalResult("snapshot-1", [], [], false, []);
        var json = JsonSerializer.Serialize(final, WorkspaceJson.Options);
        Assert.DoesNotContain("nextCursor", json);
        var continued = final with { Truncated = true, NextCursor = "opaque" };
        Assert.Contains("\"nextCursor\":\"opaque\"", JsonSerializer.Serialize(continued, WorkspaceJson.Options));
        var request = JsonSerializer.Serialize(Request with { Cursor = "opaque" }, WorkspaceJson.Options);
        Assert.Contains("\"cursor\":\"opaque\"", request);
    }

    [Theory]
    [InlineData("not base64 at all!!!")]
    [InlineData("")]
    public void MalformedCursorsFailInvalid(string cursor)
    {
        var error = Assert.Throws<WorkspaceException>(() => TraversalCursor.Decode(cursor, Request, "snapshot-1", 42));
        Assert.Equal("invalidCursor", error.Code);
        Assert.Contains("Restart", error.Message);
    }

    [Fact]
    public void WrongVersionAndOversizeFailInvalid()
    {
        var wrongVersion = TraversalCursor.Encode(Payload() with { Version = 99 });
        Assert.Equal("invalidCursor",
            Assert.Throws<WorkspaceException>(() => TraversalCursor.Decode(wrongVersion, Request, "snapshot-1", 42)).Code);
        var oversize = new string('A', TraversalCursor.MaxCursorChars + 1);
        Assert.Equal("invalidCursor",
            Assert.Throws<WorkspaceException>(() => TraversalCursor.Decode(oversize, Request, "snapshot-1", 42)).Code);
    }

    [Fact]
    public void OutOfRangeReferenceIndexFailsInvalid()
    {
        var payload = Payload() with
        {
            Resume = Payload().Resume with { Pending = [new TraversalPending([77])] }
        };
        var cursor = TraversalCursor.Encode(payload);
        Assert.Equal("invalidCursor",
            Assert.Throws<WorkspaceException>(() => TraversalCursor.Decode(cursor, Request, "snapshot-1", 42)).Code);
    }

    [Theory]
    [InlineData("incoming", 2, false)]
    [InlineData("outgoing", 3, false)]
    [InlineData("outgoing", 2, true)]
    public void ParameterMismatchFailsInvalid(string direction, int depth, bool includeUnavailable)
    {
        var cursor = TraversalCursor.Encode(Payload());
        var changed = Request with { Direction = direction, MaxDepth = depth, IncludeUnavailable = includeUnavailable };
        Assert.Equal("invalidCursor",
            Assert.Throws<WorkspaceException>(() => TraversalCursor.Decode(cursor, changed, "snapshot-1", 42)).Code);
    }

    [Fact]
    public void ChangedStartFailsInvalid()
    {
        var cursor = TraversalCursor.Encode(Payload());
        var changed = Request with { Start = new PublishedKey("example-sales", "concept", "https://example.test/sales#Other") };
        Assert.Equal("invalidCursor",
            Assert.Throws<WorkspaceException>(() => TraversalCursor.Decode(cursor, changed, "snapshot-1", 42)).Code);
    }

    [Fact]
    public void SnapshotOrJournalMovementFailsStale()
    {
        var cursor = TraversalCursor.Encode(Payload());
        Assert.Equal("staleCursor",
            Assert.Throws<WorkspaceException>(() => TraversalCursor.Decode(cursor, Request, "snapshot-2", 42)).Code);
        Assert.Equal("staleCursor",
            Assert.Throws<WorkspaceException>(() => TraversalCursor.Decode(cursor, Request, "snapshot-1", 43)).Code);
    }

    [Fact]
    public void MissingBoundFieldFailsInvalidNotStale()
    {
        var cursor = TraversalCursor.Encode(Payload() with { Snapshot = null! });
        Assert.Equal("invalidCursor",
            Assert.Throws<WorkspaceException>(() => TraversalCursor.Decode(cursor, Request, "snapshot-1", 42)).Code);
    }

    [Fact]
    public void ParameterMismatchIsReportedBeforeStaleness()
    {
        var cursor = TraversalCursor.Encode(Payload());
        var changed = Request with { Direction = "incoming" };
        Assert.Equal("invalidCursor",
            Assert.Throws<WorkspaceException>(() => TraversalCursor.Decode(cursor, changed, "snapshot-2", 99)).Code);
    }
}
