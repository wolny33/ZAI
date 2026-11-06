using Backend.Common;
using Npgsql;
using System.Globalization;
using System.Text.Json.Serialization;

namespace Backend.Endpoints
{
    public static class MeasurementEndpoints
    {
        private const string SelectMeasurements = "SELECT id, series_id, sensor_id, value, ts FROM measurements";
        private const int MaxMinutesInFuture = 5;
        private const int MinLimit = 1;
        private const int DefaultLimit = 1000;
        private const int MaxLimit = 10000;
        private record MeasurementDto(long Id, int SeriesId, 
            [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? SensorId, 
            double Value, DateTimeOffset Timestamp);
        private record MeasurementQuery(int[]? SeriesIds, DateTimeOffset? From, DateTimeOffset? To, bool Descending, int Limit);

        public static void Map(WebApplication app)
        {
            app.MapGet("/api/measurements", GetMeasurements);
            app.MapGet("/api/measurements/{id:long}", GetMeasurementById);
            app.MapPost("/api/measurements", CreateMeasurement);
        }

        // GET /api/measurements?series=1,2&from=...&to=...&sort=-timestamp&limit=100
        private static async Task<IResult> GetMeasurements(HttpContext ctx, NpgsqlDataSource ds)
        {
            var query = ReadMeasurementQuery(ctx.Request.Query);

            await using var list = ds.CreateCommand();
            var conditions = new List<string>();
            if (query.SeriesIds != null)
            {
                conditions.Add("series_id = ANY(@series_ids)");
                list.Parameters.AddWithValue("series_ids", query.SeriesIds);
            }
            if (query.From != null)
            {
                conditions.Add("ts >= @time_from");
                list.Parameters.AddWithValue("time_from", query.From.Value);
            }
            if (query.To != null)
            {
                conditions.Add("ts <= @time_to");
                list.Parameters.AddWithValue("time_to", query.To.Value);
            }

            var where = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";
            var direction = query.Descending ? "DESC" : "ASC";
            list.CommandText = SelectMeasurements + where + " ORDER BY ts " + direction + ", id " + direction + " LIMIT @limit";
            list.Parameters.AddWithValue("limit", query.Limit);

            await using var reader = await list.ExecuteReaderAsync();
            var result = new List<MeasurementDto>();
            while (await reader.ReadAsync())
            {
                result.Add(ReadMeasurement(reader));
            }
            return Results.Json(result);
        }

        // GET /api/measurements/{id}
        private static async Task<IResult> GetMeasurementById(long id, NpgsqlDataSource ds)
        {
            await using var get = ds.CreateCommand(SelectMeasurements + " WHERE id = @id");
            get.Parameters.AddWithValue("id", id);
            await using var reader = await get.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                throw new ApiException(404, "Not Found", $"Measurement {id} does not exist.");
            }
            return Results.Json(ReadMeasurement(reader));
        }

        // POST /api/measurements (sensor key in the X-API-Key header)
        private static async Task<IResult> CreateMeasurement(HttpContext ctx, NpgsqlDataSource ds, ILoggerFactory loggers)
        {
            var log = loggers.CreateLogger("Measurements");
            var sensor = await SensorAuth.Require(ctx, ds);

            var body = await HttpHelpers.ReadJsonObject(ctx);
            var validator = new RequestValidator(body);
            var value = validator.GetRequiredNumber("value");
            var timestamp = validator.GetOptionalTimestamp("timestamp");
            if (timestamp != null && timestamp > DateTimeOffset.UtcNow.AddMinutes(MaxMinutesInFuture))
            {
                log.LogWarning(
                    "Rejected timestamp {Timestamp} from sensor {SensorId}: more than {Minutes} minutes in the future.",
                    timestamp, sensor.SensorId, MaxMinutesInFuture);
                validator.AddRuleError("timestamp", 
                    $"Value must not be more than {MaxMinutesInFuture} minutes in the future.");
            }
            validator.ThrowIfInvalid();

            // Without a timestamp the measurement gets the current time of the server
            var time = timestamp ?? DateTimeOffset.UtcNow;

            await using var connection = await ds.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            // FOR SHARE keeps the range of the series unchanged until the commit
            // (PUT /api/series locks the row FOR UPDATE)
            double minValue;
            double maxValue;
            await using (var lockRow = new NpgsqlCommand(
                "SELECT min_value, max_value FROM series WHERE id = @id FOR SHARE", connection, transaction))
            {
                lockRow.Parameters.AddWithValue("id", sensor.SeriesId);
                await using var reader = await lockRow.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    // The series was deleted (together with its sensors) after the key was checked
                    throw SensorAuth.InvalidKey();
                }
                minValue = reader.GetDouble(0);
                maxValue = reader.GetDouble(1);
            }

