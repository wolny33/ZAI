using Npgsql;

namespace Backend.Data;

public static class Db
{
    /// <summary>
    /// Runs db/schema.sql (copied next to the application by the .csproj).
    /// The script is idempotent, so it is safe to run it on every start.
    /// </summary>
    public static async Task ApplySchema(NpgsqlDataSource ds, string dir, ILogger log)
    {
        var sql = await File.ReadAllTextAsync(Path.Combine(dir, "schema.sql"));
        await using var cmd = ds.CreateCommand(sql);
        await cmd.ExecuteNonQueryAsync();
        log.LogInformation("Database schema applied.");
    }
}
