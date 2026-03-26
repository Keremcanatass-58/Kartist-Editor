using Kartist.Models;

namespace Kartist.Services
{
    public interface IAiImageService
    {
        Task<AiImageResponse> GenerateImageAsync(string prompt, CancellationToken cancellationToken = default);
        string GetConfiguredProviderName();
    }
}
