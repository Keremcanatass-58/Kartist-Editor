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

            return await SendChatRequestAsync(systemPrompt, $"Request: {prompt}", 200, 0.7, null, cancellationToken);
        }

        public async Task<string> GenerateDesignSuggestionJsonAsync(string prompt, string kategori, string style, string history = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(prompt) || !HasConfiguredProvider())
            {
                return null;
            }

            var systemPrompt = $@"Sen üst düzey bir Yapay Zeka Tasarım Motorusun. Amacın, kullanıcının girdiği promptu KELİME KELİME, HECE HECE okumak, tam olarak ne istediğini eksiksiz bir şekilde anlamak ve hiçbir detayı atlamadan %100 hedefe yönelik bir tasarım JSON'ı çıkarmaktır.

## ADIM 1: KELİME KELİME ANALİZ
- Kullanıcının yazdığı HER BİR kelimeyi analiz et. Özel bir yer (örn: İstanbul), özel bir kişi (örn: Anne), özel bir nesne (örn: Papatya) geçiyorsa bunu ASLA görmezden gelme!
- İstenen asıl konsept ne? (Doğum günü, özür, aşk mektubu, kutlama vb.)
- Kullanıcı spesifik bir şey istemişse (örn: 'istanbullu', 'güllü', 'deniz manzaralı') bunu Pexels görsel aramasında (revisedImagePrompt) EN BAŞA koy.

## ADIM 2: ÜRETİM VE MUTLAK KURALLAR
1. ÇIKTI FORMATI: SADECE geçerli bir JSON döndüreceksin. Başka hiçbir şey yazma.
2. tema: Kullanıcının yazdıklarını kelimesi kelimesine özetleyen çok güçlü bir başlık (max 4 kelime).
3. anaMetin: Kullanıcının girdiği tüm detayları (mekan, kişi, olay vb.) zekice harmanlayan, etkileyici, duygu yüklü özel bir metin (2-4 cümle). Asla sıradan, jenerik metinler yazma. Prompttaki her detayı metne yedir.
4. yaziFontu: İçeriğe en uygun font: ""Poppins"", ""Montserrat"", ""Inter"", ""Playfair Display"", ""Roboto"", ""Raleway"", ""Lora"", ""Oswald""
5. renkPaleti: Atmosfere tam uygun 3 HEX renk: [""#Koyu_Arkaplan"", ""#Parlak_Vurgu"", ""#Yumusak_Ton""]
6. layoutStyle: ""modern"", ""minimal"", ""bold"", ""elegant""
7. emojiler: Metinle %100 uyumlu max 3 emoji.
8. revisedImagePrompt: Pexels API için İNGİLİZCE fotoğraf arama kelimeleri. DİKKAT: Kullanıcı 'mektup', 'kart', 'mesaj' dese bile, KESİNLİKLE 'letter', 'card', 'reading', 'paper', 'person' gibi şeyler ARAMA! SADECE MİMARİ, DOĞA, MANZARA veya OBJE ara. (Örn: Kullanıcı İstanbul dediyse sadece ""istanbul bosphorus romantic"" yaz. Sadece arka plan olabilecek estetik konseptler yaz.)

## JSON FORMATI:
{{
  ""renkPaleti"": [""#koyu_arka_plan"", ""#parlak_vurgu"", ""#yumusak_ton""],
  ""tema"": ""Kısa ve Güçlü Başlık"",
  ""yaziFontu"": ""Playfair Display"",
  ""layoutStyle"": ""elegant"",
  ""anaMetin"": ""Kullanıcının verdiği her detayı içeren mükemmel mesaj."",
  ""emojiler"": [""🌹"", ""✨""],
  ""revisedImagePrompt"": ""english keywords for real photography search""
}}";

            return await SendChatRequestAsync(systemPrompt, $"Kullanıcı isteği: {prompt}", 500, 0.8, history, cancellationToken);
        }

        public async Task<string> GenerateTextAsync(string category, string prompt, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(prompt) || !HasConfiguredProvider())
            {
                return null;
            }

            var systemPrompt = $@"Sen yaratıcı ve duygusal metinler yazan bir AI yazarısın. Kullanıcı bir tasarım içine eklemek için bir metin istiyor.
Kategori: {category}
Kullanıcı İsteği: {prompt}
GÖREVİN: Kullanıcının isteğine uygun, etkileyici, duygu yüklü ve tasarımda güzel duracak 2-3 cümlelik harika bir metin oluştur. 
SADECE ÜRETTİĞİN METNİ DÖNDÜR. Başka hiçbir açıklama, yorum veya tırnak işareti ekleme.";

            return await SendChatRequestAsync(systemPrompt, $"Metin üret: {prompt}", 300, 0.8, null, cancellationToken);
        }

        private async Task<string> SendChatRequestAsync(string systemPrompt, string userPrompt, int maxTokens, double temperature, string historyJson = null, CancellationToken cancellationToken = default)
        {
            var endpoint = GetProviderEndpoint();
            var apiKey = GetProviderApiKey();
            var model = GetProviderModel();
            var provider = ResolveProviderName();

            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(model))
            {
                return null;
            }

            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(Math.Max(5, _options.TimeoutSeconds))
            };

            // Gemini OpenAI-compat endpoint: API key goes as query parameter
            // Groq & OpenAI: API key goes as Bearer token
            string requestUrl;
            if (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
            {
                // Gemini uses API key as query parameter on its OpenAI-compatible endpoint
                var separator = endpoint.Contains("?") ? "&" : "?";
                requestUrl = $"{endpoint}{separator}key={apiKey}";
            }
            else
            {
                requestUrl = endpoint;
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

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
                requestUrl,
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                System.Diagnostics.Debug.WriteLine($"[AiPromptService] {provider} error ({response.StatusCode}): {errorBody}");
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
            return string.IsNullOrWhiteSpace(configured) ? "Gemini" : configured;
        }

        private string GetProviderApiKey()
        {
            var provider = ResolveProviderName();

            if (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
            {
                return _configuration["Gemini:ApiKey"]
                    ?? _configuration["GEMINI_API_KEY"]
                    ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                    ?? string.Empty;
            }

            if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                return _configuration["OpenAI:ApiKey"]
                    ?? _configuration["OPENAI_API_KEY"]
                    ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                    ?? string.Empty;
            }

            // Default: Groq
            return _configuration["Groq:ApiKey"]
                ?? _configuration["GROQ_API_KEY"]
                ?? Environment.GetEnvironmentVariable("GROQ_API_KEY")
                ?? string.Empty;
        }

        private string GetProviderEndpoint()
        {
            var provider = ResolveProviderName();

            if (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
                return _options.GeminiEndpoint;

            if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
                return _options.OpenAiChatEndpoint;

            return _options.GroqEndpoint;
        }

        private string GetProviderModel()
        {
            var provider = ResolveProviderName();

            if (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
                return _options.GeminiModel;

            if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
                return _options.OpenAiChatModel;

            return _options.GroqModel;
        }
    }
}
