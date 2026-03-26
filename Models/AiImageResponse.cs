namespace Kartist.Models
{
    public class AiImageResponse
    {
        public bool Success { get; set; }
        public string DataUrl { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
        public string RevisedPrompt { get; set; } = string.Empty;
        public List<string> Warnings { get; set; } = new();
        public string Error { get; set; } = string.Empty;
    }
}
