using System;
using System.Security.Cryptography;

namespace CST.Avalonia.Services.LocalApi
{
    /// <summary>Bearer-token generation for the loopback API. Shared by the server and the "rotate token" action.</summary>
    internal static class ApiToken
    {
        /// <summary>A URL-safe 256-bit random bearer token.</summary>
        public static string Generate()
        {
            Span<byte> bytes = stackalloc byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }
    }
}
