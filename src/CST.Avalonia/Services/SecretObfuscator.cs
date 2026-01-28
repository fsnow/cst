using System;
using System.Text;

namespace CST.Avalonia.Services
{
    /// <summary>
    /// Simple XOR-based obfuscation to prevent credentials from appearing
    /// as plaintext in the binary. This is NOT encryption - it only deters
    /// casual inspection with tools like 'strings' or hex editors.
    /// </summary>
    internal static class SecretObfuscator
    {
        // XOR key split across multiple parts to make it slightly harder to find
        private static readonly string[] KeyParts = { "CST", "Reader", "2025", "Pali" };

        private static string Key => string.Concat(KeyParts);

        /// <summary>
        /// Obfuscates a plaintext string. Use this once to generate the encoded
        /// value, then store that value in the code.
        /// </summary>
        public static string Obfuscate(string plaintext)
        {
            if (string.IsNullOrEmpty(plaintext))
                return string.Empty;

            var key = Key;
            var result = new char[plaintext.Length];
            for (int i = 0; i < plaintext.Length; i++)
            {
                result[i] = (char)(plaintext[i] ^ key[i % key.Length]);
            }
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(result));
        }

        /// <summary>
        /// Deobfuscates an encoded string back to plaintext at runtime.
        /// </summary>
        public static string Deobfuscate(string obfuscated)
        {
            if (string.IsNullOrEmpty(obfuscated))
                return string.Empty;

            var key = Key;
            var bytes = Convert.FromBase64String(obfuscated);
            var chars = Encoding.UTF8.GetString(bytes);
            var result = new char[chars.Length];
            for (int i = 0; i < chars.Length; i++)
            {
                result[i] = (char)(chars[i] ^ key[i % key.Length]);
            }
            return new string(result);
        }
    }
}
