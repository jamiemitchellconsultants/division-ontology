using System.Security.Cryptography;
using System.Text.Json;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Store;
using Xunit;

namespace RuntimeKnowledge.Tests;

public sealed class BundleGraphSchemaTests
{
    [Theory]
    [InlineData("available", 1)]
    [InlineData("available", 2)]
    [InlineData("unavailable", null)]
    public async Task AcceptsSupportedGraphMetadata(string state, int? version)
    {
        var (objects, id) = await WithGraphVersion(state, version);
        var bundle = await new BundleReader(objects).ReadAsync(id, CancellationToken.None);
        Assert.Equal(1, bundle.Manifest.SchemaVersion);
        Assert.Equal(state, bundle.Manifest.Sources[0].GraphStatus);
        Assert.Equal(version, bundle.Manifest.Sources[0].GraphSchemaVersion);
        Assert.Equal(1, bundle.Manifest.Sources[1].GraphSchemaVersion);
    }

    [Theory]
    [InlineData("available", 0)]
    [InlineData("available", 3)]
    [InlineData("available", null)]
    [InlineData("unavailable", 1)]
    [InlineData("unavailable", 2)]
    public async Task RejectsInvalidGraphMetadata(string state, int? version)
    {
        var (objects, id) = await WithGraphVersion(state, version);
        var error = await Assert.ThrowsAsync<WorkspaceException>(() =>
            new BundleReader(objects).ReadAsync(id, CancellationToken.None));
        Assert.Equal("invalidBundle", error.Code);
    }

    [Fact]
    public async Task Bundle_with_impact_input_role_is_read()
    {
        var cancellation = CancellationToken.None;
        var objects = new MemoryBundles();
        var originalId = objects.AddSnapshot("initial");
        var impactInputBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new { schemaVersion = 1 }, WorkspaceJson.Options);
        var impactDigest = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(impactInputBytes));
        await objects.PutImmutableAsync("blobs/" + impactDigest, new MemoryStream(impactInputBytes), cancellation);

        await using var original = await objects.OpenAsync("manifests/" + originalId + ".json", cancellation);
        var manifest = (await System.Text.Json.JsonSerializer.DeserializeAsync<BundleManifest>(
            original, WorkspaceJson.Options, cancellation))!;
        var impactFile = new BundleFile("impactInput", "division-core", "impact-input.json", impactDigest, impactInputBytes.Length);
        var allFiles = manifest.Files.Concat(new[] { impactFile }).ToArray();
        var newManifest = manifest with { Files = allFiles };
        var newBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(newManifest, WorkspaceJson.Options);
        var newId = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(newBytes));
        using var newData = new MemoryStream(newBytes);
        await objects.PutImmutableAsync("manifests/" + newId + ".json", newData, cancellation);

        var bundle = await new BundleReader(objects).ReadAsync(newId, cancellation);
        Assert.NotNull(bundle);
        Assert.True(bundle.Manifest.Files.Any(f => f.Role == "impactInput"));
    }

    private static async Task<(MemoryBundles Objects, string Id)> WithGraphVersion(string state, int? version)
    {
        var cancellation = CancellationToken.None;
        var objects = new MemoryBundles();
        var originalId = objects.AddSnapshot("initial");
        await using var original = await objects.OpenAsync("manifests/" + originalId + ".json", cancellation);
        var manifest = (await JsonSerializer.DeserializeAsync<BundleManifest>(
            original, WorkspaceJson.Options, cancellation))!;
        var sources = manifest.Sources.ToArray();
        sources[0] = sources[0] with { GraphStatus = state, GraphSchemaVersion = version };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest with { Sources = sources }, WorkspaceJson.Options);
        var id = Convert.ToHexStringLower(SHA256.HashData(bytes));
        using var data = new MemoryStream(bytes);
        await objects.PutImmutableAsync("manifests/" + id + ".json", data, cancellation);
        return (objects, id);
    }
}
