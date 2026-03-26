namespace Kartist.Services
{
    public interface IAiPromptService
    {
        Task<string> RewriteImagePromptAsync(string prompt, string style, CancellationToken cancellationToken = default);
        Task<string> GenerateDesignSuggestionJsonAsync(string prompt, string kategori, string style, string history = null, CancellationToken cancellationToken = default);
        bool HasConfiguredProvider();
        string GetConfiguredProviderName();
    }
}

