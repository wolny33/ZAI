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
        /// Reads a required text field. Records an error and returns null if it is missing or not a string.
        /// </summary>
        public string GetRequiredString(string field, int minLength = 0)
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
            if (text.Length < minLength)
            {
                _ruleErrors.Add(new FieldError(field, $"Value must be at least {minLength} characters long."));
            }

            return value.GetString() ?? string.Empty;
        }

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
    }

}
