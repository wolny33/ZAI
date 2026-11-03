using Npgsql;

namespace Backend.Common
{
    /// <summary>
    /// An administrator session found for the Bearer token of a request.
    /// </summary>
    public record AdminSession(int AdminId, string TokenHash);

    public static class AdminAuth
    {
        private const string BearerPrefix = "Bearer ";

        /// <summary>
        /// Returns the session of the request or throws 401 when the token is missing, unknown or expired.
        /// </summary>
        public static async Task<AdminSession> Require(HttpContext ctx, NpgsqlDataSource ds)
        {
            var header = ctx.Request.Headers.Authorization.ToString();
            if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw Unauthorized();
            }

            var token = header.Substring(BearerPrefix.Length).Trim();
            if (token.Length == 0)
            {
                throw Unauthorized();
            }

            // The database keeps only the hash of the token
            var tokenHash = HttpHelpers.Sha256Hex(token);
            await using var find = ds.CreateCommand(
                "SELECT admin_id FROM admin_sessions WHERE token_hash = @token_hash AND expires_at > now()");
            find.Parameters.AddWithValue("token_hash", tokenHash);
            var adminId = await find.ExecuteScalarAsync();
            if (adminId == null)
            {
                throw Unauthorized();
            }

            return new AdminSession((int)adminId, tokenHash);
        }

        private static ApiException Unauthorized() =>
            new(401, "Unauthorized", "A valid administrator token is required.", wwwAuthenticate: "Bearer");

    }
}
