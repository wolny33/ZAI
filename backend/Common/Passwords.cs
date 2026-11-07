namespace Backend.Common
{
    /// <summary>
    /// Hashes and checks administrator passwords with bcrypt.
    /// </summary>
    public static class Passwords
    {
        private const int WorkFactor = 12;

        // Checked when the username does not exist, so that an unknown user takes as long
        // to reject as a known one (the response time must not reveal which usernames exist).
        private static readonly string DummyHash = Hash("dummy-password");

        public static string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

        /// <summary>
        /// Checks a password against a stored hash, returns false when there is no hash.
        /// </summary>
        public static bool Verify(string password, string? hash)
        {
            // bcrypt always runs (against DummyHash for an unknown user)
            var matches = BCrypt.Net.BCrypt.Verify(password, hash ?? DummyHash);
            return matches && hash != null;
        }
    }
}
