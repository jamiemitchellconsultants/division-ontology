namespace RuntimeKnowledge.Impact.Engine;

internal static class StableSortExtensions
{
    /// <summary>Sorts in place, keeping equal elements in their original order as Array.prototype.sort does.
    /// List&lt;T&gt;.Sort is unstable, so the engine never uses it.</summary>
    public static void StableSort<T>(this List<T> list, Comparison<T> comparison)
    {
        var sorted = list.Order(Comparer<T>.Create(comparison)).ToList();
        list.Clear();
        list.AddRange(sorted);
    }
}
