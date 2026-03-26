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

            var systemPrompt = $@"You are a professional designer. Translate the user's request into a highly descriptive English image prompt for a greeting card background.
RULES:
- NO LANDSCAPES (No mountains, deserts, canyons).
- FOCUS ON THE SUBJECT: If they ask for flowers (daisies, roses), the prompt must be about flowers.
- STYLE: {style}.
- Output ONLY the prompt in English. End with ', high quality, aesthetic, no text, no letters'.";

            return await SendChatRequestAsync(systemPrompt, $"Request: {prompt}", 200, 0.7, cancellationToken);
        }

        public async Task<string> GenerateDesignSuggestionJsonAsync(string prompt, string kategori, string style, string history = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(prompt) || !HasConfiguredProvider())
            {
                return null;
            }

            var systemPrompt = $@"
Kullanıcı için estetik bir kart tasarımı kurgula. 
NOT: Eğer kullanıcı çiçek, obje veya özel bir renk belirtirse mutlaka onu kullan. Alakasız manzara resimleri önerme.

JSON formatında cevap ver:
{{
  ""renkPaleti"": [""#renk1"", ""#renk2"", ""#renk3""],
  ""tema"": ""Kısa Başlık"",
  ""yaziFontu"": ""Poppins veya Montserrat"",
  ""layoutStyle"": ""modern"",
  ""anaMetin"": ""Duygusal mesaj"",
  ""emojiler"": [""😊"", ""🎉"", ""✨""],
  ""revisedImagePrompt"": ""Arka plan için İngilizce detaylı prompt (Örn: 'aesthetic white daisy pattern background')""
}}

Kurallar:
- Sadece JSON.
- Dil: Kullanıcı dili.";

            return await SendChatRequestAsync(systemPrompt, $"Istek: {prompt}", 500, 0.7, history, cancellationToken);
        }

        private async Task<string> SendChatRequestAsync(string systemPrompt, string userPrompt, int maxTokens, double temperature, string historyJson = null, CancellationToken cancellationToken = default)
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

            var messages = new List<object>
            {
                new { role = "system", content = systemPrompt }
            };

            // Parse and add history if present
            if (!string.IsNullOrWhiteSpace(historyJson))
            {
                try
                {
                    var history = JsonSerializer.Deserialize<List<JsonElement>>(historyJson);
                    if (history != null)
                    {
                        foreach (var msg in history)
                        {
                            messages.Add(msg);
                        }
                    }
                }
                catch { /* Ignore malformed history */ }
            }

            messages.Add(new { role = "user", content = userPrompt });

            var payload = new
            {
                model,
                messages,
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
                ?.Trim();
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

