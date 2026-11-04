using Backend.Common;
using Npgsql;

namespace Backend.Endpoints
{
    public static class SensorEndpoints
    {
        private const string SelectSensorsQuery =
            "SELECT s.id, s.name, s.series_id, s.created_at, " +
            "(SELECT max(m.ts) FROM measurements m WHERE m.sensor_id = s.id) FROM sensors s";

        private record SensorDto(int Id, string Name, int SeriesId, DateTimeOffset CreatedAt, DateTimeOffset? LastMeasurementAt);
        private record SensorCreatedDto(int Id, string Name, int SeriesId, DateTimeOffset CreatedAt, DateTimeOffset? LastMeasurementAt, string ApiKey)
            : SensorDto(Id, Name, SeriesId, CreatedAt, LastMeasurementAt);


        public static void Map(WebApplication app)
        {
            app.MapGet("/api/sensors", GetSensors);
            app.MapGet("/api/sensors/{id:int}", GetSensorById);
            app.MapPost("/api/sensors", CreateSensor);
            app.MapDelete("/api/sensors/{id:int}", DeleteSensor);
        }

        // GET /api/sensors
        private static async Task<IResult> GetSensors(HttpContext ctx, NpgsqlDataSource ds)
        {
            await AdminAuth.Require(ctx, ds);

            await using var list = ds.CreateCommand(SelectSensorsQuery + " ORDER BY s.id");
            await using var reader = await list.ExecuteReaderAsync();

            var result = new List<SensorDto>();
            while (await reader.ReadAsync())
            {
                result.Add(ReadSensor(reader));
            }
            return Results.Json(result);
        }

        // GET /api/sensors/{id}
        private static async Task<IResult> GetSensorById(int id, HttpContext ctx, NpgsqlDataSource ds)
        {
            await AdminAuth.Require(ctx, ds);

            await using var get = ds.CreateCommand(SelectSensorsQuery + " WHERE s.id = @id");
            get.Parameters.AddWithValue("id", id);
            await using var reader = await get.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                throw SensorNotFound(id);
            }
            return Results.Json(ReadSensor(reader));
        }

        // POST /api/sensors
        private static async Task<IResult> CreateSensor(HttpContext ctx, NpgsqlDataSource ds)
        {
            await AdminAuth.Require(ctx, ds);

            var body = await HttpHelpers.ReadJsonObject(ctx);
            var validator = new RequestValidator(body);
            var name = validator.GetRequiredString("name", minLength: 1, maxLength: 100);
            var seriesId = validator.GetRequiredInteger("seriesId");
            validator.ThrowIfInvalid();

            var apiKey = HttpHelpers.NewSecret();
            try
            {
                await using var insert = ds.CreateCommand(
                    "INSERT INTO sensors (name, series_id, key_hash) VALUES (@name, @series_id, @key_hash) " +
                    "RETURNING id, name, series_id, created_at");
                insert.Parameters.AddWithValue("name", name);
                insert.Parameters.AddWithValue("series_id", seriesId);
                insert.Parameters.AddWithValue("key_hash", HttpHelpers.Sha256Hex(apiKey));
                await using var reader = await insert.ExecuteReaderAsync();
                await reader.ReadAsync();

                var sensor = new SensorCreatedDto(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2), 
                    reader.GetFieldValue<DateTimeOffset>(3), null, apiKey);
                return Results.Created("/api/sensors/" + sensor.Id, sensor);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
            {
                throw new ApiException(422, "Unprocessable Content", "The request body breaks validation rules.", 
                    [new FieldError("seriesId", $"Series {seriesId} does not exist.")]);

            }
        }

        // DELETE /api/sensors/{id}
        private static async Task<IResult> DeleteSensor(int id, HttpContext ctx, NpgsqlDataSource ds)
        {
            await AdminAuth.Require(ctx, ds);

            // measurements.sensor_id becomes NULL (ON DELETE SET NULL)
            await using var remove = ds.CreateCommand("DELETE FROM sensors WHERE id = @id");
            remove.Parameters.AddWithValue("id", id);
            if (await remove.ExecuteNonQueryAsync() == 0)
            {
                throw SensorNotFound(id);
            }
            return Results.NoContent();
        }

        private static SensorDto ReadSensor(NpgsqlDataReader reader) => new SensorDto(reader.GetInt32(0),
            reader.GetString(1), reader.GetInt32(2), reader.GetFieldValue<DateTimeOffset>(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4));

        private static ApiException SensorNotFound(int id) => 
            new ApiException(404, "Not Found", $"Sensor {id} does not exist.");
    }
}
