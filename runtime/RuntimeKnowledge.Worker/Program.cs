using System.Globalization;
using System.Text.Json;
using Amazon;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Npgsql;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Providers;
using RuntimeKnowledge.Store;

namespace RuntimeKnowledge.Worker;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, signal) =>
        {
            signal.Cancel = true;
            stop.Cancel();
        };
        var settings = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        try
        {
            await Execute(settings, args, stop.Token);
            return 0;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception problem)
        {
            Console.Error.WriteLine(problem is WorkspaceException expected
                ? $"{expected.Code}: {expected.Message}"
                : $"Workspace command failed ({problem.GetType().Name}).");
            return 1;
        }
    }

    private static async Task Execute(IConfiguration settings, string[] args, CancellationToken token)
    {
        switch (args)
        {
            case ["bucket-init"]:
            case ["upload", _]:
                await ObjectCommand(settings, args, token);
                return;
        }

        var connection = Require(settings, "ConnectionStrings:Workspace");
        await using var dataSource = NpgsqlDataSource.Create(connection);
        var db = new Db(dataSource);

        switch (args)
        {
            case ["migrate"]:
                await db.MigrateAsync(token);
                return;
            case ["provision-database"]:
                await db.MigrateAsync(token);
                await ProvisionRoles(db, settings, token);
                return;
            case ["index-ready"]:
                if (await new IndexRepository(db).ReadActiveProfileAsync(token) is null)
                    throw new WorkspaceException("indexNotReady", "No embedding profile has been selected.");
                return;
            case ["index-once"] or ["index-loop"]:
                await RunIndex(settings.GetSection("Workspace:Embeddings"), db, args[0] == "index-loop", token);
                return;
            case ["stage", _] or ["activate", _] or ["describe-current"] or ["writes", "pause" or "resume"]
                or ["replay", "--through", _] or ["activate-replay", "--generation", _, "--expect-position", _]:
                await BundleCommand(settings, db, args, token);
                return;
            default:
                throw new WorkspaceException("invalidCommand",
                    "Use migrate, provision-database, bucket-init, upload, stage, activate, index-ready, index-once, index-loop, describe-current, replay, writes, or activate-replay.");
        }
    }

    private static async Task ObjectCommand(IConfiguration settings, string[] args, CancellationToken token)
    {
        var store = settings.GetSection("Workspace:Objects");
        using var client = CreateObjectClient(store);
        var bucket = Require(store, "Bucket");
        if (args is ["bucket-init"])
        {
            var current = await client.ListBucketsAsync(new Amazon.S3.Model.ListBucketsRequest(), token);
            if (current.Buckets?.Any(item => item.BucketName == bucket) != true)
                await client.PutBucketAsync(new Amazon.S3.Model.PutBucketRequest { BucketName = bucket }, token);
            return;
        }

        var objects = new BundleObjects(client, bucket);
        foreach (var prefix in new[] { "blobs", "manifests" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(args[1], prefix)))
            {
                await using var content = File.OpenRead(file);
                await objects.PutImmutableAsync($"{prefix}/{Path.GetFileName(file)}", content, token);
            }
        }
    }

    private static async Task BundleCommand(IConfiguration settings, Db db, string[] args, CancellationToken token)
    {
        var store = settings.GetSection("Workspace:Objects");
        using var client = CreateObjectClient(store);
        var objects = new BundleObjects(client, Require(store, "Bucket"));
        var importer = new BundleImport(new SnapshotStore(db, objects));
        var replay = new ReplayStore(db, objects);

        switch (args)
        {
            case ["stage", var manifest]:
                Print(await importer.StageAsync(manifest, token));
                break;
            case ["activate", var manifest]:
                Print(await importer.ActivateAsync(manifest, token));
                break;
            case ["describe-current"]:
                Print(await replay.DescribeCurrentAsync(token));
                break;
            case ["writes", "pause" or "resume"]:
                await replay.SetWritesPausedAsync(args[1] == "pause", token);
                break;
            case ["replay", "--through", var position] when long.TryParse(position, CultureInfo.InvariantCulture, out var head):
                Print(await replay.RebuildAsync(head, token));
                break;
            case ["activate-replay", "--generation", var generation, "--expect-position", var position]
                when Guid.TryParse(generation, out var candidate) &&
                     long.TryParse(position, CultureInfo.InvariantCulture, out var head):
                await replay.CutoverAsync(candidate, head, token);
                break;
            default:
                throw new WorkspaceException("invalidCommand", "Supply the requested digest, journal position or generation.");
        }
    }

    private static async Task RunIndex(IConfiguration settings, Db db, bool continuous, CancellationToken token)
    {
        var provider = Require(settings, "Provider");
        var profile = new EmbeddingProfile(provider, Require(settings, "Model"), Require(settings, "Revision"),
            int.Parse(Require(settings, "Dimensions"), CultureInfo.InvariantCulture), "knowledge-v1");
        var embeddings = EmbeddingAdapters.Create(provider, settings["Endpoint"], settings["Region"]);
        using var disposable = embeddings as IDisposable;
        var index = new IndexCommand(new IndexRepository(db), embeddings, profile);

        do
        {
            var batch = await index.RunBatchAsync(16, token);
            if (!continuous || batch.Claimed > 0) Print(batch);
            if (continuous) await Task.Delay(TimeSpan.FromSeconds(2), token);
        } while (continuous);
    }

    private static async Task ProvisionRoles(Db db, IConfiguration settings, CancellationToken token)
    {
        await using var connection = await db.OpenAsync(token);
        await using var tx = await connection.BeginTransactionAsync(token);
        foreach (var role in new[] { "api", "publisher", "indexer" })
        {
            var group = $"workspace_{role}";
            var login = $"{group}_user";
            var password = Require(settings, $"Workspace:Database:{role}Password");
            await using var create = new NpgsqlCommand("""
                SELECT CASE WHEN EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = $1)
                    THEN format('ALTER ROLE %I LOGIN PASSWORD %L', $1, $2)
                    ELSE format('CREATE ROLE %I LOGIN PASSWORD %L', $1, $2)
                END
                """, connection, tx);
            create.Parameters.AddWithValue(login);
            create.Parameters.AddWithValue(password);
            var sql = (string)(await create.ExecuteScalarAsync(token))!;
            await using var provision = new NpgsqlCommand(sql, connection, tx);
            await provision.ExecuteNonQueryAsync(token);
            await using var grant = new NpgsqlCommand($"GRANT {group} TO {login}", connection, tx);
            await grant.ExecuteNonQueryAsync(token);
        }
        await tx.CommitAsync(token);
    }

    private static AmazonS3Client CreateObjectClient(IConfiguration settings)
    {
        var region = settings["Region"] ?? "us-east-1";
        if (settings["Endpoint"] is not { Length: > 0 } endpoint)
            return new AmazonS3Client(RegionEndpoint.GetBySystemName(region));

        return new AmazonS3Client(Require(settings, "AccessKey"), Require(settings, "SecretKey"), new AmazonS3Config
        {
            ServiceURL = endpoint,
            AuthenticationRegion = region,
            ForcePathStyle = true
        });
    }

    private static string Require(IConfiguration settings, string key) =>
        settings[key] is { Length: > 0 } value ? value
            : throw new WorkspaceException("missingConfiguration", $"Configure {settings.PathFor(key)}.");

    private static string PathFor(this IConfiguration settings, string key) =>
        settings is IConfigurationSection section ? $"{section.Path}:{key}" : key;

    private static void Print<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value, WorkspaceJson.Options));
}