            if (value < minValue || value > maxValue)
            {
                log.LogWarning(
                    "Rejected value {Value} from sensor {SensorId}: outside [{Min}, {Max}] of series {SeriesId}.",
                    value, sensor.SensorId, minValue, maxValue, sensor.SeriesId);
                throw new ApiException(422, "Unprocessable Content", "The request body breaks validation rules.",
                    [new FieldError("value", $"Value must be between {minValue} and {maxValue}.")]);
            }

            MeasurementDto measurement;
            try
            {
                await using var insert = new NpgsqlCommand(
                    "INSERT INTO measurements (ts, series_id, sensor_id, value) VALUES (@ts, @series_id, @sensor_id, @value) " +
                    "RETURNING id, series_id, sensor_id, value, ts", connection, transaction);
                insert.Parameters.AddWithValue("ts", time);
                insert.Parameters.AddWithValue("series_id", sensor.SeriesId);
                insert.Parameters.AddWithValue("sensor_id", sensor.SensorId);
                insert.Parameters.AddWithValue("value", value);
                await using var reader = await insert.ExecuteReaderAsync();
                await reader.ReadAsync();
                measurement = ReadMeasurement(reader);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
            {
                // The sensor was unregistered after the key was checked
                throw SensorAuth.InvalidKey();
            }

            await transaction.CommitAsync();
            return Results.Created("/api/measurements/" + measurement.Id, measurement);
        }


        /// <summary>
        /// Reads and checks the query string of GET /api/measurements, every wrong parameter is listed.
        /// </summary>
        private static MeasurementQuery ReadMeasurementQuery(IQueryCollection query)
        {
            var errors = new List<FieldError>();

            // Comma-separated series ids, e.g. "1,2,5"; missing or empty means all series
            int[]? seriesIds = null;
            var seriesText = query["series"].ToString();
            if (seriesText.Length > 0)
            {
                var ids = new List<int>();
                foreach (var part in seriesText.Split(','))
                {
                    if (int.TryParse(part.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                    {
                        ids.Add(id);
                    }
                    else
                    {
                        errors.Add(new FieldError("series", "Value must be a comma-separated list of series ids."));
                        break;
                    }
                }
                seriesIds = ids.ToArray();
            }

            var from = ReadDateTimeParameter(query, "from", errors);
            var to = ReadDateTimeParameter(query, "to", errors);

            var sort = query["sort"].ToString();
            var descending = sort == "-timestamp";
            if (sort.Length > 0 && sort != "timestamp" && !descending)
            {
                errors.Add(new FieldError("sort", "Value must be timestamp or -timestamp."));
            }

            var limit = DefaultLimit;
            var limitText = query["limit"].ToString();
            if (limitText.Length > 0)
            {
                if (!int.TryParse(limitText, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit < MinLimit || limit > MaxLimit)
                {
                    errors.Add(new FieldError("limit", $"Value must be an integer from {MinLimit} to {MaxLimit}."));
                }
            }

            if (errors.Count > 0)
            {
                throw new ApiException(400, "Bad Request", "The query string is invalid.", errors);
            }
            return new MeasurementQuery(seriesIds, from, to, descending, limit);
        }

        private static DateTimeOffset? ReadDateTimeParameter(IQueryCollection query, string name, List<FieldError> errors)
        {
            var text = query[name].ToString();
            if (text.Length == 0)
            {
                return null;
            }
            if (!DateTimeParsing.TryParse(text, out var time))
            {
                errors.Add(new FieldError(name, "Value must be a date-time, e.g. 2026-10-15T08:30:00Z."));
                return null;
            }
            return time;
        }

        private static MeasurementDto ReadMeasurement(NpgsqlDataReader reader) => new MeasurementDto(
            reader.GetInt64(0), reader.GetInt32(1), reader.IsDBNull(2) ? null : reader.GetInt32(2),
            reader.GetDouble(3), reader.GetFieldValue<DateTimeOffset>(4));
    }
}
