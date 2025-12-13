using System;
using System.Security.Cryptography;
using System.Text;

namespace OnlineContract.Helpers
{
    public static class PasswordHelper
    {
        // PBKDF2 parameters
        private const int Iterations = 100_000;
        private const int SaltSize = 16; // 128-bit
        private const int KeySize = 32; // 256-bit

        public static string HashPassword(string password)
        {
            if (password is null)
                throw new ArgumentNullException(nameof(password));

            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var passwordBytes = Encoding.UTF8.GetBytes(password);
            // Use the static Pbkdf2 API that returns a derived key byte[]
            var subkey = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
            return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(subkey)}";
        }

        public static bool VerifyPassword(string inputPassword, string storedHash)
        {
            if (string.IsNullOrEmpty(storedHash))
                return false;

            if (inputPassword is null)
                return false;

            // New format: pbkdf2$iterations$saltBase64$subkeyBase64
            if (storedHash.StartsWith("pbkdf2$", StringComparison.Ordinal))
            {
                // trim to avoid issues with trailing newlines/whitespace from storage
                storedHash = storedHash.Trim();

                // limit split to 4 parts in case of unexpected extra separators
                var parts = storedHash.Split(new[] { '$' }, 4);
                if (parts.Length != 4)
                    return false;

                if (!int.TryParse(parts[1], out var iterations))
                    return false;

                try
                {
                    var salt = Convert.FromBase64String(parts[2]);
                    var expectedSubkey = Convert.FromBase64String(parts[3]);

                    var passwordBytes = Encoding.UTF8.GetBytes(inputPassword);
                    var actualSubkey = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, iterations, HashAlgorithmName.SHA256, expectedSubkey.Length);

                    return CryptographicOperations.FixedTimeEquals(actualSubkey, expectedSubkey);
                }
                catch (FormatException)
                {
                    return false;
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }

            // Legacy: plain SHA-256 base64 - keep compatibility
            using var sha256 = SHA256.Create();
            var inputBytes = Encoding.UTF8.GetBytes(inputPassword);
            var hash = sha256.ComputeHash(inputBytes);
            var inputBase64 = Convert.ToBase64String(hash);
            return inputBase64 == storedHash;
        }
    }
}