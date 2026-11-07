using Backend.Common;
using Npgsql;

namespace Backend.Data
{

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

        /// <summary>
        /// Creates the administrator account from ADMIN_USERNAME / ADMIN_PASSWORD
        /// if there is no administrator yet.
        /// </summary>
        public static async Task SeedAdmin(NpgsqlDataSource ds, IConfiguration config, ILogger log)
        {
            await using var exists = ds.CreateCommand("SELECT EXISTS (SELECT 1 FROM admin_users)");
            if (await exists.ExecuteScalarAsync() is true)
            {
                return;
            }

            var username = config["ADMIN_USERNAME"] ?? "admin";
            var password = config["ADMIN_PASSWORD"];
            if (string.IsNullOrEmpty(password))
            {
                log.LogWarning("No administrator exists and ADMIN_PASSWORD is not set - skipping creation.");
                return;
            }

            var hash = Passwords.Hash(password);
            await using var insert = ds.CreateCommand(
                "INSERT INTO admin_users (username, password_hash) VALUES (@username, @password_hash) ON CONFLICT (username) DO NOTHING");
            insert.Parameters.AddWithValue("username", username);
            insert.Parameters.AddWithValue("password_hash", hash);
            await insert.ExecuteNonQueryAsync();
            log.LogInformation("Administrator '{Username}' created.", username);
        }

    }
}