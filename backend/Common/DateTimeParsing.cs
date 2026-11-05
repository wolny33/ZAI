using System.Globalization;

namespace Backend.Common
{
    public static class DateTimeParsing
    {
        /// <summary>
        /// Parses a date and time (e.g. 2026-10-15T08:30:00Z). A value without a time zone is treated as UTC.
        /// The result is always in UTC, which is the only offset that Npgsql writes to a timestamptz column.
        /// </summary>
        public static bool TryParse(string text, out DateTimeOffset timestamp) => 
            DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal | 
                DateTimeStyles.AdjustToUniversal, out timestamp);
    }
}
