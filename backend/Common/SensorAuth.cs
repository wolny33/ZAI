using Npgsql;

namespace Backend.Common
{
    /// <summary>
    /// The sensor identified by the X-API-Key header, and the series it sends to.
    /// </summary>
    public record SensorIdentity(int SensorId, int SeriesId);

    public static class SensorAuth
    {
        public static async Task<SensorIdentity> Require(HttpContext ctx, NpgsqlDataSource ds)
        {
            var apiKey = ctx.Request.Headers["X-API-Key"].ToString();
            if (apiKey.Length == 0)
            {
                throw InvalidKey();
            }

            // The database keeps only the hash of the key
            await using var find = ds.CreateCommand("SELECT id, series_id FROM sensors WHERE key_hash = @key_hash");
            find.Parameters.AddWithValue("key_hash", Secrets.Hash(apiKey));
            await using var reader = await find.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                throw InvalidKey();
            }
            return new SensorIdentity(reader.GetInt32(0), reader.GetInt32(1));
        }

        public static ApiException InvalidKey() => 
            new(401, "Unauthorized", "A valid sensor API key is required.", wwwAuthenticate: "ApiKey");
    }
}
