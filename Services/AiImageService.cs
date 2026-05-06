using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Kartist.Models;
using Microsoft.Extensions.Options;

namespace Kartist.Services
{
    public class AiImageService : IAiImageService
    {
        private readonly AiOptions _options;
        private readonly IConfiguration _configuration;

        public AiImageService(IOptions<AiOptions> options, IConfiguration configuration)
        {
            _options = options.Value;
            _configuration = configuration;
        }

        public string GetConfiguredProviderName()
        {
            return string.IsNullOrWhiteSpace(_options.ImageProvider) ? "Pexels" : _options.ImageProvider;
        }

        public async Task<AiImageResponse> GenerateImageAsync(string prompt, CancellationToken cancellationToken = default)
        {
            var normalizedPrompt = (prompt ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedPrompt))
            {
                return new AiImageResponse
                {
                    Success = false,
                    Error = "Prompt bos olamaz.",
                    Provider = GetConfiguredProviderName()
                };
            }

            // 1. Pexels (gerçek fotoğraflar — en tutarlı sonuç)
            var pexelsKey = _options.PexelsApiKey;
            if (string.IsNullOrWhiteSpace(pexelsKey))
            {
                pexelsKey = _configuration["Pexels:ApiKey"] ?? "";
            }

            if (!string.IsNullOrWhiteSpace(pexelsKey))
            {
                var pexelsResult = await TryFetchFromPexelsAsync(normalizedPrompt, pexelsKey, cancellationToken);
                if (pexelsResult.Success)
                {
                    return pexelsResult;
                }
            }

            // 2. Pollinations (AI-generated — fallback)
            var pollinationsResult = await TryGenerateWithPollinationsAsync(normalizedPrompt, cancellationToken);
            if (pollinationsResult.Success)
            {
                pollinationsResult.Warnings.Add("Pexels API kullanılamadı, AI görsel üretimi kullanıldı.");
                return pollinationsResult;
            }

            // 3. Picsum (genel fallback)
            var genericFallback = await TryGenerateWithKeywordFallbackAsync(normalizedPrompt, cancellationToken);
            genericFallback.Warnings.InsertRange(0, pollinationsResult.Warnings);
            if (!string.IsNullOrWhiteSpace(pollinationsResult.Error))
            {
                genericFallback.Warnings.Insert(0, pollinationsResult.Error);
            }
            return genericFallback;
        }

