using Npgsql;

namespace RuntimeKnowledge.Store;

public sealed class Db(NpgsqlDataSource source)
{
    public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = await source.OpenConnectionAsync(cancellationToken);
        try
        {
            // The image preloads AGE for all sessions, including non-superuser runtime roles.
            await using var searchPath = new NpgsqlCommand(
                "SET search_path = pg_catalog, ag_catalog, public", connection);
            await searchPath.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var initialize = new NpgsqlCommand("""
            SELECT pg_advisory_xact_lock(840248491012318);
            CREATE SCHEMA IF NOT EXISTS workspace_migrations;
            CREATE TABLE IF NOT EXISTS workspace_migrations.applied (
                resource_name text PRIMARY KEY,
                applied_at timestamptz NOT NULL DEFAULT now()
            );
            """, connection, transaction))
        {
            await initialize.ExecuteNonQueryAsync(cancellationToken);
        }

        var assembly = typeof(Db).Assembly;
        var migrations = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("RuntimeKnowledge.Store.Sql.", StringComparison.Ordinal) &&
                           name.EndsWith(".sql", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
        foreach (var migration in migrations)
        {
            await using var check = new NpgsqlCommand(
                "SELECT EXISTS (SELECT FROM workspace_migrations.applied WHERE resource_name = $1)",
                connection, transaction);
            check.Parameters.AddWithValue(migration);
            if (await check.ExecuteScalarAsync(cancellationToken) is true)
                continue;

            await using var resource = assembly.GetManifestResourceStream(migration)
                ?? throw new InvalidOperationException($"Migration resource missing: {migration}");
            using var reader = new StreamReader(resource);
            var sql = await reader.ReadToEndAsync(cancellationToken);
            await using var apply = new NpgsqlCommand(sql, connection, transaction);
            await apply.ExecuteNonQueryAsync(cancellationToken);
            await using var record = new NpgsqlCommand(
                "INSERT INTO workspace_migrations.applied (resource_name) VALUES ($1)",
                connection, transaction);
            record.Parameters.AddWithValue(migration);
            await record.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
