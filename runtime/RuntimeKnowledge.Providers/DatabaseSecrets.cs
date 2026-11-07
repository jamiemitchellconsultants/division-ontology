using System.Globalization;
using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Npgsql;

namespace RuntimeKnowledge.Providers;

public static class DatabaseSecrets
{
    public static async Task<string> ResolveAsync(string? connection, string? secretArn, string? host,
        string? maximumPoolSize, CancellationToken token)
    {
        if (!string.IsNullOrWhiteSpace(connection)) return connection;
        if (string.IsNullOrWhiteSpace(secretArn) || string.IsNullOrWhiteSpace(host) ||
            !int.TryParse(maximumPoolSize, CultureInfo.InvariantCulture, out var pool) || pool is < 1 or > 20)
            throw new InvalidOperationException("Configure the workspace database connection or secret, host and pool limit.");

        using var client = new AmazonSecretsManagerClient();
        var secret = await client.GetSecretValueAsync(new GetSecretValueRequest { SecretId = secretArn }, token);
        using var value = JsonDocument.Parse(secret.SecretString ?? "");
        var username = value.RootElement.GetProperty("username").GetString();
        var password = value.RootElement.GetProperty("password").GetString();
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("The database secret requires a username and password.");

        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Database = "workspace",
            Username = username,
            Password = password,
            MaxPoolSize = pool,
            GssEncryptionMode = GssEncryptionMode.Disable
        }.ConnectionString;
    }
}
