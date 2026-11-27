using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact;

namespace RuntimeKnowledge.Impact.Bundle;

public sealed record BundleFile(string Role, string SourceId, string Path, string Sha256, long Bytes);

public sealed class ImpactBundle
{
    public string Id { get; }
    public JsonObject Manifest { get; }
    public IReadOnlyList<BundleFile> Files { get; }
    private readonly Dictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);
    private readonly Func<string, byte[]?> _blobFn;

    private ImpactBundle(string id, JsonObject manifest, IReadOnlyList<BundleFile> files)
    {
        Id = id;
        Manifest = manifest;
        Files = files;
        _blobFn = null!;
    }

    private ImpactBundle(string id, JsonObject manifest, IReadOnlyList<BundleFile> files, Func<string, byte[]?> blobFn)
    {
        Id = id;
        Manifest = manifest;
        Files = files;
        _blobFn = blobFn;
    }

    public static ImpactBundle Open(string directory, string manifestId)
    {
        var manifestPath = Path.Combine(directory, "manifests", manifestId + ".json");
        var manifestBytes = File.ReadAllBytes(manifestPath);
        var manifest = JsonNode.Parse(manifestBytes)!.AsObject();
        var blobDir = Path.Combine(directory, "blobs");
        return FromObjects(manifestId, manifestBytes, sha =>
        {
            var path = Path.Combine(blobDir, sha);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        });
    }

    public static ImpactBundle FromObjects(string manifestId, byte[] manifestBytes, Func<string, byte[]?> blob)
    {
        var actualId = Convert.ToHexStringLower(SHA256.HashData(manifestBytes));
        if (actualId != manifestId)
            throw new ImpactException($"invalid impact bundle: manifest digest mismatch (expected {manifestId}, got {actualId})");

        var manifest = JsonNode.Parse(manifestBytes)!.AsObject();
        if (manifest["schemaVersion"] is not JsonValue sv || sv.GetValue<int>() != 1)
            throw new ImpactException("invalid impact bundle: unsupported schema version");

        var files = new List<BundleFile>();
        var fileArray = manifest["files"]!.AsArray();
        foreach (var fileNode in fileArray)
        {
            var fo = fileNode!.AsObject();
            var file = new BundleFile(
                fo["role"]!.GetValue<string>(),
                fo["sourceId"]!.GetValue<string>(),
                fo["path"]!.GetValue<string>(),
                fo["sha256"]!.GetValue<string>(),
                fo["bytes"]!.GetValue<long>());
            files.Add(file);
        }

        var impactInputCount = files.Count(f => f.Role == "impactInput" && f.SourceId == "division-core");
        if (impactInputCount != 1)
            throw new ImpactException($"invalid impact bundle: expected exactly one impactInput file for division-core, found {impactInputCount}");

        return new ImpactBundle(manifestId, manifest, files, blob);
    }

    public void VerifyBlobs()
    {
        foreach (var file in Files)
        {
            var bytes = _blobFn(file.Sha256);
            if (bytes == null)
                throw new ImpactException($"invalid impact bundle: missing blob {file.Sha256}");
            var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (digest != file.Sha256)
                throw new ImpactException($"invalid impact bundle: blob digest mismatch for {file.Path}");
            if (bytes.Length != file.Bytes)
                throw new ImpactException($"invalid impact bundle: blob length mismatch for {file.Path}");
            _blobs.TryAdd(file.Sha256, bytes);
        }
    }

    public byte[]? Read(string role, string sourceId, string path)
    {
        var file = Files.FirstOrDefault(f => f.Role == role && f.SourceId == sourceId && f.Path == path);
        if (file == null) return null;
        if (_blobs.TryGetValue(file.Sha256, out var cached)) return cached;
        var bytes = _blobFn(file.Sha256);
        if (bytes != null) _blobs.TryAdd(file.Sha256, bytes);
        return bytes;
    }

    private readonly Dictionary<string, string> _checkouts = new(StringComparer.Ordinal);

    public void SetCheckout(string sourceId, string dir)
    {
        _checkouts[sourceId] = dir;
    }

    public string? GetCheckoutDir(string sourceId)
    {
        _checkouts.TryGetValue(sourceId, out var dir);
        return dir;
    }

    public byte[]? ReadFileFromCheckout(string sourceId, string path)
    {
        var dir = GetCheckoutDir(sourceId);
        if (dir == null) return null;
        var filePath = Path.Combine(dir, path);
        return File.Exists(filePath) ? File.ReadAllBytes(filePath) : null;
    }
}
