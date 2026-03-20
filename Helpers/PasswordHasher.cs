namespace Kartist.Helpers
{
    public static class PasswordHasher
    {
        public static string HashPassword(string plainPassword)
        {
            return BCrypt.Net.BCrypt.HashPassword(plainPassword, workFactor: 12);
        }

        public static bool VerifyPassword(string plainPassword, string hashedPassword)
        {
            try
            {
                return BCrypt.Net.BCrypt.Verify(plainPassword, hashedPassword);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsHashed(string password)
        {
            return !string.IsNullOrEmpty(password) && password.StartsWith("$2");
        }
    }
}
