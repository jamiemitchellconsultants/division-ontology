using System.Security.Cryptography;
using System.Text.Json;
using RuntimeKnowledge.Contracts;

namespace RuntimeKnowledge.Store;

internal sealed class BundleReader(IBundleObjects objects)
{
    private const int MaxBytes = 64 * 1024 * 1024;

    public async Task<VerifiedBundle> ReadAsync(string id, CancellationToken cancellationToken)
    {
        if (!Hex(id, 64)) throw Invalid("Invalid snapshot digest.");
        try
        {
            var manifestBytes = await ReadObjectAsync($"manifests/{id}.json", id, null, cancellationToken);
            var manifest = Deserialize<BundleManifest>(manifestBytes);
            var sources = ValidateManifest(manifest);
            PublishedProjection? projection = null;
            foreach (var file in manifest.Files)
            {
                var bytes = await ReadObjectAsync($"blobs/{file.Sha256}", file.Sha256, file.Bytes, cancellationToken);
                if (file.Role == "projection") projection = Deserialize<PublishedProjection>(bytes);
            }

            ValidateProjection(projection!, sources, id);
            return new VerifiedBundle(id, manifest, projection!);
        }
        catch (JsonException ex)
        {
            throw new WorkspaceException("invalidBundle", "Bundle contains invalid JSON.", ex);
        }
        catch (FileNotFoundException ex)
        {
            throw new WorkspaceException("invalidBundle", "Bundle object is missing.", ex);
        }
    }

    private static Dictionary<string, string> ValidateManifest(BundleManifest manifest)
    {
        if (manifest.SchemaVersion != 1 || !Hex(manifest.DivisionCommit, 40) ||
            !Hex(manifest.CatalogSha256, 64) || manifest.Sources is null || manifest.Files is null ||
            (manifest.RegistryCommit is not null && !Hex(manifest.RegistryCommit, 40)))
            throw Invalid("Unsupported or invalid manifest.");

        var sources = new Dictionary<string, string>(StringComparer.Ordinal) { ["division-core"] = manifest.DivisionCommit };
        foreach (var source in manifest.Sources)
        {
            if (source is null || string.IsNullOrWhiteSpace(source.Id) || !Hex(source.Commit, 40) ||
                source.GraphStatus is not ("available" or "unavailable") ||
                (source.GraphStatus == "available"
                    ? source.GraphSchemaVersion is not (1 or 2)
                    : source.GraphSchemaVersion is not null) ||
                !sources.TryAdd(source.Id, source.Commit))
                throw Invalid("Duplicate or invalid source revision.");
        }

        var seen = new HashSet<(string Role, string Source, string Path)>();
        foreach (var file in manifest.Files)
        {
            if (file is null || string.IsNullOrWhiteSpace(file.Role) || file.SourceId is null ||
                !sources.ContainsKey(file.SourceId) || !SafePath(file.Path) ||
                !Hex(file.Sha256, 64) || file.Bytes is < 0 or > MaxBytes ||
                !seen.Add((file.Role, file.SourceId, file.Path)))
                throw Invalid("Duplicate or invalid bundle file.");
        }

        foreach (var required in new[] { "catalog", "nquads", "sourceInventory", "projection" })
        {
            if (manifest.Files.Count(f => f.Role == required && f.SourceId == "division-core") != 1 ||
                manifest.Files.Any(f => f.Role == required && f.SourceId != "division-core"))
                throw Invalid($"Bundle requires one {required} object.");
        }
        if (manifest.Files.Single(f => f.Role == "catalog").Sha256 != manifest.CatalogSha256)
            throw Invalid("Manifest catalog digest differs from the catalog object.");
        return sources;
    }

