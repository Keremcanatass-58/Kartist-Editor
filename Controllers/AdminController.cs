using Dapper;
using Kartist.Helpers;
using Kartist.Hubs;
using Kartist.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using System.Security.Claims;

namespace Kartist.Controllers
{
    public class AdminController : Controller
    {
        private readonly string _baglanti;
        private readonly IHubContext<AdminHub> _hubContext;

        public AdminController(IConfiguration config, IHubContext<AdminHub> hubContext)
        {
            _baglanti = config.GetConnectionString("DefaultConnection");
            _hubContext = hubContext;
        }

        private bool AdminKontrol()
        {
            if (!User.Identity.IsAuthenticated) return false;
            string email = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;

            using (var db = new SqlConnection(_baglanti))
            {
                try
                {
                    string yetki = db.QueryFirstOrDefault<string>(
                        "SELECT Yetki FROM Kullanicilar WHERE Email = @e",
                        new { e = email }, commandTimeout: 3);
                    return yetki == "Admin";
                }
                catch
                {
                    return false;
                }
            }
        }

        private bool AdminYetkili() =>
            HttpContext.Session.GetString("AdminOturumu") != null || AdminKontrol();

        private void EnsureYarismaSchema(SqlConnection db)
        {
            db.Execute(@"
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('Yarismalar') AND type = 'U')
BEGIN
    CREATE TABLE Yarismalar (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        Baslik NVARCHAR(200) NOT NULL,
        Aciklama NVARCHAR(1000) NULL,
        Tema NVARCHAR(100) NOT NULL,
        Odul NVARCHAR(100) NOT NULL,
        KapakUrl NVARCHAR(500) NOT NULL,
        Durum NVARCHAR(20) NOT NULL DEFAULT 'active',
        SonKatilimTarihi DATETIME NOT NULL,
        OylamaBitisTarihi DATETIME NULL,
        OlusturmaTarihi DATETIME NOT NULL DEFAULT GETUTCDATE()
    );
END
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Yarismalar') AND name = 'Baslik')
    ALTER TABLE Yarismalar ADD Baslik NVARCHAR(200) NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Yarismalar') AND name = 'Aciklama')
    ALTER TABLE Yarismalar ADD Aciklama NVARCHAR(1000) NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Yarismalar') AND name = 'Tema')
    ALTER TABLE Yarismalar ADD Tema NVARCHAR(100) NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Yarismalar') AND name = 'Odul')
    ALTER TABLE Yarismalar ADD Odul NVARCHAR(100) NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Yarismalar') AND name = 'KapakUrl')
    ALTER TABLE Yarismalar ADD KapakUrl NVARCHAR(500) NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Yarismalar') AND name = 'Durum')
    ALTER TABLE Yarismalar ADD Durum NVARCHAR(20) NOT NULL DEFAULT 'active';
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Yarismalar') AND name = 'SonKatilimTarihi')
    ALTER TABLE Yarismalar ADD SonKatilimTarihi DATETIME NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Yarismalar') AND name = 'OylamaBitisTarihi')
    ALTER TABLE Yarismalar ADD OylamaBitisTarihi DATETIME NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Yarismalar') AND name = 'OlusturmaTarihi')
    ALTER TABLE Yarismalar ADD OlusturmaTarihi DATETIME NOT NULL DEFAULT GETUTCDATE();
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Yarismalar') AND name = 'StartDate' AND is_nullable = 0)
    ALTER TABLE Yarismalar ALTER COLUMN StartDate DATETIME NULL;
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Yarismalar') AND name = 'EndDate' AND is_nullable = 0)
    ALTER TABLE Yarismalar ALTER COLUMN EndDate DATETIME NULL;
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Yarismalar') AND name = 'VotingEndDate' AND is_nullable = 0)
    ALTER TABLE Yarismalar ALTER COLUMN VotingEndDate DATETIME NULL;
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Yarismalar') AND name = 'Status' AND is_nullable = 0)
    ALTER TABLE Yarismalar ALTER COLUMN Status INT NULL;
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Yarismalar') AND name = 'CreatedById' AND is_nullable = 0)
    ALTER TABLE Yarismalar ALTER COLUMN CreatedById INT NULL;
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Yarismalar') AND name = 'CreatedAt' AND is_nullable = 0)
    ALTER TABLE Yarismalar ALTER COLUMN CreatedAt DATETIME NULL;
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('YarismaKatilimlari') AND type = 'U')
BEGIN
    CREATE TABLE YarismaKatilimlari (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        YarismaId INT NOT NULL,
        KullaniciId INT NOT NULL,
        Baslik NVARCHAR(200) NOT NULL,
        Aciklama NVARCHAR(1000) NULL,
        GorselUrl NVARCHAR(500) NOT NULL,
        OySayisi INT NOT NULL DEFAULT 0,
        OlusturmaTarihi DATETIME NOT NULL DEFAULT GETUTCDATE()
    );
END
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('YarismaOylari') AND type = 'U')
BEGIN
    CREATE TABLE YarismaOylari (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        YarismaId INT NOT NULL,
        KatilimId INT NOT NULL,
        KullaniciId INT NOT NULL,
        Tarih DATETIME NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT UQ_YarismaOy UNIQUE (YarismaId, KullaniciId)
    );
END");
        }

        public IActionResult Login()
        {
            if (HttpContext.Session.GetString("AdminOturumu") != null) return RedirectToAction("Panel");
            return View();
        }

        [HttpPost]
        public IActionResult Login(string kadi, string sifre)
        {
            using (var db = new SqlConnection(_baglanti))
            {
                var yonetici = db.QueryFirstOrDefault(
                    "SELECT TOP 1 Id, Sifre FROM Yoneticiler WHERE KullaniciAdi = @k",
                    new { k = kadi }, commandTimeout: 3);

                if (yonetici != null)
                {
                    string dbSifre = (string)yonetici.Sifre;
                    bool sifreDogruMu = PasswordHasher.IsHashed(dbSifre)
                        && PasswordHasher.VerifyPassword(sifre, dbSifre);

                    if (sifreDogruMu)
                    {
                        HttpContext.Session.SetString("AdminOturumu", "Aktif");
                        return RedirectToAction("Panel");
                    }
                }
            }
            ViewBag.Hata = "Hatalı Giriş!";
            return View();
        }

        public IActionResult Panel()
        {
            if (!AdminYetkili()) return RedirectToAction("Login");

            using (var db = new SqlConnection(_baglanti))
            {
                const int timeout = 3;
                EnsureYarismaSchema(db);

                ViewBag.ToplamUye = db.ExecuteScalar<int>(
                    "SELECT COUNT(1) FROM Kullanicilar", commandTimeout: timeout);
                ViewBag.ToplamTasarim = db.ExecuteScalar<int>(
                    "SELECT COUNT(1) FROM Sablonlar", commandTimeout: timeout);
                ViewBag.BekleyenSayisi = db.ExecuteScalar<int>(
                    "SELECT COUNT(1) FROM Sablonlar WHERE OnayDurumu = 'Bekliyor'",
                    commandTimeout: timeout);
                ViewBag.ToplamCiro = db.ExecuteScalar<decimal>(
                    "SELECT ISNULL(SUM(Fiyat), 0) FROM Sablonlar", commandTimeout: timeout);

                ViewBag.Bekleyenler = db.Query<Sablon>(
                    "SELECT TOP 50 Id, Baslik, Kategori, Fiyat, ResimUrl, OnayDurumu FROM Sablonlar WHERE OnayDurumu = 'Bekliyor' ORDER BY Id DESC",
                    commandTimeout: timeout).ToList();

                ViewBag.SonUyeler = db.Query<dynamic>(
                    "SELECT TOP 50 Id, AdSoyad, Email, KalanKredi, UyelikTipi FROM Kullanicilar ORDER BY Id DESC",
                    commandTimeout: timeout).ToList();

                ViewBag.Yarismalar = db.Query<dynamic>(@"
                    SELECT TOP 20 y.Id, y.Baslik, y.Tema, y.Odul, y.Durum,
                           y.SonKatilimTarihi, y.OylamaBitisTarihi,
                           COUNT(k.Id) AS KatilimciSayisi
                    FROM Yarismalar y
                    LEFT JOIN YarismaKatilimlari k ON k.YarismaId = y.Id
                    GROUP BY y.Id, y.Baslik, y.Tema, y.Odul, y.Durum, y.SonKatilimTarihi, y.OylamaBitisTarihi, y.OlusturmaTarihi
                    ORDER BY y.OlusturmaTarihi DESC", commandTimeout: timeout).ToList();

                var aktifKartlar = db.Query<Sablon>(
                    "SELECT TOP 60 Id, Baslik, Kategori, Fiyat, ResimUrl, OnayDurumu FROM Sablonlar WHERE OnayDurumu = 'Onaylandi' OR OnayDurumu IS NULL ORDER BY Id DESC",
                    commandTimeout: timeout).ToList();

                return View(aktifKartlar);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> KrediYukle(int id, int miktar)
        {
            if (!AdminYetkili()) return Unauthorized();

            using (var db = new SqlConnection(_baglanti))
            {
                string email = db.QueryFirstOrDefault<string>(
                    "SELECT Email FROM Kullanicilar WHERE Id = @id", new { id }, commandTimeout: 3);
                db.Execute(
                    "UPDATE Kullanicilar SET KalanKredi = KalanKredi + @m WHERE Id = @id",
                    new { m = miktar, id = id }, commandTimeout: 3);

                if (!string.IsNullOrEmpty(email))
                    await _hubContext.Clients.Group(email).SendAsync("SayfayiYenile");
            }
            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UyelikDegistir(int id, string tip)
        {
            if (!AdminYetkili()) return Unauthorized();

            using (var db = new SqlConnection(_baglanti))
            {
                string email = db.QueryFirstOrDefault<string>(
                    "SELECT Email FROM Kullanicilar WHERE Id = @id", new { id }, commandTimeout: 3);
                db.Execute(
                    "UPDATE Kullanicilar SET UyelikTipi = @t WHERE Id = @id",
                    new { t = tip, id = id }, commandTimeout: 3);

                if (!string.IsNullOrEmpty(email))
                    await _hubContext.Clients.Group(email).SendAsync("SayfayiYenile");
            }
            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Ekle(Sablon model)
        {
            if (!AdminYetkili()) return RedirectToAction("Login");

            using (var db = new SqlConnection(_baglanti))
            {
                model.OnayDurumu = "Onaylandi";
                db.Execute(
                    "INSERT INTO Sablonlar (Baslik, Kategori, Fiyat, ResimUrl, OnayDurumu) VALUES (@Baslik, @Kategori, @Fiyat, @ResimUrl, @OnayDurumu)",
                    model, commandTimeout: 3);
            }
            return RedirectToAction("Panel");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Sil(int id)
        {
            if (!AdminYetkili()) return RedirectToAction("Login");

            using (var db = new SqlConnection(_baglanti))
            {
                db.Execute("DELETE FROM Sablonlar WHERE Id = @id", new { id }, commandTimeout: 3);
            }
            return RedirectToAction("Panel");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Onayla(int id)
        {
            if (!AdminYetkili()) return RedirectToAction("Login");

            using (var db = new SqlConnection(_baglanti))
            {
                db.Execute("UPDATE Sablonlar SET OnayDurumu = 'Onaylandi' WHERE Id = @id", new { id }, commandTimeout: 3);
            }
            return RedirectToAction("Panel");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Reddet(int id)
        {
            if (!AdminYetkili()) return RedirectToAction("Login");

            using (var db = new SqlConnection(_baglanti))
            {
                db.Execute("DELETE FROM Sablonlar WHERE Id = @id", new { id }, commandTimeout: 3);
            }
            return RedirectToAction("Panel");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult YarismaOlustur(string baslik, string aciklama, string tema, string odul, string kapakUrl, DateTime sonKatilimTarihi, DateTime? oylamaBitisTarihi, string durum)
        {
            if (!AdminYetkili()) return RedirectToAction("Login");

            if (string.IsNullOrWhiteSpace(baslik) || string.IsNullOrWhiteSpace(tema) || string.IsNullOrWhiteSpace(odul))
            {
                TempData["AdminHata"] = "Yarışma başlığı, tema ve ödül alanları zorunludur.";
                return RedirectToAction("Panel");
            }

            var izinliDurumlar = new[] { "active", "voting", "ended" };
            durum = izinliDurumlar.Contains(durum) ? durum : "active";
            var oylamaBitis = oylamaBitisTarihi ?? sonKatilimTarihi.AddDays(3);

            if (durum == "active" && sonKatilimTarihi <= DateTime.Now)
            {
                TempData["AdminHata"] = "Aktif yarışma için son katılım tarihi gelecekte olmalıdır.";
                return RedirectToAction("Panel");
            }

            if (oylamaBitis <= sonKatilimTarihi)
            {
                TempData["AdminHata"] = "Oylama bitiş tarihi son katılım tarihinden sonra olmalıdır.";
                return RedirectToAction("Panel");
            }

            var temizBaslik = InputValidator.SanitizeHtml(baslik.Trim());
            var temizAciklama = InputValidator.SanitizeHtml(aciklama ?? "");
            var temizTema = InputValidator.SanitizeHtml(tema.Trim());
            var temizOdul = InputValidator.SanitizeHtml(odul.Trim());
            var temizKapak = string.IsNullOrWhiteSpace(kapakUrl)
                ? "https://images.unsplash.com/photo-1558655146-9f40138edfeb?w=1200"
                : InputValidator.SanitizeHtml(kapakUrl.Trim());

            using (var db = new SqlConnection(_baglanti))
            {
                EnsureYarismaSchema(db);
                db.Execute(@"
                    INSERT INTO Yarismalar (Baslik, Aciklama, Tema, Odul, KapakUrl, Durum, SonKatilimTarihi, OylamaBitisTarihi)
                    VALUES (@Baslik, @Aciklama, @Tema, @Odul, @KapakUrl, @Durum, @SonKatilimTarihi, @OylamaBitisTarihi)",
                    new
                    {
                        Baslik = temizBaslik,
                        Aciklama = temizAciklama,
                        Tema = temizTema,
                        Odul = temizOdul,
                        KapakUrl = temizKapak,
                        Durum = durum,
                        SonKatilimTarihi = sonKatilimTarihi,
                        OylamaBitisTarihi = oylamaBitis
                    },
                    commandTimeout: 3);
            }

            TempData["AdminMesaj"] = "Yarışma oluşturuldu.";
            return RedirectToAction("Panel");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult YarismaDurumDegistir(int id, string durum)
        {
            if (!AdminYetkili()) return RedirectToAction("Login");

            var izinliDurumlar = new[] { "active", "voting", "ended" };
            if (!izinliDurumlar.Contains(durum)) return RedirectToAction("Panel");

            using (var db = new SqlConnection(_baglanti))
            {
                EnsureYarismaSchema(db);
                db.Execute("UPDATE Yarismalar SET Durum = @durum WHERE Id = @id",
                    new { id, durum }, commandTimeout: 3);
            }

            TempData["AdminMesaj"] = "Yarışma durumu güncellendi.";
            return RedirectToAction("Panel");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult YarismaSil(int id)
        {
            if (!AdminYetkili()) return RedirectToAction("Login");

            using (var db = new SqlConnection(_baglanti))
            {
                EnsureYarismaSchema(db);
                db.Execute("DELETE FROM YarismaOylari WHERE YarismaId = @id", new { id }, commandTimeout: 3);
                db.Execute("DELETE FROM YarismaKatilimlari WHERE YarismaId = @id", new { id }, commandTimeout: 3);
                db.Execute("DELETE FROM Yarismalar WHERE Id = @id", new { id }, commandTimeout: 3);
            }

            TempData["AdminMesaj"] = "Yarışma silindi.";
            return RedirectToAction("Panel");
        }

        public IActionResult Cikis()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }
    }
}
