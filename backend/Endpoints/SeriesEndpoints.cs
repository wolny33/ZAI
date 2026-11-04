using Backend.Common;
using Npgsql;
using static Npgsql.Replication.PgOutput.Messages.RelationMessage;

namespace Backend.Endpoints
{
    public static class SeriesEndpoints
    {
        private const string Columns = "id, name, min_value, max_value, color, icon, unit";

        private record SeriesInput(string Name, double MinValue, double MaxValue, string Color, string? Icon, string? Unit);
        private record SeriesDto(int Id, string Name, double MinValue, double MaxValue, string Color, string? Icon, string? Unit): 
            SeriesInput(Name, MinValue, MaxValue, Color, Icon, Unit);

        public static void Map(WebApplication app)
        {
            app.MapGet("/api/series", GetSeries);
            app.MapGet("/api/series/{id:int}", GetSeriesById);
            app.MapPost("/api/series", CreateSeries);
            app.MapPut("/api/series/{id:int}", UpdateSeries);
            app.MapDelete("/api/series/{id:int}", DeleteSeries);
        }

        // GET /api/series
        private static async Task<IResult> GetSeries(NpgsqlDataSource ds)
        {
            await using var list = ds.CreateCommand("SELECT " + Columns + " FROM series ORDER BY id");
            await using var reader = await list.ExecuteReaderAsync();

            var result = new List<SeriesDto>();
            while (await reader.ReadAsync())
            {
                result.Add(ReadSeries(reader));
            }
            return Results.Json(result);

        }

        // GET /api/series/{id}
        private static async Task<IResult> GetSeriesById(int id, NpgsqlDataSource ds)
        {
            await using var get = ds.CreateCommand("SELECT " + Columns + " FROM series WHERE id = @id");
            get.Parameters.AddWithValue("id", id);
            await using var reader = await get.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                throw SeriesNotFound(id);
            }
            return Results.Json(ReadSeries(reader));
        }

        // POST /api/series
        private static async Task<IResult> CreateSeries(HttpContext ctx, NpgsqlDataSource ds)
        {
            await AdminAuth.Require(ctx, ds);
            var input = await ReadSeriesInput(ctx);

            await using var insert = ds.CreateCommand(
                "INSERT INTO series (name, min_value, max_value, color, icon, unit) " +
                "VALUES (@name, @min_value, @max_value, @color, @icon, @unit) RETURNING " + Columns);
            AddSeriesParameters(insert, input);
            await using var reader = await insert.ExecuteReaderAsync();
            await reader.ReadAsync();

            var series = ReadSeries(reader);
            return Results.Created("/api/series/" + series.Id, series);

        }

        // PUT /api/series/{id}
        private static async Task<IResult> UpdateSeries(int id, HttpContext ctx, NpgsqlDataSource ds)
        {
            await AdminAuth.Require(ctx, ds);
            var input = await ReadSeriesInput(ctx);

            // Check and update in one transaction (rolled back unless committed)
            await using var connection = await ds.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            // Lock the series row so that nothing can change it between the check and the update
            await using (var lockRow = new NpgsqlCommand("SELECT 1 FROM series WHERE id = @id FOR UPDATE", connection, transaction))
            {
                lockRow.Parameters.AddWithValue("id", id);
                if (await lockRow.ExecuteScalarAsync() == null)
                {
                    throw SeriesNotFound(id);
                }

                // The new range must still contain every stored measurement
                await using (var check = new NpgsqlCommand(
                    "SELECT EXISTS (SELECT 1 FROM measurements WHERE series_id = @id AND (value < @min_value OR value > @max_value))",
                    connection, transaction))
                {
                    check.Parameters.AddWithValue("id", id);
                    check.Parameters.AddWithValue("min_value", input.MinValue);
                    check.Parameters.AddWithValue("max_value", input.MaxValue);
                    if ((bool?)await check.ExecuteScalarAsync() == true)
                    {
                        throw new ApiException(409, "Conflict",
                            "The new range [minValue, maxValue] does not contain all measurements already stored in this series.");
                    }
                }

                SeriesDto series;
                await using (var update = new NpgsqlCommand(
                    "UPDATE series SET name = @name, min_value = @min_value, max_value = @max_value, " +
                    "color = @color, icon = @icon, unit = @unit WHERE id = @id RETURNING " + Columns,
                    connection, transaction))
                {
                    update.Parameters.AddWithValue("id", id);
                    AddSeriesParameters(update, input);
                    await using var reader = await update.ExecuteReaderAsync();
                    await reader.ReadAsync();
                    series = ReadSeries(reader);
                }

                await transaction.CommitAsync();
                return Results.Json(series);
            }
        }

        // DELETE /api/series/{id}
        private static async Task<IResult> DeleteSeries(int id, HttpContext ctx, NpgsqlDataSource ds)
        {
            await AdminAuth.Require(ctx, ds);

            // The schema's ON DELETE CASCADE also deletes the sensors and measurements of the series
            await using var remove = ds.CreateCommand("DELETE FROM series WHERE id = @id");
            remove.Parameters.AddWithValue("id", id);
            if (await remove.ExecuteNonQueryAsync() == 0)
            {
                throw SeriesNotFound(id);
            }
            return Results.NoContent();
        }

        private static SeriesDto ReadSeries(NpgsqlDataReader reader) => new SeriesDto(reader.GetInt32(0), reader.GetString(1), 
            reader.GetDouble(2), reader.GetDouble(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6));

        private static ApiException SeriesNotFound(int id) => new ApiException(404, "Not Found", $"Series {id} does not exist.");

        /// <summary>
        /// Reads and validates the body of POST and PUT.
        /// </summary>
        private static async Task<SeriesInput> ReadSeriesInput(HttpContext ctx)
        {
            var body = await HttpHelpers.ReadJsonObject(ctx);
            var validator = new RequestValidator(body);
            var name = validator.GetRequiredString("name", minLength: 1, maxLength: 100);
            var minValue = validator.GetRequiredNumber("minValue");
            var maxValue = validator.GetRequiredNumber("maxValue");
            var color = validator.GetRequiredString("color");
            var icon = validator.GetOptionalString("icon");
            var unit = validator.GetOptionalString("unit", maxLength: 20);

            if (!IsColor(color))
            {
                validator.AddRuleError("color", "Value must be a color in the #RRGGBB format.");
            }
            if (minValue >= maxValue)
            {
                validator.AddRuleError("maxValue", "Value must be greater than minValue.");
            }

            validator.ThrowIfInvalid();
            return new SeriesInput(name, minValue, maxValue, color, icon, unit);
        }

        private static bool IsColor(string text) => text.Length == 7 && text[0] == '#' && text.Skip(1).All(char.IsAsciiHexDigit);

        private static void AddSeriesParameters(NpgsqlCommand command, SeriesInput input)
        {
            command.Parameters.AddWithValue("name", input.Name);
            command.Parameters.AddWithValue("min_value", input.MinValue);
            command.Parameters.AddWithValue("max_value", input.MaxValue);
            command.Parameters.AddWithValue("color", input.Color);
            command.Parameters.AddWithValue("icon", (object?)input.Icon ?? DBNull.Value);
            command.Parameters.AddWithValue("unit", (object?)input.Unit ?? DBNull.Value);
        }

    }
}
