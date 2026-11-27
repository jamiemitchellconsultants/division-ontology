using System.Security.Cryptography;
using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact.Bundle;

namespace RuntimeKnowledge.Impact.Model;

/// <summary>The division's relationship master list, pinned to the catalog commit (ADR 0032, B10).</summary>
public sealed class MasterList
{
    public const string Path = "relationships/master-list.json";

    public JsonObject Identity { get; }
    private readonly Dictionary<string, string> _impacts;

    private MasterList(JsonObject identity, Dictionary<string, string> impacts)
    {
        Identity = identity;
        _impacts = impacts;
    }

    /// <summary>subject-to-object, object-to-subject, both or none; null when the relationship is not listed (B8).</summary>
    public string? Impact(string relationshipIri) => _impacts.TryGetValue(relationshipIri, out var impact) ? impact : null;

    internal static MasterList? Read(JsonObject input, ImpactBundle bundle)
    {
        if (input["masterList"] is not JsonObject identity) return null;
        var bytes = bundle.Read("relationshipMasterList", "division-core", Path)
            ?? throw new ImpactException("invalid impact bundle: master list digest");
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != identity["sha256"]?.GetValue<string>())
            throw new ImpactException("invalid impact bundle: master list digest");
        var impacts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in JsonNode.Parse(bytes)!["relationships"]!.AsArray())
            impacts[entry!["iri"]!.GetValue<string>()] = entry["impact"]!.GetValue<string>();
        return new MasterList(identity.DeepClone().AsObject(), impacts);
    }
}
