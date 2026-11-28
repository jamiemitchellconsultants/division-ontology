using RuntimeKnowledge.Impact.Engine;
using Xunit;

namespace RuntimeKnowledge.Tests.Impact;

public sealed class StableSortTests
{
    [Fact]
    public void Equal_elements_keep_their_original_order()
    {
        // Enough equal keys that an introsort would reorder them.
        var items = Enumerable.Range(0, 200).Select(i => (Key: i % 3, Seq: i)).ToList();
        items.StableSort((a, b) => a.Key.CompareTo(b.Key));
        Assert.Equal(items.OrderBy(x => x.Key).ThenBy(x => x.Seq), items);
    }
}
