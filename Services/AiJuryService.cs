using System.Globalization;

namespace Kartist.Services
{
    public class AiJuryService
    {
        private readonly IAiPromptService _prompt;
        private readonly ILogger<AiJuryService> _logger;

        public AiJuryService(IAiPromptService prompt, ILogger<AiJuryService> logger)
        {
            _prompt = prompt;
            _logger = logger;
        }

        public async Task<JuriKarari> DegerlendirAsync(string tema, string baslik, string aciklama, CancellationToken cancellationToken = default)
        {
            tema = (tema ?? "").Trim();
            baslik = (baslik ?? "").Trim();
            aciklama = (aciklama ?? "").Trim();

            if (!_prompt.HasConfiguredProvider())
            {
                return new JuriKarari(60, "AI jüri yapılandırılmadı; ortalama bir puan verildi.", "yok");
            }

            var saglayici = _prompt.GetConfiguredProviderName() ?? "bilinmeyen";

            var istek = $@"Yarışma teması: {(string.IsNullOrEmpty(tema) ? "(belirtilmemiş)" : tema)}
Katılım başlığı: {(string.IsNullOrEmpty(baslik) ? "(boş)" : baslik)}
Katılım açıklaması: {(string.IsNullOrEmpty(aciklama) ? "(boş)" : aciklama)}

Bu yarışma katılımını 0-100 arası bir tam sayı skorla puanla ve kısa (en fazla 200 karakter) Türkçe bir jüri yorumu yaz.
ZORUNLU çıktı formatı (tam olarak iki satır, başka hiçbir şey yok):
SKOR: <0-100 arası tam sayı>
YORUM: <tek satır Türkçe yorum>";

            string ham = null;
            try
            {
                ham = await _prompt.GenerateTextAsync("yarisma-juri", istek, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI jüri çağrısı başarısız oldu");
            }

            if (string.IsNullOrWhiteSpace(ham))
            {
                return new JuriKarari(60, "AI jüri şu an yanıt veremedi; ortalama bir puan verildi.", saglayici);
            }

            var (skor, yorum) = Ayrıştır(ham);
            return new JuriKarari(skor, yorum, saglayici);
        }

        private static (int Skor, string Yorum) Ayrıştır(string ham)
        {
            int? skor = null;
            string yorum = null;

            foreach (var satir in ham.Split('\n'))
            {
                var temiz = satir.Trim().TrimStart('-', '*', '•').Trim();
                if (temiz.Length == 0) continue;

                if (skor == null && temiz.StartsWith("SKOR", StringComparison.OrdinalIgnoreCase))
                {
                    var rakam = new string(temiz.Where(char.IsDigit).ToArray());
                    if (int.TryParse(rakam, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
                    {
                        skor = Math.Clamp(s, 0, 100);
                    }
                }
                else if (yorum == null && temiz.StartsWith("YORUM", StringComparison.OrdinalIgnoreCase))
                {
                    var idx = temiz.IndexOf(':');
                    yorum = (idx >= 0 ? temiz[(idx + 1)..] : temiz).Trim();
                }
            }

            if (skor == null)
            {
                var ilkRakam = new string(ham.Where(char.IsDigit).Take(3).ToArray());
                if (int.TryParse(ilkRakam, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
                {
                    skor = Math.Clamp(s, 0, 100);
                }
            }

            if (string.IsNullOrWhiteSpace(yorum))
            {
                yorum = ham.Trim();
            }

            if (yorum.Length > 1000) yorum = yorum[..1000];

            return (skor ?? 60, yorum);
        }
    }

    public record JuriKarari(int Skor, string Yorum, string SaglayiciAdi);
}
