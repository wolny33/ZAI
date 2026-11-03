namespace Backend.Common
{
    /// <summary>
    /// An error that is returned to the client as an RFC 9457 response.
    /// </summary>
    public sealed class ApiException: Exception
    {
        public int Status { get; }
        public string Title { get; }
        public string? Detail { get; }
        public IEnumerable<FieldError>? Errors { get; }

        /// <summary>
        /// Value of the WWW-Authenticate header (used with status 401).
        /// </summary>
        public string? WwwAuthenticate { get; }

        public ApiException(int status, string title, string? detail = null, IEnumerable<FieldError>? errors = null, 
            string? wwwAuthenticate = null): base(detail ?? title)
        {
            Status = status;
            Title = title;
            Detail = detail;
            Errors = errors;
            WwwAuthenticate = wwwAuthenticate;
        }
    }
}
