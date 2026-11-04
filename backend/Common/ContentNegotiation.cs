namespace Backend.Common
{
    public static class ContentNegotiation
    {
        private static readonly string[] JsonTypes = { "*/*", "application/*", "application/json",
            "application/problem+json" };
        public static bool AcceptsJson(HttpRequest request)
        {
            var accept = request.GetTypedHeaders().Accept;
            return accept.Count == 0 || accept.Any(type => type.Quality != 0 &&
                JsonTypes.Contains(type.MediaType.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase));
        }
    }
}
