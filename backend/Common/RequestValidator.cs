using System.Text.Json;

namespace Backend.Common
{
    /// <summary>
    /// Reads fields from a JSON request body and collects all problems found,
    /// so that one response can report every invalid field at once.
    /// </summary>
    public sealed  class RequestValidator
    {
        private readonly JsonElement _body;

        // Missing fields and wrong types (answered with 400)
        private readonly List<FieldError> _errors = new();

        // Well-formed values that break a rule, e.g. a text that is too short (answered with 422)
        private readonly List<FieldError> _ruleErrors = new();


        public RequestValidator(JsonElement body) => _body = body;

        /// <summary>
        /// Reads a required text field. Records an error and 
        /// returns an empty string if it is missing or not a string.
        /// </summary>
        public string GetRequiredString(string field, int minLength = 0, int maxLength = int.MaxValue)
        {
            if (!_body.TryGetProperty(field, out var value))
            {
                _errors.Add(new FieldError(field, "Field is required."));
                return string.Empty;
            }
            if (value.ValueKind != JsonValueKind.String)
            {
                _errors.Add(new FieldError(field, "Value must be a string."));
                return string.Empty;
            }

            var text = value.GetString() ?? string.Empty;
            CheckLength(field, text, minLength, maxLength);
            return text;
        }

        /// <summary>
        /// Reads an optional text field: null if it is missing or null.
        /// </summary>
        public string? GetOptionalString(string field, int maxLength = int.MaxValue)
        {
            if (!_body.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }
            if (value.ValueKind != JsonValueKind.String)
            {
                _errors.Add(new FieldError(field, "Value must be a string or null."));
                return null;
            }

            var text = value.GetString() ?? string.Empty;
            CheckLength(field, text, 0, maxLength);
            return text;
        }

        /// <summary>
        /// Reads a required number. On a problem an error is recorded and 0 is returned.
        /// </summary>
        public double GetRequiredNumber(string field)
        {
            if (!_body.TryGetProperty(field, out var value))
            {
                _errors.Add(new FieldError(field, "Field is required."));
                return 0;
            }
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number))
            {
                _errors.Add(new FieldError(field, "Value must be a number."));
                return 0;
            }
            return number;
        }

        /// <summary>
        /// Reads a required integer number. On a problem an error is recorded and 0 is returned.
        /// </summary>
        public int GetRequiredInteger(string field)
        {
            if (!_body.TryGetProperty(field, out var value))
            {
                _errors.Add(new FieldError(field, "Field is required."));
                return 0;
            }
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
            {
                _errors.Add(new FieldError(field, "Value must be an integer."));
                return 0;
            }
            return number;
        }

        /// <summary>
        /// Reads an optional date-time: null if it is missing or null.
        /// </summary>
        public DateTimeOffset? GetOptionalTimestamp(string field)
        {
            if (!_body.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }
            if (value.ValueKind != JsonValueKind.String || 
                !DateTimeParsing.TryParse(value.GetString() ?? string.Empty, out var timestamp))
            {
                _errors.Add(new FieldError(field, "Value must be a date-time, e.g. 2026-10-15T08:30:00Z."));
                return null;
            }
            return timestamp;
        }

        /// <summary>
        /// Records a broken rule that the other methods cannot check (answered with 422).
        /// </summary>
        public void AddRuleError(string field, string message) => _ruleErrors.Add(new FieldError(field, message));

        /// <summary>
        /// Throws a 400 ApiException listing all recorded errors.
        /// </summary>
        public void ThrowIfInvalid()
        {
            if (_errors.Count > 0)
            {
                throw new ApiException(400, "Bad Request", "One or more fields in request body are invalid.", _errors);
            }
            if (_ruleErrors.Count > 0)
            {
                throw new ApiException(422, "Unprocessable Content", "The request body breaks validation rules.", _ruleErrors);
            }

        }

        private void CheckLength(string field, string text, int minLength, int maxLength)
        {
            if (text.Length < minLength)
            {
                _ruleErrors.Add(new FieldError(field, $"Value must be at least {minLength} characters long."));
            }
            if (text.Length > maxLength)
            {
                _ruleErrors.Add(new FieldError(field, $"Value must be at most {maxLength} characters long."));
            }
        }
    }

}
