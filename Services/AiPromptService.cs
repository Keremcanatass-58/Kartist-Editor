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

            var systemPrompt = $@"You are an expert background scenery analyst.
Your task is to extract ONLY the visual landscape/scenery keywords from the user's request for a background search.
The style is: {style ?? "Standard"}.

STRICT RULES:
1. IGNORE names of people, recipients, and the fact that it is a card or design.
2. FOCUS ONLY on the location, elements, and aesthetic.
3. OUTPUT ONLY 3-6 English keywords separated by commas.
4. MANDATORY: Include 'no text' and 'no letters' as the last keywords.
5. NO HUMANS, NO FACES. Scenery only.";

            return await SendChatRequestAsync(systemPrompt, $"User Request: {prompt}", 60, 0.3, cancellationToken);
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