        /// <summary>
        /// Pexels API ile tema'ya uygun yüksek kaliteli stok fotoğraf getirir.
        /// Gemini'nin ürettiği İngilizce prompt'tan anahtar kelimeleri çıkarır ve aratır.
        /// </summary>
        private async Task<AiImageResponse> TryFetchFromPexelsAsync(string prompt, string apiKey, CancellationToken cancellationToken)
        {
            try
            {
                // Gemini'nin İngilizce prompt'undan arama terimlerini çıkar
                var searchQuery = ExtractPexelsSearchQuery(prompt);

                using var client = BuildClient();
                client.DefaultRequestHeaders.Add("Authorization", apiKey);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                // Pexels search — orientation=landscape kart arka planı için ideal
                var url = $"https://api.pexels.com/v1/search?query={Uri.EscapeDataString(searchQuery)}&per_page=15&orientation=landscape&size=large";

                using var response = await client.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return new AiImageResponse
                    {
                        Success = false,
                        Provider = "Pexels",
                        RevisedPrompt = prompt,
                        Error = $"Pexels API hatasi: {response.StatusCode}"
                    };
                }

                var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
                using var jsonDoc = JsonDocument.Parse(responseString);

                var photos = jsonDoc.RootElement.GetProperty("photos");
                if (photos.GetArrayLength() == 0)
                {
                    // Daha geniş arama yap
                    var fallbackQuery = ExtractSimpleKeywords(prompt);
                    if (fallbackQuery != searchQuery)
                    {
                        return await TryFetchFromPexelsWithQueryAsync(fallbackQuery, apiKey, prompt, cancellationToken);
                    }

                    return new AiImageResponse
                    {
                        Success = false,
                        Provider = "Pexels",
                        RevisedPrompt = prompt,
                        Error = "Pexels aramasinda uygun gorsel bulunamadi."
                    };
                }

                // Rastgele bir fotoğraf seç (ilk 10 sonuçtan)
                var maxIndex = Math.Min(photos.GetArrayLength(), 10);
                var randomIndex = Random.Shared.Next(0, maxIndex);
                var selectedPhoto = photos[randomIndex];

                // landscape veya large2x kalitesini al
                var photoUrl = selectedPhoto.GetProperty("src").GetProperty("landscape").GetString()
                    ?? selectedPhoto.GetProperty("src").GetProperty("large2x").GetString()
                    ?? selectedPhoto.GetProperty("src").GetProperty("large").GetString();

                if (string.IsNullOrWhiteSpace(photoUrl))
                {
                    return new AiImageResponse
                    {
                        Success = false,
                        Provider = "Pexels",
                        RevisedPrompt = prompt,
                        Error = "Pexels gorsel URL'i alinamadi."
                    };
                }

                // Fotoğrafı indir ve base64'e çevir
                using var imgClient = BuildClient();
                imgClient.DefaultRequestHeaders.UserAgent.ParseAdd("KartistAI/1.0");
                using var imgResponse = await imgClient.GetAsync(photoUrl, cancellationToken);
                
                if (!imgResponse.IsSuccessStatusCode)
                {
                    return new AiImageResponse
                    {
                        Success = false,
                        Provider = "Pexels",
                        RevisedPrompt = prompt,
                        Error = "Pexels gorsel indirilemedi."
                    };
                }

                var bytes = await imgResponse.Content.ReadAsByteArrayAsync(cancellationToken);
                var mediaType = imgResponse.Content.Headers.ContentType?.MediaType ?? "image/jpeg";

                if (bytes.Length == 0 || bytes.Length > _options.MaxImageBytes)
                {
                    return new AiImageResponse
                    {
                        Success = false,
                        Provider = "Pexels",
                        RevisedPrompt = prompt,
                        Error = "Gorsel boyutu siniri asti."
                    };
                }

                var photographer = "";
                if (selectedPhoto.TryGetProperty("photographer", out var photog))
                {
                    photographer = photog.GetString() ?? "";
                }

                return new AiImageResponse
                {
                    Success = true,
                    Provider = "Pexels",
                    RevisedPrompt = prompt,
                    DataUrl = $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}",
                    Warnings = new List<string> { !string.IsNullOrWhiteSpace(photographer) ? $"Fotoğraf: {photographer} (Pexels)" : "Pexels" }
                };
            }
            catch (Exception ex)
            {
                return new AiImageResponse
                {
                    Success = false,
                    Provider = "Pexels",
                    RevisedPrompt = prompt,
                    Error = ex.Message
                };
            }
        }

        private async Task<AiImageResponse> TryFetchFromPexelsWithQueryAsync(string query, string apiKey, string originalPrompt, CancellationToken cancellationToken)
        {
            try
            {
                using var client = BuildClient();
                client.DefaultRequestHeaders.Add("Authorization", apiKey);

                var url = $"https://api.pexels.com/v1/search?query={Uri.EscapeDataString(query)}&per_page=10&orientation=landscape&size=large";
                using var response = await client.GetAsync(url, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    return new AiImageResponse { Success = false, Provider = "Pexels", Error = "Pexels fallback arama basarisiz." };
                }

                var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
                using var jsonDoc = JsonDocument.Parse(responseString);
                var photos = jsonDoc.RootElement.GetProperty("photos");
                
                if (photos.GetArrayLength() == 0)
                {
                    return new AiImageResponse { Success = false, Provider = "Pexels", Error = "Pexels fallback aramasinda gorsel bulunamadi." };
                }

                var randomIndex = Random.Shared.Next(0, Math.Min(photos.GetArrayLength(), 10));
                var photoUrl = photos[randomIndex].GetProperty("src").GetProperty("landscape").GetString();

                using var imgClient = BuildClient();
                imgClient.DefaultRequestHeaders.UserAgent.ParseAdd("KartistAI/1.0");
                using var imgResponse = await imgClient.GetAsync(photoUrl, cancellationToken);
                var bytes = await imgResponse.Content.ReadAsByteArrayAsync(cancellationToken);
                var mediaType = imgResponse.Content.Headers.ContentType?.MediaType ?? "image/jpeg";

                return new AiImageResponse
                {
                    Success = true,
                    Provider = "Pexels",
                    RevisedPrompt = originalPrompt,
                    DataUrl = $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}"
                };
            }
            catch
            {
                return new AiImageResponse { Success = false, Provider = "Pexels", Error = "Pexels fallback hatasi." };
            }
        }

        /// <summary>
        /// Gemini'nin İngilizce prompt'undan Pexels için arama terimleri çıkarır.
        /// Gemini zaten mükemmel İngilizce arama terimleri döndürdüğü için sadece filtreleme yapıyoruz.
        /// </summary>
        private string ExtractPexelsSearchQuery(string prompt)
        {
            // Sadece gereksiz kelimeleri filtrele, orijinal bağlamı (örn: Istanbul) BOZMA
            return ExtractSimpleKeywords(prompt);
        }

        private string ExtractSimpleKeywords(string prompt)
        {
            // Gereksiz kelimeler listesi
            var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "the", "a", "an", "for", "and", "or", "but", "in", "on", "at", "to", "of", "with",
                "high", "quality", "aesthetic", "professional", "beautiful", "elegant", "background",
                "card", "no", "text", "letters", "words", "writing", "typography", "watermark",
                "clean", "only", "absolutely", "style", "resolution", "8k", "4k", "photo",
                "seed", "focus", "macro", "shot", "illustration", "award", "winning",
                "icin", "bir", "ve", "ile", "olan", "bu", "su", "arka", "plan"
            };

            var words = prompt
                .Replace(",", " ")
                .Replace(".", " ")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2 && !stopWords.Contains(w) && !w.All(char.IsDigit))
                .Take(10) // Pexels için 5 kelime çok azdı, önemli kelimeleri (örn: istanbul) kırpıyordu.
                .ToArray();

            return words.Length > 0 ? string.Join(" ", words) : "aesthetic pastel background";
        }

        private async Task<AiImageResponse> TryGenerateWithPollinationsAsync(string prompt, CancellationToken cancellationToken)
        {
            try
            {
                using var client = BuildClient();
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
                client.DefaultRequestHeaders.UserAgent.ParseAdd("KartistAI/1.0");

                var enhancedPrompt = EnhancePrompt(prompt);
                var encodedPrompt = Uri.EscapeDataString(enhancedPrompt);
                var seed = Random.Shared.Next(100000, 999999);
                var model = string.IsNullOrWhiteSpace(_options.PollinationsModel) ? "flux" : _options.PollinationsModel;
                var endpoint = _options.PollinationsEndpoint?.TrimEnd('/') ?? "https://image.pollinations.ai/prompt";
                
                var imageUrl = $"{endpoint}/?prompt={encodedPrompt}&width=1024&height=1024&seed={seed}&nologo=true&model={Uri.EscapeDataString(model)}&negative_prompt={Uri.EscapeDataString("text, letters, words, writing, typography, font, watermark, logo, signature, caption")}";

                using var response = await client.GetAsync(imageUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return new AiImageResponse
                    {
                        Success = false,
                        Provider = "Pollinations",
                        RevisedPrompt = prompt,
                        Error = "Ucretsiz gorsel servisi su an yanit vermedi."
                    };
                }

                var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (!mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    return new AiImageResponse
                    {
                        Success = false,
                        Provider = "Pollinations",
                        RevisedPrompt = prompt,
                        Error = "Gorsel servisi beklenen formatta veri donmedi."
                    };
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (bytes.Length == 0 || bytes.Length > _options.MaxImageBytes)
                {
                    return new AiImageResponse
                    {
                        Success = false,
                        Provider = "Pollinations",
                        RevisedPrompt = prompt,
                        Error = "Uretilen gorsel boyutu izin verilen siniri asti."
                    };
                }

                return new AiImageResponse
                {
                    Success = true,
                    Provider = "Pollinations",
                    RevisedPrompt = prompt,
                    DataUrl = $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}"
                };
            }
            catch (Exception ex)
            {
                return new AiImageResponse
                {
                    Success = false,
                    Provider = "Pollinations",
                    RevisedPrompt = prompt,
                    Error = ex.Message
                };
            }
        }

        private async Task<AiImageResponse> TryGenerateWithKeywordFallbackAsync(string prompt, CancellationToken cancellationToken)
        {
            var seed = Uri.EscapeDataString(prompt.ToLowerInvariant().Replace(' ', '-'));
            var picsumUrl = $"https://picsum.photos/seed/{seed}/1024/1024";

            var result = await TryFetchImageAsDataUrlAsync(picsumUrl, "Picsum", prompt, cancellationToken);
            if (result.Success)
            {
                result.Warnings.Add("AI gorsel uretilemedigi icin ucretsiz genel fallback gorsel kullanildi.");
                return result;
            }

            result.Error = string.IsNullOrWhiteSpace(result.Error)
                ? "Gorsel olusturulamadi. Lutfen daha sonra tekrar deneyin."
                : result.Error;
            return result;
        }

        private async Task<AiImageResponse> TryFetchImageAsDataUrlAsync(string imageUrl, string provider, string prompt, CancellationToken cancellationToken)
        {
            try
            {
                using var client = BuildClient();
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
                client.DefaultRequestHeaders.UserAgent.ParseAdd("KartistAI/1.0");

                using var response = await client.GetAsync(imageUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return new AiImageResponse
                    {
                        Success = false,
                        Provider = provider,
                        RevisedPrompt = prompt,
                        Error = $"{provider} fallback servisi yanit vermedi."
                    };
                }

                var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (!mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    return new AiImageResponse
                    {
                        Success = false,
                        Provider = provider,
                        RevisedPrompt = prompt,
                        Error = $"{provider} gorsel formatinda veri dondurmedi."
                    };
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (bytes.Length == 0 || bytes.Length > _options.MaxImageBytes)
                {
                    return new AiImageResponse
                    {
                        Success = false,
                        Provider = provider,
                        RevisedPrompt = prompt,
                        Error = $"{provider} gorsel boyutu uygun degil."
                    };
                }

                return new AiImageResponse
                {
                    Success = true,
                    Provider = provider,
                    RevisedPrompt = prompt,
                    DataUrl = $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}"
                };
            }
            catch (Exception ex)
            {
                return new AiImageResponse
                {
                    Success = false,
                    Provider = provider,
                    RevisedPrompt = prompt,
                    Error = ex.Message
                };
            }
        }

        private string EnhancePrompt(string prompt)
        {
            var p = prompt.ToLowerInvariant();
            const string noTextSuffix = ", absolutely no text, no letters, no words, no writing, no typography, no watermarks, clean background only";

            if (p.Contains("papatya") || p.Contains("daisy")) return "macro shot of beautiful white daisies with yellow centers, soft morning bokeh, high quality floral background, 8k resolution, professional card aesthetic" + noTextSuffix;
            if (p.Contains("gül") || p.Contains("rose")) return "exquisite red roses with water drops, velvet petals, romantic soft lighting, cinematic aesthetic, high resolution card background" + noTextSuffix;
            if (p.Contains("doğum günü") || p.Contains("birthday") || p.Contains("dogum")) return "festive birthday celebration background, colorful soft bokeh balloons, sparkling glitters, elegant party atmosphere" + noTextSuffix;
            if (p.Contains("anne") || p.Contains("mother")) return "heartwarming soft pastel floral background, gentle pink and white textures, warm glowing light, maternal aesthetic, high quality" + noTextSuffix;
            if (p.Contains("aşk") || p.Contains("love") || p.Contains("sevgili")) return "romantic aesthetic hearts bokeh, soft glowing red and pink tones, dreamy atmosphere, elegant love theme" + noTextSuffix;

            return $"{prompt}, high quality professional card background, aesthetic, award-winning illustration style" + noTextSuffix;
        }

        private HttpClient BuildClient()
        {
            return new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(Math.Max(5, _options.TimeoutSeconds))
            };
        }
    }
}
