namespace Kartist.Models
{
    public class AiOptions
    {
        public string ImageProvider { get; set; } = "Pollinations";
        public string PromptProvider { get; set; } = "Groq";
        public int TimeoutSeconds { get; set; } = 25;
        public int MaxImageBytes { get; set; } = 5242880;
        public string PollinationsEndpoint { get; set; } = "https://image.pollinations.ai/prompt";
        public string PollinationsModel { get; set; } = "flux";
        public string GroqEndpoint { get; set; } = "https://api.groq.com/openai/v1/chat/completions";
        public string GroqModel { get; set; } = "llama-3.3-70b-versatile";
        public string OpenAiChatEndpoint { get; set; } = "https://api.openai.com/v1/chat/completions";
        public string OpenAiChatModel { get; set; } = "gpt-4o-mini";
        public string OpenAiImageEndpoint { get; set; } = "https://api.openai.com/v1/images/generations";
        public string OpenAiImageModel { get; set; } = "gpt-image-1";
    }
}
