using Backend.Common;
using Npgsql;

namespace Backend.Endpoints
{
    public static class AuthEndpoints
    {
        private const int TokenLifetimeSeconds = 8 * 60 * 60;

        public static void Map(WebApplication app)
        {
            app.MapPost("/api/auth/login", Login);
            app.MapPost("/api/auth/logout", Logout);
            app.MapPut("/api/auth/password", ChangePassword);
        }

        // POST /api/auth/login
        private static async Task<IResult> Login(HttpContext ctx, NpgsqlDataSource ds, ILogger<Program> log)
        {
            var body = await HttpHelpers.ReadJsonObject(ctx);
            var validator = new RequestValidator(body);
            var username = validator.GetRequiredString("username");
            var password = validator.GetRequiredString("password");
            validator.ThrowIfInvalid();

            // Find the administrator
            int? adminId = null;
            string? hash = null;
            await using (var find = ds.CreateCommand("SELECT id, password_hash FROM admin_users WHERE username = @username"))
            {
                find.Parameters.AddWithValue("username", username);
                await using var reader = await find.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    adminId = reader.GetInt32(0);
                    hash = reader.GetString(1);
                }
            }

            if (!Passwords.Verify(password, hash) || adminId == null)
            {
                log.LogWarning("Failed login attempt for user '{Username}'.", username);
                throw new ApiException(401, "Unauthorized", "Invalid username or password.", wwwAuthenticate: "Bearer");
            }

            // Create a session: the client gets the token, the database keeps only its SHA-256 hash
            var token = Secrets.NewSecret();
            await using (var insert = ds.CreateCommand(
                "INSERT INTO admin_sessions (token_hash, admin_id, expires_at) " +
                "VALUES (@token_hash, @admin_id, now() + make_interval(secs => @expires_in))"))
            {
                insert.Parameters.AddWithValue("token_hash", Secrets.Hash(token));
                insert.Parameters.AddWithValue("admin_id", adminId.Value);
                insert.Parameters.AddWithValue("expires_in", (double)TokenLifetimeSeconds);
                await insert.ExecuteNonQueryAsync();
            }

            // Housekeeping: remove sessions that have already expired
            await using (var cleanup = ds.CreateCommand("DELETE FROM admin_sessions WHERE expires_at < now()"))
            {
                await cleanup.ExecuteNonQueryAsync();
            }

            return Results.Json(new { accessToken = token, tokenType = "Bearer", expiresIn = TokenLifetimeSeconds });
        }

        // POST /api/auth/logout
        private static async Task<IResult> Logout(HttpContext ctx, NpgsqlDataSource ds)
        {
            var session = await AdminAuth.Require(ctx, ds);

            await using var revoke = ds.CreateCommand("DELETE FROM admin_sessions WHERE token_hash = @token_hash");
            revoke.Parameters.AddWithValue("token_hash", session.TokenHash);
            await revoke.ExecuteNonQueryAsync();

            return Results.NoContent();
        }

        // PUT /api/auth/password
        private static async Task<IResult> ChangePassword(HttpContext ctx, NpgsqlDataSource ds, ILogger<Program> log)
        {
            var session = await AdminAuth.Require(ctx, ds);

            var body = await HttpHelpers.ReadJsonObject(ctx);
            var validator = new RequestValidator(body);
            var currentPassword = validator.GetRequiredString("currentPassword");
            var newPassword = validator.GetRequiredString("newPassword", minLength: 8);
            validator.ThrowIfInvalid();

            // Check if the current password is correct
            await using (var find = ds.CreateCommand("SELECT password_hash FROM admin_users WHERE id = @admin_id"))
            {
                find.Parameters.AddWithValue("admin_id", session.AdminId);
                var hash = (string?)await find.ExecuteScalarAsync();
                if (!Passwords.Verify(currentPassword, hash))
                {
                    log.LogWarning("Password change rejected for administrator {AdminId}: wrong current password.", session.AdminId);
                    throw new ApiException(403, "Forbidden", "The current password is incorrect.");
                }
            }

            // Other sessions except the current one are removed first
            await using (var revoke = ds.CreateCommand(
                "DELETE FROM admin_sessions WHERE admin_id = @admin_id AND token_hash <> @token_hash"))
            {
                revoke.Parameters.AddWithValue("admin_id", session.AdminId);
                revoke.Parameters.AddWithValue("token_hash", session.TokenHash);
                await revoke.ExecuteNonQueryAsync();
            }

            await using (var update = ds.CreateCommand("UPDATE admin_users SET password_hash = @password_hash WHERE id = @admin_id"))
            {
                update.Parameters.AddWithValue("password_hash", Passwords.Hash(newPassword));
                update.Parameters.AddWithValue("admin_id", session.AdminId);
                await update.ExecuteNonQueryAsync();
            }

            return Results.NoContent();
        }
    }
}
