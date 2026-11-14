using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace RuangMeetingApi.Data;

/// <summary>Creates the database if missing and runs Database/RuangMeetingDb.sql (idempotent).</summary>
public static partial class DatabaseInitializer
{
    public static async Task EnsureAsync(string connectionString, string scriptPath, CancellationToken ct = default)
    {
        var csb = new SqlConnectionStringBuilder(connectionString);
        var dbName = csb.InitialCatalog;

        // 1. create the database itself (connect to master)
        csb.InitialCatalog = "master";
        await using (var master = new SqlConnection(csb.ConnectionString))
        {
            await master.OpenAsync(ct);
            await using var cmd = master.CreateCommand();
            cmd.CommandText = $"IF DB_ID(@db) IS NULL EXEC('CREATE DATABASE [{dbName.Replace("]", "]]")}]')";
            cmd.Parameters.AddWithValue("@db", dbName);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 2. run the schema script batch by batch
        var script = await File.ReadAllTextAsync(scriptPath, ct);
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        foreach (var batch in GoSeparator().Split(script).Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = batch;
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoSeparator();
}
