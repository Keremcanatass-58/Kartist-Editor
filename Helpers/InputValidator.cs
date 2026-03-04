using System.Text.RegularExpressions;

namespace Kartist.Helpers
{
    public static class InputValidator
    {
        public static string SanitizeHtml(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            
            string pattern = @"<[^>]+>";
            return Regex.Replace(input, pattern, string.Empty);
        }

        public static bool IsValidInput(string input)
        {
            if (string.IsNullOrEmpty(input)) return true;
            
            string[] dangerousChars = { "'", "\"", ";", "--", "/*", "*/", "xp_", "sp_", "exec", "execute", "union", "select", "insert", "update", "delete", "drop", "create", "alter" };
            
            string lowerInput = input.ToLower();
            foreach (var dangerous in dangerousChars)
            {
                if (lowerInput.Contains(dangerous))
                {
                    return false;
                }
            }
            
            return true;
        }

        public static bool IsValidEmail(string email)
        {
            if (string.IsNullOrEmpty(email)) return false;
            
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsValidImageFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            
            string[] allowedExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
            string extension = Path.GetExtension(fileName).ToLower();
            
            return allowedExtensions.Contains(extension);
        }

        public static bool IsValidAudioFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            
            string[] allowedExtensions = { ".mp3", ".wav", ".ogg", ".m4a" };
            string extension = Path.GetExtension(fileName).ToLower();
            
            return allowedExtensions.Contains(extension);
        }

        public static bool IsValidLength(string input, int minLength, int maxLength)
        {
            if (input == null) return minLength == 0;
            return input.Length >= minLength && input.Length <= maxLength;
        }

        public static bool IsValidInteger(string input, out int value)
        {
            return int.TryParse(input, out value);
        }

        public static bool IsValidDecimal(string input, out decimal value)
        {
            return decimal.TryParse(input, out value);
        }
    }
}


