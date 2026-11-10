using Npgsql;
using Xunit;

namespace RuntimeKnowledge.Tests;

public sealed class DatabaseTests
{
    [Fact]
    public async Task JournalSqlAndAgeShareRollback()
    {
        await using var fixture = await DatabaseFixture.OpenAsync();
        await using var db = new NpgsqlConnection(fixture.ConnectionString);
        await db.OpenAsync();
        await Execute(db, """
            SET search_path = ag_catalog, public;
            SET ROLE workspace_api;
            """);
        await using var tx = await db.BeginTransactionAsync();
        await using var write = new NpgsqlCommand("""
            INSERT INTO workspace_journal.events
                (position, event_id, event_type, schema_version, occurred_at, payload)
                VALUES (workspace_journal.next_position(), '18840d86-6335-4d0b-b77c-9e56c19ce9b1', 'annotationCreated', 1, now(), '{}');
            SELECT * FROM ag_catalog.cypher('workspace_g1',
                $$ CREATE (:Entity {key: 'proof'}) $$) AS (v agtype);
            """, db, tx);
        await write.ExecuteNonQueryAsync();
        await tx.RollbackAsync();
        Assert.Equal(0L, await Scalar(db, "SELECT count(*) FROM workspace_journal.events"));
        Assert.Equal(1L, await Scalar(db, "SELECT workspace_journal.next_position()"));
        Assert.Equal("0", await Scalar(db, """
            SELECT n::text FROM ag_catalog.cypher('workspace_g1', $$ MATCH (n:Entity) RETURN count(n) $$)
                AS (n agtype)
            """));
        Assert.Equal(0d, await Scalar(db, "SELECT '[1,0]'::vector <=> '[1,0]'::vector"));
    }

    [Fact]
    public async Task ApplicationRoleCanAppendButCannotRewriteHistory()
    {
        await using var fixture = await DatabaseFixture.OpenAsync();
        await using var db = new NpgsqlConnection(fixture.ConnectionString);
        await db.OpenAsync();
        await Execute(db, """
            SET ROLE workspace_api;
            INSERT INTO workspace_journal.events
                (position, event_id, event_type, schema_version, occurred_at, payload)
                VALUES (workspace_journal.next_position(), '18840d86-6335-4d0b-b77c-9e56c19ce9b1', 'annotationCreated', 1, now(), '{}');
            """);
        var error = await Assert.ThrowsAsync<PostgresException>(
            () => Execute(db, "DELETE FROM workspace_journal.events"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        Assert.Equal(1L, await Scalar(db, "SELECT count(*) FROM workspace_journal.events"));
    }

    private static async Task Execute(NpgsqlConnection db, string sql)
    {
        await using var command = new NpgsqlCommand(sql, db);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> Scalar(NpgsqlConnection db, string sql)
    {
        await using var command = new NpgsqlCommand(sql, db);
        return await command.ExecuteScalarAsync();
    }
}
