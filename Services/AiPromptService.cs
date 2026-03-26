using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Kartist.Models;
using Microsoft.Extensions.Options;

namespace Kartist.Services
{
    public class AiPromptService : IAiPromptService
    {
        private readonly AiOptions _options;
        private readonly IConfiguration _configuration;

        public AiPromptService(IOptions<AiOptions> options, IConfiguration configuration)
        {
            _options = options.Value;
            _configuration = configuration;
        }

        public bool HasConfiguredProvider()
        {
            return !string.IsNullOrWhiteSpace(GetProviderApiKey());
        }

        public string GetConfiguredProviderName()
        {
            return ResolveProviderName();
        }

        public async Task<string> RewriteImagePromptAsync(string prompt, string style, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(prompt) || !HasConfiguredProvider())
            {
                return null;
            }

            var systemPrompt = $@"You are an expert AI image generation prompt engineer.
Your task is to translate and enhance the user's request into a highly descriptive English prompt for an image generator.
The requested visual style is: {style ?? "Standard"}.

STRICT RULES:
1. Translate the user's request faithfully to English.
2. Keep ALL specific subjects mentioned by the user (people, faces, characters, objects, etc.). Do NOT remove them.
3. Add relevant lighting, aesthetic, and photographic/artistic keywords to match the requested style.
4. Output ONLY the final English prompt. No conversational text.
5. MANDATORY: End the prompt with the exact keywords: ', no text, no watermark, no letters'.";

            return await SendChatRequestAsync(systemPrompt, $"User Request: {prompt}", 200, 0.7, cancellationToken);
        }

        public async Task<string> GenerateDesignSuggestionJsonAsync(string prompt, string kategori, string style, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(prompt) || !HasConfiguredProvider())
            {
                return null;
            }

            var systemPrompt = $@"
Sen, Kartist platformu icin calisan, dunyaca unlu bir bas tasarimcisin.
Gorevin, kullanicinin girdigi prompt'a gore duygusal derinligi olan, estetik ve kisilestirilmis bir kart tasarimi kurgulamak.

Kullanici su gorsel tarzi tercih etti: {style ?? "Modern"}.
Kategori: {kategori ?? "Genel"}.

Sadece gecerli JSON dondur:
{{
  ""renkPaleti"": [""#ArkaPlan"", ""#Panel"", ""#Vurgu""],
  ""tema"": ""Vurucu bir baslik"",
  ""yaziFontu"": ""Poppins, Montserrat, Roboto, Playfair Display veya Inter arasindan sec"",
  ""layoutStyle"": ""minimal, bold, elegant veya modern"",
  ""anaMetin"": ""1-3 cumlelik etkileyici mesaj"",
  ""emojiler"": [""*"", ""*"", ""*""]
}}

Kurallar:
- Sadece JSON ver.
- Dil: Kullanici hangi dilde yazarsa o dilde cevap ver.";

            return await SendChatRequestAsync(systemPrompt, $"Istek: {prompt}", 500, 0.7, cancellationToken);
        }

        private async Task<string> SendChatRequestAsync(string systemPrompt, string userPrompt, int maxTokens, double temperature, CancellationToken cancellationToken)
        {
            var endpoint = GetProviderEndpoint();
            var apiKey = GetProviderApiKey();
            var model = GetProviderModel();

            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(model))
            {
                return null;
            }

            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(Math.Max(5, _options.TimeoutSeconds))
            };

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var payload = new
            {
                model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature,
                max_tokens = maxTokens
            };

            using var response = await client.PostAsync(
                endpoint,
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
            using var jsonDoc = JsonDocument.Parse(responseString);
            return jsonDoc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString()
                ?.Trim()
                .Replace("\"", string.Empty);
        }

        private string ResolveProviderName()
        {
            var configured = _options.PromptProvider?.Trim();
            return string.IsNullOrWhiteSpace(configured) ? "Groq" : configured;
        }

        private string GetProviderApiKey()
        {
            var provider = ResolveProviderName();
            if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                return _configuration["OpenAI:ApiKey"]
                    ?? _configuration["OPENAI_API_KEY"]
                    ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                    ?? string.Empty;
            }

            return _configuration["Groq:ApiKey"]
                ?? _configuration["GROQ_API_KEY"]
                ?? Environment.GetEnvironmentVariable("GROQ_API_KEY")
                ?? string.Empty;
        }

        private string GetProviderEndpoint()
        {
            var provider = ResolveProviderName();
            return provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)
                ? _options.OpenAiChatEndpoint
                : _options.GroqEndpoint;
        }

        private string GetProviderModel()
        {
            var provider = ResolveProviderName();
            return provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)
                ? _options.OpenAiChatModel
                : _options.GroqModel;
        }
    }
}

