using Microsoft.AspNetCore.WebUtilities;
using System.Security.Cryptography;
using System.Text;

namespace Backend.Common
{
    /// <summary>
    /// Creates and hashes random secrets (access tokens, sensor API keys).
    /// </summary>
    public static class Secrets
    {
        /// <summary>
        /// A random secret (access token, API key): 32 random bytes as 43 URL-safe characters.
        /// </summary>
        public static string NewSecret() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        /// <summary>
        /// SHA-256 of a text as 64 lowercase hex characters (how tokens and API keys are stored).
        /// </summary>
        public static string Hash(string text) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}