    private static void ValidateProjection(PublishedProjection value, Dictionary<string, string> sources, string id)
    {
        if (value.Entities is null || value.RelationshipTypes is null || value.Edges is null || value.Gaps is null)
            throw Invalid("Incomplete projection.");
        var all = new HashSet<PublishedKey>();
        var endpoints = new HashSet<PublishedKey>();
        var types = new HashSet<PublishedKey>();
        foreach (var entity in value.Entities)
        {
            if (entity?.Key?.Kind is not ("concept" or "node") || string.IsNullOrWhiteSpace(entity.Label) ||
                entity.Types is null || !ValidRecord(entity.Key, entity.Provenance, sources, id) ||
                !all.Add(entity.Key))
                throw Invalid("Duplicate or invalid published entity.");
            endpoints.Add(entity.Key);
        }
        foreach (var type in value.RelationshipTypes)
        {
            if (type?.Key?.Kind != "relationshipType" || string.IsNullOrWhiteSpace(type.Label) ||
                !ValidRecord(type.Key, type.Provenance, sources, id) || !all.Add(type.Key))
                throw Invalid("Duplicate or invalid relationship type.");
            types.Add(type.Key);
            endpoints.Add(type.Key);
        }
        foreach (var edge in value.Edges)
        {
            if (edge?.Key?.Kind is not ("assertion" or "reviewedLink" or "denotation" or "c4Binding") ||
                string.IsNullOrWhiteSpace(edge.Label) || !ValidRecord(edge.Key, edge.Provenance, sources, id) ||
                !all.Add(edge.Key) || !endpoints.Contains(edge.From) || !endpoints.Contains(edge.To) ||
                (edge.RelationshipType is not null && !types.Contains(edge.RelationshipType)) ||
                (edge.Key.Kind == "assertion" &&
                    (edge.RelationshipType is null || edge.Provenance.Basis is not ("direct" or "inferred") ||
                     edge.Provenance.Evidence.Count == 0)))
                throw Invalid("Duplicate or invalid published edge.");
        }
    }

    private static bool ValidRecord(PublishedKey key, Provenance? provenance,
        Dictionary<string, string> sources, string snapshotId) =>
        !string.IsNullOrWhiteSpace(key.Identifier) && !string.IsNullOrWhiteSpace(key.SourceId) &&
        sources.TryGetValue(key.SourceId, out var commit) && provenance is not null && provenance.Key == key &&
        provenance.SourceCommit == commit && (provenance.SnapshotId is null || provenance.SnapshotId == snapshotId) &&
        provenance.Evidence is not null && provenance.Evidence.All(e =>
            e is not null && SafePath(e.Path) && e.StartLine > 0 && e.EndLine >= e.StartLine &&
            Hex(e.BlobId, 40) && !string.IsNullOrWhiteSpace(e.Role));

    private async Task<byte[]> ReadObjectAsync(string key, string digest, long? length, CancellationToken token)
    {
        await using var source = await objects.OpenAsync(key, token);
        using var bytes = new MemoryStream();
        var buffer = new byte[65536];
        while (true)
        {
            var read = await source.ReadAsync(buffer, token);
            if (read == 0) break;
            if (bytes.Length + read > (length ?? MaxBytes))
                throw Invalid("Bundle object length does not match its manifest.");
            bytes.Write(buffer, 0, read);
        }
        var result = bytes.ToArray();
        if ((length is not null && result.LongLength != length) ||
            Convert.ToHexStringLower(SHA256.HashData(result)) != digest)
            throw Invalid("Bundle object digest or length does not match its manifest.");
        return result;
    }

    private static T Deserialize<T>(byte[] bytes) =>
        JsonSerializer.Deserialize<T>(bytes, WorkspaceJson.Options) ?? throw Invalid("Missing bundle data.");

    internal static bool Hex(string? value, int size) => value?.Length == size &&
        value.All(ch => ch is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool SafePath(string? path) => !string.IsNullOrWhiteSpace(path) &&
        !path.Contains('\\') && !path.Any(char.IsControl) &&
        path.Split('/').All(part => part is not ("" or "." or ".."));

    private static WorkspaceException Invalid(string message) => new("invalidBundle", message);
}
