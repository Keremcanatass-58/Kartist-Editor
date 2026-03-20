using System.Text.RegularExpressions;
using System.Web;

namespace Kartist.Helpers
{
    public static class InputValidator
    {
        public static string SanitizeHtml(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            input = Regex.Replace(input, @"<script[^>]*>[\s\S]*?</script>", string.Empty, RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"<iframe[^>]*>[\s\S]*?</iframe>", string.Empty, RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"on\w+\s*=\s*""[^""]*""", string.Empty, RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"on\w+\s*=\s*'[^']*'", string.Empty, RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"javascript\s*:", string.Empty, RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"<[^>]+>", string.Empty);
            return input.Trim();
        }

        public static string SanitizeForDisplay(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return HttpUtility.HtmlEncode(input);
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

        public static bool IsValidPrompt(string input)
        {
            if (string.IsNullOrEmpty(input)) return false;
            if (input.Length > 1000) return false;

            string[] xssPatterns = { "<script", "javascript:", "onerror=", "onload=", "<iframe" };
            string lower = input.ToLower();
            foreach (var p in xssPatterns)
            {
                if (lower.Contains(p)) return false;
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


