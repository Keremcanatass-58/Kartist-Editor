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
            return string.IsNullOrWhiteSpace(_options.ImageProvider) ? "Pollinations" : _options.ImageProvider;
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

            if (GetConfiguredProviderName().Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                var openAiResult = await TryGenerateWithOpenAiAsync(normalizedPrompt, cancellationToken);
                if (openAiResult.Success)
                {
                    return openAiResult;
                }

                openAiResult.Warnings.Add("OpenAI image provider kullanilamadi, ucretsiz saglayiciya geri donuldu.");
                var fallback = await TryGenerateWithPollinationsAsync(normalizedPrompt, cancellationToken);
                if (fallback.Success)
                {
                    fallback.Warnings.InsertRange(0, openAiResult.Warnings);
                    return fallback;
                }

                var keywordFallback = await TryGenerateWithKeywordFallbackAsync(normalizedPrompt, cancellationToken);
                keywordFallback.Warnings.InsertRange(0, openAiResult.Warnings);
                return keywordFallback;
            }

            var pollinationsResult = await TryGenerateWithPollinationsAsync(normalizedPrompt, cancellationToken);
            if (pollinationsResult.Success)
            {
                return pollinationsResult;
            }

            var genericFallback = await TryGenerateWithKeywordFallbackAsync(normalizedPrompt, cancellationToken);
            genericFallback.Warnings.InsertRange(0, pollinationsResult.Warnings);
            if (!string.IsNullOrWhiteSpace(pollinationsResult.Error))
            {
                genericFallback.Warnings.Insert(0, pollinationsResult.Error);
            }
            return genericFallback;
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
                
                // Using 'prompt' as a query parameter is often more reliable for long complex prompts
                var imageUrl = $"{endpoint}/?prompt={encodedPrompt}&width=1024&height=1024&seed={seed}&nologo=true&model={Uri.EscapeDataString(model)}";

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

        private async Task<AiImageResponse> TryGenerateWithOpenAiAsync(string prompt, CancellationToken cancellationToken)
        {
            var apiKey = _configuration["OpenAI:ApiKey"]
                ?? _configuration["OPENAI_API_KEY"]
                ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new AiImageResponse
                {
                    Success = false,
                    Provider = "OpenAI",
                    RevisedPrompt = prompt,
                    Error = "OpenAI image provider icin API anahtari tanimli degil."
                };
            }

            try
            {
                using var client = BuildClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

                var payload = new
                {
                    model = _options.OpenAiImageModel,
                    prompt,
                    size = "1024x1024"
                };

                using var response = await client.PostAsync(
                    _options.OpenAiImageEndpoint,
                    new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
                    cancellationToken);

                var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return new AiImageResponse
                    {
                        Success = false,
                        Provider = "OpenAI",
                        RevisedPrompt = prompt,
                        Error = $"OpenAI image endpoint hatasi: {response.StatusCode}"
                    };
                }

                using var jsonDoc = JsonDocument.Parse(responseString);
                var imageEntry = jsonDoc.RootElement.GetProperty("data")[0];

                if (imageEntry.TryGetProperty("b64_json", out var b64Json))
                {
                    return new AiImageResponse
                    {
                        Success = true,
                        Provider = "OpenAI",
                        RevisedPrompt = prompt,
                        DataUrl = $"data:image/png;base64,{b64Json.GetString()}"
                    };
                }

                if (imageEntry.TryGetProperty("url", out var imageUrl))
                {
                    using var imageResponse = await client.GetAsync(imageUrl.GetString(), cancellationToken);
                    var bytes = await imageResponse.Content.ReadAsByteArrayAsync(cancellationToken);
                    var mediaType = imageResponse.Content.Headers.ContentType?.MediaType ?? "image/png";
                    return new AiImageResponse
                    {
                        Success = true,
                        Provider = "OpenAI",
                        RevisedPrompt = prompt,
                        DataUrl = $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}"
                    };
                }

                return new AiImageResponse
                {
                    Success = false,
                    Provider = "OpenAI",
                    RevisedPrompt = prompt,
                    Error = "OpenAI image response formati beklenenden farkli."
                };
            }
            catch (Exception ex)
            {
                return new AiImageResponse
                {
                    Success = false,
                    Provider = "OpenAI",
                    RevisedPrompt = prompt,
                    Error = ex.Message
                };
            }
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
            
            // PRIORITY 1: Identify key themes and return EXCLUSIVELY high-weight English prompts
            if (p.Contains("papatya")) return "macro shot of beautiful white daisies with yellow centers, soft morning bokeh, high quality floral background, 8k resolution, professional card aesthetic, no text";
            if (p.Contains("gül") || p.Contains("rose")) return "exquisite red roses with water drops, velvet petals, romantic soft lighting, cinematic aesthetic, high resolution card background, no text";
            if (p.Contains("doğum günü") || p.Contains("birthday")) return "festive birthday celebration background, colorful soft bokeh balloons, sparkling glitters, elegant party atmosphere, no text";
            if (p.Contains("anne") || p.Contains("mother")) return "heartwarming soft pastel background, gentle textures, warm glowing light, maternal aesthetic, high quality, no text";
            if (p.Contains("aşk") || p.Contains("love") || p.Contains("sevgili")) return "romantic aesthetic hearts bokeh, soft glowing red and pink tones, dreamy atmosphere, elegant love theme, no text";

            // PRIORITY 2: If no specific theme, translate the prompt but wrap it in quality tags
            // We assume 'prompt' might contain Turkish, so we still use it as secondary context
            return $"{prompt}, high quality professional card background, aesthetic, award-winning illustration style, no text, no letters";
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
