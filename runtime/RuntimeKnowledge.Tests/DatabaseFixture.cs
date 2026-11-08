using Npgsql;
using RuntimeKnowledge.Store;

namespace RuntimeKnowledge.Tests;

internal sealed class DatabaseFixture : IAsyncDisposable
{
    private readonly string _adminConnectionString;
    private readonly string _database;

    private DatabaseFixture(string adminConnectionString, string database, string connectionString)
    {
        _adminConnectionString = adminConnectionString;
        _database = database;
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public static async Task<DatabaseFixture> OpenAsync()
    {
        var configured = Environment.GetEnvironmentVariable("WORKSPACE_TEST_POSTGRES")
            ?? throw new InvalidOperationException("Set WORKSPACE_TEST_POSTGRES to the disposable test container.");
        var admin = new NpgsqlConnectionStringBuilder(configured) { Pooling = false };
        if (admin.Host is not ("127.0.0.1" or "localhost" or "::1") ||
            admin.Database != "postgres" || admin.Username != "workspace_test_admin")
            throw new InvalidOperationException("Integration tests require the local workspace test container.");

        var name = $"workspace_test_{Guid.NewGuid():N}";
        await using var db = new NpgsqlConnection(admin.ConnectionString);
        await db.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE {name} TEMPLATE template0", db);
        await create.ExecuteNonQueryAsync();

        var test = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = name };
        var fixture = new DatabaseFixture(admin.ConnectionString, name, test.ConnectionString);
        try
        {
            await using var source = NpgsqlDataSource.Create(fixture.ConnectionString);
            await new Db(source).MigrateAsync(CancellationToken.None);
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await using var db = new NpgsqlConnection(_adminConnectionString);
        await db.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE {_database}", db);
        await drop.ExecuteNonQueryAsync();
    }
}
