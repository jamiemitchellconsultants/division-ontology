using RuntimeKnowledge.Contracts;

namespace RuntimeKnowledge.Web.Services;

public sealed record InventoryChoices(IReadOnlyList<(PublishedKey Key, string Label)> Entities,
    IReadOnlyList<(PublishedKey Key, string Label)> Types)
{
    public static async Task<InventoryChoices> Load(WorkspaceClient api)
    {
        var entities = new List<(PublishedKey, string)>();
        var types = new List<(PublishedKey, string)>();
        string? cursor = null;
        do
        {
            var page = await api.Inventory(cursor);
            entities.AddRange(page.Entities.Select(entity => (entity.Key, entity.Label)));
            types.AddRange(page.RelationshipTypes.Select(type => (type.Key, type.Label)));
            cursor = page.NextCursor;
        } while (cursor is not null);
        return new InventoryChoices(entities, types);
    }
}
