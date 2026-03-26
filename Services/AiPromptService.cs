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

            var systemPrompt = $@"You are an elite AI image prompt specialist. 
Your goal is to create a STUNNING visual background for a greeting card.

STRICT GUIDELINES:
1. NO GENERIC LANDSCAPES: Do NOT generate mountains, canyons, or forests unless the user specifically asks for them. 
2. THEME-FIRST: If the user mentions an object (e.g., 'papatya'/daisy, 'balon'/balloon, 'kalp'/heart), that object MUST be the main visual element.
3. CARD AESTHETIC: Focus on 'graphic design', 'aesthetic patterns', 'soft bokeh', and 'artistic compositions'.
4. STYLE: Apply the '{style}' style with high artistic fidelity.
5. TRANSLATION: User asked: '{prompt}'. Translate and enrich this into a professional prompt.
6. NO TEXT: MANDATORY - Output ONLY the English prompt. End with: ', no text, no watermark, no letters'.";

            return await SendChatRequestAsync(systemPrompt, $"Creative Request: {prompt}", 200, 0.7, cancellationToken);
        }

        public async Task<string> GenerateDesignSuggestionJsonAsync(string prompt, string kategori, string style, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(prompt) || !HasConfiguredProvider())
            {
                return null;
            }

            var systemPrompt = $@"
Sen bir grafik tasarım dehasısın. Kullanıcıya 'VAY BE' dedirtecek bir kart tasarımı oluşturacaksın.

KRİTİK TALİMATLAR:
1. GÖRSEL TUTARLILIK: Kullanıcı 'papatya' diyorsa, resim komutu mutlaka papatya içermeli, renkler sarı-beyaz-yeşil olmalı.
2. ASLA MANZARA YAPMA: Doğum günü kartı için kanyon, dağ, taş gibi alakasız manzaralar ÖNERME. Onun yerine estetik desenler, ilgili objeler ve şık kompozisyonlar kullan.
3. KİŞİSELLEŞTİRME: 'Anne', 'Sevgili', 'Arkadaş' gibi hitapları 'anaMetin' ve 'tema'da samimi bir dille kullan.

Sadece geçerli JSON:
{{
  ""renkPaleti"": [""#ArkaPlan"", ""#Panel"", ""#Vurgu""],
  ""tema"": ""Kullanıcı isteğine özel, vurucu başlık"",
  ""yaziFontu"": ""Poppins, Montserrat veya Playfair Display"",
  ""layoutStyle"": ""minimal, bold veya modern"",
  ""anaMetin"": ""Duygusal ve etkileyici 1-3 cümle"",
  ""emojiler"": [""emoji1"", ""emoji2"", ""emoji3""],
  ""revisedImagePrompt"": ""Aesthetic background prompt in English (EX: 'soft aesthetic daisy field background, bokeh, pastel colors, high quality')""
}}

Kurallar:
- Sadece JSON. Başka yazı ekleme.
- Dil: Kullanıcı diliyle cevap ver.";

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

