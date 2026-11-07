using System.Globalization;
using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;
using Microsoft.Extensions.Configuration;
using Npgsql;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Providers;
using RuntimeKnowledge.Store;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace RuntimeKnowledge.Worker;

public sealed record ImportRequest(string? ManifestId);
public sealed record ImportBatch(string State, string? ManifestId, long? Position = null);

public sealed class LambdaEntry
{
    private readonly Db? _db;
    private readonly IBundleObjects? _objects;
    private readonly IEmbeddingProvider? _embeddings;
    private readonly EmbeddingProfile? _profile;
    private readonly IConfiguration _settings = new ConfigurationBuilder().AddEnvironmentVariables().Build();

    public LambdaEntry() { }

    public LambdaEntry(Db db, IBundleObjects objects, IEmbeddingProvider embeddings, EmbeddingProfile profile) =>
        (_db, _objects, _embeddings, _profile) = (db, objects, embeddings, profile);

    public async Task<IndexBatchResult> IndexHandler(ILambdaContext context)
    {
        using var deadline = Deadline(context);
        var token = deadline.Token;
        await using var source = await DataSourceAsync(token);
        var db = _db ?? new Db(source!);
        var settings = _settings.GetSection("Workspace:Embeddings");
        var profile = _profile ?? new EmbeddingProfile("Bedrock", TitanEmbeddings.ModelId,
            settings["Revision"] ?? throw new InvalidOperationException("Configure the Bedrock embedding revision."),
            int.Parse(settings["Dimensions"] ?? "1024", CultureInfo.InvariantCulture), "knowledge-v1");
        var embeddings = _embeddings ?? EmbeddingAdapters.Create("Bedrock", null, settings["Region"]);
        using var disposable = _embeddings is null ? embeddings as IDisposable : null;
        return await new IndexCommand(new IndexRepository(db), embeddings, profile).RunBatchAsync(16, token);
    }

    public async Task<ImportBatch> ImportHandler(ImportRequest request, ILambdaContext context)
    {
        if (string.IsNullOrWhiteSpace(request.ManifestId)) return new ImportBatch("idle", null);
        var manifest = request.ManifestId;
        using var deadline = Deadline(context);
        var token = deadline.Token;
        await using var source = await DataSourceAsync(token);
        var db = _db ?? new Db(source!);
        if (await new IndexRepository(db).ReadActiveProfileAsync(token) is null)
            return new ImportBatch("waitingForProfile", manifest);

        var settings = _settings.GetSection("Workspace:Objects");
        using var client = _objects is null ? Program.CreateObjectClient(settings) : null;
        var objects = _objects ?? new BundleObjects(client!, settings["Bucket"] ??
            throw new InvalidOperationException("Configure the S3 bundle bucket."));
        var snapshots = new BundleImport(new SnapshotStore(db, objects));
        try
        {
            var position = await snapshots.ActivateAsync(manifest, token);
            return new ImportBatch("active", manifest, position);
        }
        catch (WorkspaceException problem) when (problem.Code == "snapshotNotStaged")
        {
            await snapshots.StageAsync(manifest, token);
            return new ImportBatch("staged", manifest);
        }
        catch (WorkspaceException problem) when (problem.Code == "snapshotIndexPending")
        {
            return new ImportBatch("waitingForIndex", manifest);
        }
    }

    private async Task<NpgsqlDataSource?> DataSourceAsync(CancellationToken token)
    {
        if (_db is not null) return null;
        var connection = await DatabaseSecrets.ResolveAsync(_settings.GetConnectionString("Workspace"),
            _settings["Workspace:Database:SecretArn"], _settings["Workspace:Database:Host"],
            _settings["Workspace:Database:MaximumPoolSize"], token);
        return NpgsqlDataSource.Create(connection);
    }

    private static CancellationTokenSource Deadline(ILambdaContext context)
    {
        var remaining = context.RemainingTime - TimeSpan.FromSeconds(2);
        return new CancellationTokenSource(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
    }
}
