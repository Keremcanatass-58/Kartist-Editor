using System.Text.RegularExpressions;

namespace Kartist.Services
{
    public class AiModerationResult
    {
        public bool IsToxic { get; set; }
        public string Message { get; set; }
    }

    public class AiModerationService
    {
        // Simple local AI moderation based on stop words and regexes
        private readonly List<string> _toxicWords = new List<string>
        {
            "aptal", "gerizekalı", "salak", "lanet", "şerefsiz", 
            "orospu", "piç", "amk", "aq", "siktir", 
            "ibne", "gavat", "kaltak", "mal"
        };

        public async Task<AiModerationResult> AnalyzeContentAsync(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return new AiModerationResult { IsToxic = false };

            var loweredContent = content.ToLowerInvariant();

            // Simulate AI thinking time to make it feel like a heavy AI operation
            await Task.Delay(300);

            // 1. Check direct matches
            foreach (var word in _toxicWords)
            {
                // Regex for exact word match with standard boundaries
                if (Regex.IsMatch(loweredContent, $@"\b{word}\b", RegexOptions.IgnoreCase))
                {
                    return new AiModerationResult
                    {
                        IsToxic = true,
                        Message = "İçeriğiniz Kartist AI tarafından 'Nefret Söylemi veya Hakaret' olarak algılandı."
                    };
                }
                
                // Also check for simple variations (e.g., repeating chars) -> a.m.k, s a l a k
                var spacedOut = string.Join(@"[.\s\-_]*", word.ToCharArray());
                if (Regex.IsMatch(loweredContent, spacedOut, RegexOptions.IgnoreCase))
                {
                    return new AiModerationResult
                    {
                        IsToxic = true,
                        Message = "Kartist AI: Topluluk kurallarına uymayan manipüle edilmiş argo içerik tespit edildi."
                    };
                }
            }

            // 2. Spam check (repeating same character too many times)
            if (Regex.IsMatch(loweredContent, @"(.)\1{10,}"))
            {
                return new AiModerationResult
                {
                    IsToxic = true,
                    Message = "Kartist AI: İçeriğiniz spam (tekrar eden karakterler) şüphesi nedeniyle engellendi."
                };
            }

            return new AiModerationResult { IsToxic = false, Message = "OK" };
        }
    }
}
