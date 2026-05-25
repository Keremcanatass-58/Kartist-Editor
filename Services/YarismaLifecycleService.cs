using Dapper;
using Microsoft.Data.SqlClient;

namespace Kartist.Services
{
    public class YarismaLifecycleService : BackgroundService
    {
        private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(60);
        private const int KazananXpOdulu = 250;

        private readonly IConfiguration _configuration;
        private readonly ILogger<YarismaLifecycleService> _logger;

        public YarismaLifecycleService(IConfiguration configuration, ILogger<YarismaLifecycleService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("YarismaLifecycleService başlatıldı. Tick aralığı: {Seconds}s", TickInterval.TotalSeconds);

            try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
            catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    TickOnce();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "YarismaLifecycleService tick hatası");
                }

                try { await Task.Delay(TickInterval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        private void TickOnce()
        {
            var conn = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(conn))
            {
                return;
            }

            using var db = new SqlConnection(conn);

            int aktiveBitenler = db.Execute(@"
                UPDATE Yarismalar
                   SET Durum = 'voting'
                 WHERE Durum = 'active'
                   AND SonKatilimTarihi IS NOT NULL
                   AND SonKatilimTarihi < GETUTCDATE()");
            if (aktiveBitenler > 0)
            {
                _logger.LogInformation("{Count} yarışma active → voting aşamasına geçti", aktiveBitenler);
            }

            var oylamasiBitenler = db.Query<int>(@"
                SELECT Id FROM Yarismalar
                 WHERE Durum = 'voting'
                   AND OylamaBitisTarihi IS NOT NULL
                   AND OylamaBitisTarihi < GETUTCDATE()").ToList();

            foreach (var yarismaId in oylamasiBitenler)
            {
                try
                {
                    Sonlandir(db, yarismaId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Yarışma {Id} sonlandırılamadı", yarismaId);
                }
            }
        }

        private void Sonlandir(SqlConnection db, int yarismaId)
        {
            db.Execute(
                "UPDATE YarismaKatilimlari SET IsWinner = 0 WHERE YarismaId = @yid AND ISNULL(IsWinner, 0) = 1",
                new { yid = yarismaId });

            var kazanan = db.QueryFirstOrDefault(@"
                SELECT TOP 1 Id, KullaniciId
                  FROM YarismaKatilimlari
                 WHERE YarismaId = @yid
                 ORDER BY OySayisi DESC, ISNULL(AiSkor, 0) DESC, OlusturmaTarihi ASC",
                new { yid = yarismaId });

            int? kazananKullaniciId = null;
            if (kazanan != null)
            {
                int katilimId = (int)kazanan.Id;
                kazananKullaniciId = (int)kazanan.KullaniciId;

                db.Execute(
                    "UPDATE YarismaKatilimlari SET IsWinner = 1 WHERE Id = @id",
                    new { id = katilimId });

                try
                {
                    db.Execute(
                        @"INSERT INTO KullaniciXP (KullaniciId, Miktar, Kaynak, Aciklama)
                          VALUES (@uid, @miktar, 'yarisma_kazanan', 'Yarışma kazandı!');",
                        new { uid = kazananKullaniciId.Value, miktar = KazananXpOdulu });

                    db.Execute(
                        "UPDATE Kullanicilar SET ToplamXP = ISNULL(ToplamXP, 0) + @miktar WHERE Id = @uid",
                        new { uid = kazananKullaniciId.Value, miktar = KazananXpOdulu });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Yarışma {Yid} kazananı için XP eklenirken hata", yarismaId);
                }
            }

            db.Execute(
                "UPDATE Yarismalar SET Durum = 'ended' WHERE Id = @id",
                new { id = yarismaId });

            _logger.LogInformation(
                "Yarışma {Yid} voting → ended (kazanan kullanıcı: {KullaniciId})",
                yarismaId,
                kazananKullaniciId?.ToString() ?? "yok");
        }
    }
}
