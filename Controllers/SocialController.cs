using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;

namespace Kartist.Controllers
{
    public class SocialController : Kartist.Controllers.Base.BaseController
    {
        private readonly string _conn;
        private readonly IConfiguration _config;
        private readonly Kartist.Services.AiModerationService _aiModerator;
        private readonly Microsoft.AspNetCore.SignalR.IHubContext<Kartist.Hubs.NotificationHub> _hubContext;
        private readonly Kartist.Services.Business.ISocialService _socialService;

        public SocialController(
            IConfiguration config, 
            Kartist.Services.AiModerationService aiModerator,
            Microsoft.AspNetCore.SignalR.IHubContext<Kartist.Hubs.NotificationHub> hubContext,
            Kartist.Services.Business.ISocialService socialService)
        {
            _config = config;
            _conn = config.GetConnectionString("DefaultConnection");
            _aiModerator = aiModerator;
            _hubContext = hubContext;
            _socialService = socialService;
        }

        private string GetEmail() => User.Identity.IsAuthenticated ? User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value : null;

        private int GetUserId(SqlConnection db, string email)
        {
            if (string.IsNullOrEmpty(email)) return 0;
            return db.ExecuteScalar<int>("SELECT Id FROM Kullanicilar WHERE Email = @e", new { e = email });
        }

        // ===== FEED =====
        public IActionResult Feed(string filter = "Tumu")
        {
            if (!User.Identity.IsAuthenticated) return RedirectToAction("Giris", "Account");

            using var db = new SqlConnection(_conn);
            string email = GetEmail();
            int userId = GetUserId(db, email);
            
            // Kullanici bilgileri
            var user = db.QueryFirstOrDefault("SELECT Id, Seviye as Level, ToplamXP as XP FROM Kullanicilar WHERE Id = @id", new { id = userId });
            ViewBag.CurrentUserId = userId;
            ViewBag.UserLevel = user?.Level ?? 1;
            ViewBag.UserXP = user?.XP ?? 0;
            ViewBag.UserMaxXP = ((user?.Level ?? 1) + 1) * 200;
            
            // Feed posts - sadece mevcut kolonlar
            var posts = db.Query(@"
                SELECT g.Id, g.Icerik as Title, 
                       ISNULL(NULLIF(g.GorselUrl, ''), '/img/default-design.svg') as ImageUrl, 
                       k.AdSoyad as UserName, 
                       ISNULL(NULLIF(k.ProfilResmi, ''), '/img/default-user.png') as UserAvatar,
                       REPLACE(k.Email, '@gmail.com', '') as UserHandle, 
                       g.BegeniSayisi as Likes, g.YorumSayisi as Comments,
                       g.GoruntulemeSayisi as Views, g.OlusturmaTarihi as CreatedAt,
                       g.KullaniciId
                FROM SosyalGonderiler g
                JOIN Kullanicilar k ON g.KullaniciId = k.Id
                ORDER BY g.OlusturmaTarihi DESC").ToList();
            
            // Trending designers
            ViewBag.TrendingDesigners = db.Query(@"
                SELECT TOP 5 ROW_NUMBER() OVER (ORDER BY k.ToplamXP DESC) as Rank,
                       k.Id, k.AdSoyad as Name, k.ProfilResmi as Avatar,
                       k.Seviye as Level, k.ToplamXP as WeeklyXP,
                       k.ToplamXP as XP, ((k.Seviye + 1) * 200) as MaxXP,
                       REPLACE(k.Email, '@gmail.com', '') as Username,
                       CAST(k.ToplamXP % 200 * 100 / 200 as int) as XPPercent
                FROM Kullanicilar k
                ORDER BY k.ToplamXP DESC").ToList();
            
            // XP Leaderboard
            ViewBag.XPLeaderboard = db.Query(@"
                SELECT TOP 3 ROW_NUMBER() OVER (ORDER BY k.ToplamXP DESC) as Rank,
                       k.AdSoyad as Name, k.ProfilResmi as Avatar,
                       k.Seviye as Level, k.ToplamXP as XP
                FROM Kullanicilar k
                ORDER BY k.ToplamXP DESC").ToList();
            
            // Live Rooms (from DB â€” fallback to empty if table missing)
            try
            {
                ViewBag.LiveRooms = db.Query(@"
                    SELECT TOP 3 cy.Id, cy.Baslik as Title, cy.IzleyiciSayisi as Viewers,
                           k.AdSoyad as HostName, k.Seviye as HostLevel,
                           ISNULL(NULLIF(k.ProfilResmi, ''),
                                  'https://ui-avatars.com/api/?name=' + REPLACE(k.AdSoyad, ' ', '+') + '&background=random&size=128') as Thumbnail,
                           CAST(1 AS BIT) as IsLive
                    FROM CanliYayinlar cy
                    JOIN Kullanicilar k ON cy.YayinciId = k.Id
                    WHERE cy.Aktif = 1
                    ORDER BY cy.BaslangicTarihi DESC").ToList();
                ViewBag.LiveCount = ((IEnumerable<dynamic>)ViewBag.LiveRooms).Count();
            }
            catch { ViewBag.LiveRooms = new List<dynamic>(); ViewBag.LiveCount = 0; }
            
            // Competitions (Mock)
            ViewBag.Competitions = new List<dynamic> {
                new { Id = 1, Title = "Gelecek Icin Tasarla", CoverImage = "https://images.unsplash.com/photo-1451187580459-43490279c0fa?w=800", Theme = "Sustainability", Prize = "10,000 TL + Premium", Participants = 234, Deadline = "5 gun", Status = "active" },
                new { Id = 2, Title = "AI x Design", CoverImage = "https://images.unsplash.com/photo-1677442136019-21780ecad995?w=800", Theme = "AI", Prize = "15,000 TL + Davetiye", Participants = 89, Deadline = "3 gun", Status = "voting" }
            };

            return View(posts);
        }

        // ===== TRENDING =====
        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        public IActionResult Trending(string filter = "week")
        {
            // DEBUG: Auth check disabled for testing
            // if (!User.Identity.IsAuthenticated) return RedirectToAction("Giris", "Account");
            
            using var db = new SqlConnection(_conn);
            string email = GetEmail() ?? "test@test.com";
            int userId = GetUserId(db, email);
            if (userId == 0) userId = 1; // Default for testing
            
            // Kullanici bilgileri
            var user = db.QueryFirstOrDefault("SELECT Seviye as Level, ToplamXP as XP FROM Kullanicilar WHERE Id = @id", new { id = userId });
            ViewBag.UserLevel = user?.Level ?? 1;
            ViewBag.UserXP = user?.XP ?? 0;
            ViewBag.UserMaxXP = ((user?.Level ?? 1) + 1) * 200;
            
            // Trending designers
            ViewBag.TrendingDesigners = db.Query(@"
                SELECT TOP 10 ROW_NUMBER() OVER (ORDER BY k.ToplamXP DESC) as Rank,
                       k.Id, k.AdSoyad as Name,
                       CASE WHEN k.ProfilResmi IS NULL OR k.ProfilResmi = '' OR k.ProfilResmi LIKE '/uploads/%'
                            THEN 'https://ui-avatars.com/api/?name=' + REPLACE(k.AdSoyad, ' ', '+') + '&background=random&size=128'
                            ELSE k.ProfilResmi END as Avatar,
                       k.Seviye as Level, k.ToplamXP as WeeklyXP,
                       k.ToplamXP as XP, ((k.Seviye + 1) * 200) as MaxXP,
                       REPLACE(k.Email, '@gmail.com', '') as Username,
                       CAST(k.ToplamXP % 200 * 100 / 200 as int) as XPPercent
                FROM Kullanicilar k
                ORDER BY k.ToplamXP DESC").ToList();
            
            // Trending designs
            ViewBag.TrendingDesigns = db.Query(@"
                SELECT TOP 9 g.Id, g.Icerik as Title, 
                       ISNULL(NULLIF(g.GorselUrl, ''), '/img/default-design.svg') as ImageUrl, 
                       k.AdSoyad as UserName,
                       CASE WHEN k.ProfilResmi IS NULL OR k.ProfilResmi = '' 
                            THEN 'https://ui-avatars.com/api/?name=' + REPLACE(k.AdSoyad, ' ', '+') + '&background=random&size=128'
                            ELSE k.ProfilResmi END as UserAvatar,
                       REPLACE(k.Email, '@gmail.com', '') as UserHandle, 
                       g.BegeniSayisi as Likes, g.YorumSayisi as Comments,
                       g.GoruntulemeSayisi as Views
                FROM SosyalGonderiler g
                JOIN Kullanicilar k ON g.KullaniciId = k.Id
                WHERE g.GorselUrl IS NOT NULL AND g.GorselUrl != ''
                ORDER BY g.BegeniSayisi DESC").ToList();
            
            return View();
        }

        // ===== DUELS =====
        [AllowAnonymous]
        public IActionResult Duels()
        {
            using var db = new SqlConnection(_conn);
            string email = GetEmail() ?? "test@test.com";
            int userId = GetUserId(db, email);
            if (userId == 0) userId = 1;

            var user = db.QueryFirstOrDefault("SELECT Seviye as Level, ToplamXP as XP FROM Kullanicilar WHERE Id = @id", new { id = userId });
            ViewBag.UserLevel = user?.Level ?? 1;
            ViewBag.UserXP = user?.XP ?? 0;
            ViewBag.UserMaxXP = ((user?.Level ?? 1) + 1) * 200;
            ViewBag.CurrentUserId = userId;

            DuelloSureDolanlariGuncelle(db);

            ViewBag.ActiveDuels = db.ExecuteScalar<int>("SELECT COUNT(*) FROM Duellolar WHERE Durum IN ('bekliyor','tasarim','oylama')");
            ViewBag.TotalVotes = db.ExecuteScalar<int>("SELECT COUNT(*) FROM DuelloOylari");
            ViewBag.WeeklyWinners = db.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Duellolar WHERE Durum = 'bitti' AND KazananId IS NOT NULL AND BitisTarihi >= DATEADD(day, -7, GETUTCDATE())");

            ViewBag.Rakipler = db.Query(@"
                SELECT TOP 12 k.Id, k.AdSoyad, k.Seviye,
                       ISNULL(NULLIF(k.ProfilResmi, ''),
                              'https://ui-avatars.com/api/?name=' + REPLACE(k.AdSoyad, ' ', '+') + '&background=random&size=128') as ProfilResmi
                FROM Kullanicilar k
                WHERE k.Id != @uid
                ORDER BY k.ToplamXP DESC", new { uid = userId }).ToList();

            ViewBag.Duels = db.Query(@"
                SELECT d.Id, d.Baslik as Title, d.Aciklama as Description, d.Durum as Status, d.Kategori,
                       d.MeydanOkuyanOy as Votes1, d.RakipOy as Votes2,
                       d.MeydanOkuyanTasarimUrl as ChallengerDesign, d.RakipTasarimUrl as OpponentDesign,
                       d.OlusturmaTarihi, d.KabulTarihi, d.OylamaBaslangic, d.BitisTarihi,
                       d.TasarimSuresiSaat, d.OylamaSuresiSaat, d.KazananId,
                       d.MeydanOkuyanId, d.RakipId,
                       k1.AdSoyad as ChallengerName, k1.Seviye as ChallengerLevel,
                       ISNULL(NULLIF(k1.ProfilResmi,''), '/img/default-user.png') as ChallengerAvatar,
                       k2.AdSoyad as OpponentName, k2.Seviye as OpponentLevel,
                       ISNULL(NULLIF(k2.ProfilResmi,''), '/img/default-user.png') as OpponentAvatar,
                       CASE
                           WHEN d.Durum = 'bekliyor' THEN 'davet bekliyor'
                           WHEN d.Durum = 'tasarim' AND d.KabulTarihi IS NOT NULL
                               THEN CONCAT(DATEDIFF(hour, GETUTCDATE(), DATEADD(hour, d.TasarimSuresiSaat, d.KabulTarihi)), ' saat')
                           WHEN d.Durum = 'oylama' AND d.OylamaBaslangic IS NOT NULL
                               THEN CONCAT(DATEDIFF(hour, GETUTCDATE(), DATEADD(hour, d.OylamaSuresiSaat, d.OylamaBaslangic)), ' saat')
                           WHEN d.Durum = 'bitti' THEN 'tamamlandi'
                           ELSE 'sure hesaplaniyor'
                       END as TimeLeft
                FROM Duellolar d
                JOIN Kullanicilar k1 ON d.MeydanOkuyanId = k1.Id
                JOIN Kullanicilar k2 ON d.RakipId = k2.Id
                ORDER BY CASE d.Durum WHEN 'oylama' THEN 0 WHEN 'tasarim' THEN 1 WHEN 'bekliyor' THEN 2 ELSE 3 END,
                         d.OlusturmaTarihi DESC").ToList();

            ViewBag.UserVotes = db.Query<int>(
                "SELECT DuelloId FROM DuelloOylari WHERE KullaniciId = @uid", new { uid = userId }).ToList();

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DuelloOlustur(int opponent, string category, string challenge)
        {
            if (!User.Identity.IsAuthenticated)
                return Json(new { success = false, message = "GiriÅŸ yapmalÄ±sÄ±nÄ±z." });

            using var db = new SqlConnection(_conn);
            int userId = GetUserId(db, GetEmail());
            if (userId == 0) return Json(new { success = false, message = "Kullanıcı bulunamadı." });
            if (userId == opponent) return Json(new { success = false, message = "Kendinize dÃ¼ello atamazsÄ±nÄ±z." });

            var rakipVarMi = db.ExecuteScalar<int>("SELECT COUNT(*) FROM Kullanicilar WHERE Id = @id", new { id = opponent });
            if (rakipVarMi == 0) return Json(new { success = false, message = "Rakip bulunamadÃ„Â±." });

            var aktifDuello = db.ExecuteScalar<int>(
                @"SELECT COUNT(*) FROM Duellolar
                  WHERE Durum IN ('bekliyor','tasarim','oylama')
                  AND ((MeydanOkuyanId = @uid AND RakipId = @rid) OR (MeydanOkuyanId = @rid AND RakipId = @uid))",
                new { uid = userId, rid = opponent });
            if (aktifDuello > 0)
                return Json(new { success = false, message = "Bu rakiple zaten aktif bir dÃ¼ellonuz var." });

            var kategoriBasliklari = new Dictionary<string, string> {
                {"ui", "UI/UX TasarÄ±m DÃ¼ellosu"}, {"logo", "Logo & Branding DÃ¼ellosu"},
                {"illustration", "Ä°llÃ¼strasyon DÃ¼ellosu"}, {"3d", "3D Modelleme DÃ¼ellosu"},
                {"motion", "Motion Design DÃ¼ellosu"}, {"typography", "Tipografi DÃ¼ellosu"}
            };
            string baslik = kategoriBasliklari.GetValueOrDefault(category ?? "", "TasarÄ±m DÃ¼ellosu");

            var temizAciklama = Kartist.Helpers.InputValidator.SanitizeHtml(challenge ?? "");

            var duelloId = db.ExecuteScalar<int>(@"INSERT INTO Duellolar (MeydanOkuyanId, RakipId, Kategori, Baslik, Aciklama, Durum)
                         VALUES (@uid, @rid, @kat, @baslik, @aciklama, 'bekliyor');
                         SELECT CAST(SCOPE_IDENTITY() as int);",
                new { uid = userId, rid = opponent, kat = category ?? "ui", baslik, aciklama = temizAciklama });

            KazanXP(db, userId, 20, "duello_baslat", "DÃ¼ello baÅŸlattÄ±");

            var rakipEmail = db.ExecuteScalar<string>("SELECT Email FROM Kullanicilar WHERE Id = @id", new { id = opponent });
            if (!string.IsNullOrEmpty(rakipEmail))
            {
                db.Execute(@"INSERT INTO Bildirimler (KullaniciId, Tip, Mesaj, GonderenId)
                             VALUES (@kid, 'duello', @mesaj, @gid)",
                    new { kid = opponent, mesaj = "sana dÃ¼ello daveti gÃ¶nderdi!", gid = userId });
            }

            return Json(new { success = true, message = "DÃ¼ello daveti gÃ¶nderildi!" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DuelloKabulEt(int duelloId)
        {
            if (!User.Identity.IsAuthenticated)
                return Json(new { success = false, message = "GiriÅŸ yapmalÄ±sÄ±nÄ±z." });

            using var db = new SqlConnection(_conn);
            int userId = GetUserId(db, GetEmail());

            var duello = db.QueryFirstOrDefault("SELECT * FROM Duellolar WHERE Id = @id", new { id = duelloId });
            if (duello == null) return Json(new { success = false, message = "DÃ¼ello bulunamadÄ±." });
            if ((string)duello.Durum != "bekliyor") return Json(new { success = false, message = "Bu dÃ¼ello artÄ±k kabul edilemez." });
            if ((int)duello.RakipId != userId) return Json(new { success = false, message = "Bu dÃ¼ello size gÃ¶nderilmedi." });

            db.Execute("UPDATE Duellolar SET Durum = 'tasarim', KabulTarihi = GETUTCDATE() WHERE Id = @id", new { id = duelloId });
            KazanXP(db, userId, 10, "duello_kabul", "DÃ¼ello kabul etti");

            return Json(new { success = true, message = "DÃ¼ello kabul edildi! TasarÄ±mÄ±nÄ±zÄ± yÃ¼kleyin." });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DuelloReddet(int duelloId)
        {
            if (!User.Identity.IsAuthenticated)
                return Json(new { success = false, message = "GiriÅŸ yapmalÄ±sÄ±nÄ±z." });

            using var db = new SqlConnection(_conn);
            int userId = GetUserId(db, GetEmail());

            var duello = db.QueryFirstOrDefault("SELECT * FROM Duellolar WHERE Id = @id", new { id = duelloId });
            if (duello == null) return Json(new { success = false, message = "DÃ¼ello bulunamadÄ±." });
            if ((string)duello.Durum != "bekliyor") return Json(new { success = false, message = "Bu dÃ¼ello artÄ±k reddedilemez." });
            if ((int)duello.RakipId != userId) return Json(new { success = false, message = "Bu dÃ¼ello size gÃ¶nderilmedi." });

            db.Execute("UPDATE Duellolar SET Durum = 'reddedildi', BitisTarihi = GETUTCDATE() WHERE Id = @id", new { id = duelloId });
            return Json(new { success = true, message = "DÃ¼ello reddedildi." });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DuelloTasarimYukle(int duelloId, IFormFile tasarim)
        {
            if (!User.Identity.IsAuthenticated)
                return Json(new { success = false, message = "GiriÅŸ yapmalÄ±sÄ±nÄ±z." });

            using var db = new SqlConnection(_conn);
            int userId = GetUserId(db, GetEmail());

            var duello = db.QueryFirstOrDefault("SELECT * FROM Duellolar WHERE Id = @id", new { id = duelloId });
            if (duello == null) return Json(new { success = false, message = "DÃ¼ello bulunamadÄ±." });
            if ((string)duello.Durum != "tasarim") return Json(new { success = false, message = "Bu dÃ¼ello tasarÄ±m aÅŸamasÄ±nda deÄŸil." });

            bool isMeydanOkuyan = (int)duello.MeydanOkuyanId == userId;
            bool isRakip = (int)duello.RakipId == userId;
            if (!isMeydanOkuyan && !isRakip) return Json(new { success = false, message = "Bu dÃ¼ellonun katÄ±lÄ±mcÄ±sÄ± deÄŸilsiniz." });

            if (tasarim == null || tasarim.Length == 0)
                return Json(new { success = false, message = "Dosya seÃ§ilmedi." });

            if (!Kartist.Helpers.FileUploadValidator.TryValidateImage(tasarim, 10 * 1024 * 1024, out var ext, out var err))
                return Json(new { success = false, message = err });

            var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "duels");
            Directory.CreateDirectory(uploadsDir);
            var fileName = $"{Guid.NewGuid()}{ext}";
            var filePath = Path.Combine(uploadsDir, fileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
                await tasarim.CopyToAsync(stream);

            var gorselUrl = $"/uploads/duels/{fileName}";

            if (isMeydanOkuyan)
                db.Execute("UPDATE Duellolar SET MeydanOkuyanTasarimUrl = @url WHERE Id = @id", new { url = gorselUrl, id = duelloId });
            else
                db.Execute("UPDATE Duellolar SET RakipTasarimUrl = @url WHERE Id = @id", new { url = gorselUrl, id = duelloId });

            var updated = db.QueryFirstOrDefault("SELECT MeydanOkuyanTasarimUrl, RakipTasarimUrl FROM Duellolar WHERE Id = @id", new { id = duelloId });
            if (!string.IsNullOrEmpty((string)updated.MeydanOkuyanTasarimUrl) && !string.IsNullOrEmpty((string)updated.RakipTasarimUrl))
            {
                db.Execute("UPDATE Duellolar SET Durum = 'oylama', OylamaBaslangic = GETUTCDATE() WHERE Id = @id", new { id = duelloId });
            }

            KazanXP(db, userId, 30, "duello_tasarim", "DÃ¼ello tasarÄ±mÄ± yÃ¼kledi");
            return Json(new { success = true, message = "TasarÄ±mÄ±nÄ±z yÃ¼klendi!" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult VoteDuel(int id, int option)
        {
            if (!User.Identity.IsAuthenticated)
                return Json(new { success = false, message = "GiriÅŸ yapmalÄ±sÄ±nÄ±z." });

            try
            {
                using var db = new SqlConnection(_conn);
                int userId = GetUserId(db, GetEmail());
                if (userId == 0) return Json(new { success = false, message = "Kullanıcı bulunamadı." });

                var duello = db.QueryFirstOrDefault("SELECT * FROM Duellolar WHERE Id = @id", new { id });
                if (duello == null) return Json(new { success = false, message = "DÃ¼ello bulunamadÄ±." });
                if ((string)duello.Durum != "oylama") return Json(new { success = false, message = "Bu dÃ¼ello oylama aÅŸamasÄ±nda deÄŸil." });
                if ((int)duello.MeydanOkuyanId == userId || (int)duello.RakipId == userId)
                    return Json(new { success = false, message = "Kendi dÃ¼ellonuza oy veremezsiniz." });

                var mevcutOy = db.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM DuelloOylari WHERE DuelloId = @did AND KullaniciId = @uid",
                    new { did = id, uid = userId });
                if (mevcutOy > 0) return Json(new { success = false, message = "Bu dÃ¼elloya zaten oy verdiniz." });

                db.Execute("INSERT INTO DuelloOylari (DuelloId, KullaniciId, Secenek) VALUES (@did, @uid, @s)",
                    new { did = id, uid = userId, s = option });

                if (option == 1)
                    db.Execute("UPDATE Duellolar SET MeydanOkuyanOy = MeydanOkuyanOy + 1 WHERE Id = @id", new { id });
                else
                    db.Execute("UPDATE Duellolar SET RakipOy = RakipOy + 1 WHERE Id = @id", new { id });

                KazanXP(db, userId, 10, "duello_oy");
                GunlukGorevIlerle(db, userId, "duello");

                var oy = db.QueryFirstOrDefault("SELECT MeydanOkuyanOy, RakipOy FROM Duellolar WHERE Id = @id", new { id });
                int v1 = (int)oy.MeydanOkuyanOy;
                int v2 = (int)oy.RakipOy;
                int total = v1 + v2;
                int p1 = total > 0 ? v1 * 100 / total : 50;
                int p2 = 100 - p1;

                return Json(new { success = true, p1, p2, xp = 10, totalVotes = total, message = "Oy verildi!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        private void DuelloSureDolanlariGuncelle(SqlConnection db)
        {
            try
            {
                var suresiDolanTasarimlar = db.Query(
                    @"SELECT Id, MeydanOkuyanId, RakipId, MeydanOkuyanTasarimUrl, RakipTasarimUrl
                      FROM Duellolar WHERE Durum = 'tasarim'
                      AND DATEADD(hour, TasarimSuresiSaat, KabulTarihi) < GETUTCDATE()").ToList();

                foreach (var d in suresiDolanTasarimlar)
                {
                    bool ikisiDeYukledi = !string.IsNullOrEmpty((string)d.MeydanOkuyanTasarimUrl) && !string.IsNullOrEmpty((string)d.RakipTasarimUrl);
                    if (ikisiDeYukledi)
                        db.Execute("UPDATE Duellolar SET Durum = 'oylama', OylamaBaslangic = GETUTCDATE() WHERE Id = @id", new { id = (int)d.Id });
                    else
                        db.Execute("UPDATE Duellolar SET Durum = 'bitti', BitisTarihi = GETUTCDATE() WHERE Id = @id", new { id = (int)d.Id });
                }

                var suresiDolanOylamalar = db.Query(
                    @"SELECT Id, MeydanOkuyanId, RakipId, MeydanOkuyanOy, RakipOy
                      FROM Duellolar WHERE Durum = 'oylama'
                      AND DATEADD(hour, OylamaSuresiSaat, OylamaBaslangic) < GETUTCDATE()").ToList();

                foreach (var d in suresiDolanOylamalar)
                {
                    int v1 = (int)d.MeydanOkuyanOy, v2 = (int)d.RakipOy;
                    int? kazanan = v1 > v2 ? (int)d.MeydanOkuyanId : v2 > v1 ? (int)d.RakipId : (int?)null;
                    db.Execute("UPDATE Duellolar SET Durum = 'bitti', BitisTarihi = GETUTCDATE(), KazananId = @k WHERE Id = @id",
                        new { k = kazanan, id = (int)d.Id });
                    if (kazanan.HasValue) KazanXP(db, kazanan.Value, 100, "duello_kazanan", "DÃ¼ello kazandÄ±!");
                }
            }
            catch { }
        }

        // ===== LIVE =====
        [AllowAnonymous]
        public IActionResult Live()
        {
            using var db = new SqlConnection(_conn);
            string email = GetEmail() ?? "test@test.com";
            int userId = GetUserId(db, email);
            if (userId == 0) userId = 1;

            var user = db.QueryFirstOrDefault("SELECT Seviye as Level, ToplamXP as XP FROM Kullanicilar WHERE Id = @id", new { id = userId });
            ViewBag.UserLevel = user?.Level ?? 1;
            ViewBag.UserXP = user?.XP ?? 0;
            ViewBag.UserMaxXP = ((user?.Level ?? 1) + 1) * 200;
            ViewBag.CurrentUserId = userId;

            List<dynamic> liveRooms;
            try
            {
                liveRooms = db.Query(@"
                    SELECT cy.Id, cy.Baslik as Title, cy.Etiketler,
                           cy.IzleyiciSayisi as Viewers, cy.BaslangicTarihi,
                           k.AdSoyad as HostName, k.Seviye as HostLevel,
                           ISNULL(NULLIF(k.ProfilResmi, ''),
                                  'https://ui-avatars.com/api/?name=' + REPLACE(k.AdSoyad, ' ', '+') + '&background=random&size=128') as HostAvatar,
                           CAST(1 AS BIT) as IsLive
                    FROM CanliYayinlar cy
                    JOIN Kullanicilar k ON cy.YayinciId = k.Id
                    WHERE cy.Aktif = 1
                    ORDER BY cy.BaslangicTarihi DESC").ToList();
            }
            catch { liveRooms = new List<dynamic>(); }

            ViewBag.LiveRooms = liveRooms;
            ViewBag.LiveCount = liveRooms.Count;

            return View();
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public IActionResult YayinBaslat(string baslik, string etiketler)
        {
            try
            {
                string email = GetEmail();
                if (email == null) return Json(new { success = false, message = "Giris yapmaniz gerekiyor." });

                using var db = new SqlConnection(_conn);
                EnsureCanliYayinlarTable(db);

                int userId = GetUserId(db, email);
                if (userId == 0) return Json(new { success = false, message = "Kullanici bulunamadi." });

                var aktifYayin = db.ExecuteScalar<int>("SELECT COUNT(*) FROM CanliYayinlar WHERE YayinciId = @uid AND Aktif = 1", new { uid = userId });
                if (aktifYayin > 0) return Json(new { success = false, message = "Zaten aktif bir yayininiz var." });

                var streamId = db.ExecuteScalar<int>(@"
                    INSERT INTO CanliYayinlar (YayinciId, Baslik, Etiketler)
                    VALUES (@uid, @baslik, @etiketler);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);",
                    new { uid = userId, baslik = baslik ?? "Kartist Live", etiketler });

                KazanXP(db, userId, 30, "canli_yayin", "Canli yayin baslatti");

                return Json(new { success = true, streamId });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Yayin baslatilamadi: " + ex.Message });
            }
        }

        private void EnsureCanliYayinlarTable(SqlConnection db)
        {
            db.Execute(@"
                IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('CanliYayinlar') AND type = 'U')
                BEGIN
                    CREATE TABLE CanliYayinlar (
                        Id INT IDENTITY(1,1) PRIMARY KEY,
                        YayinciId INT NOT NULL,
                        Baslik NVARCHAR(200) NOT NULL,
                        Etiketler NVARCHAR(500) NULL,
                        BaslangicTarihi DATETIME NOT NULL DEFAULT GETUTCDATE(),
                        BitisTarihi DATETIME NULL,
                        Aktif BIT NOT NULL DEFAULT 1,
                        IzleyiciSayisi INT NOT NULL DEFAULT 0,
                        FOREIGN KEY (YayinciId) REFERENCES Kullanicilar(Id)
                    );
                END");
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public IActionResult YayinBitir(int yayinId)
        {
            try
            {
                string email = GetEmail();
                if (email == null) return Json(new { success = false });

                using var db = new SqlConnection(_conn);
                int userId = GetUserId(db, email);

                db.Execute(@"UPDATE CanliYayinlar SET Aktif = 0, BitisTarihi = GETUTCDATE()
                             WHERE Id = @id AND YayinciId = @uid", new { id = yayinId, uid = userId });

                return Json(new { success = true });
            }
            catch { return Json(new { success = false }); }
        }

        [HttpGet]
        [IgnoreAntiforgeryToken]
        public IActionResult GetAktifYayinlar()
        {
            try
            {
                using var db = new SqlConnection(_conn);
                var rooms = db.Query(@"
                    SELECT cy.Id, cy.Baslik as Title, cy.Etiketler, cy.IzleyiciSayisi as Viewers,
                           k.AdSoyad as HostName, k.Seviye as HostLevel,
                           ISNULL(NULLIF(k.ProfilResmi, ''),
                                  'https://ui-avatars.com/api/?name=' + REPLACE(k.AdSoyad, ' ', '+') + '&background=random&size=128') as HostAvatar
                    FROM CanliYayinlar cy
                    JOIN Kullanicilar k ON cy.YayinciId = k.Id
                    WHERE cy.Aktif = 1
                    ORDER BY cy.BaslangicTarihi DESC").ToList();
                return Json(new { success = true, rooms });
            }
            catch { return Json(new { success = true, rooms = new List<dynamic>() }); }
        }

        // ===== COMPETITIONS =====
        [AllowAnonymous]
        public IActionResult Competitions()
        {
            using var db = new SqlConnection(_conn);
            EnsureYarismaTables(db);
            string email = GetEmail() ?? "test@test.com";
            int userId = GetUserId(db, email);
            if (userId == 0) userId = 1;
            
            var user = db.QueryFirstOrDefault("SELECT Seviye as Level, ToplamXP as XP FROM Kullanicilar WHERE Id = @id", new { id = userId });
            ViewBag.UserLevel = user?.Level ?? 1;
            ViewBag.UserXP = user?.XP ?? 0;
            ViewBag.UserMaxXP = ((user?.Level ?? 1) + 1) * 200;

            YarismaDurumlariniGuncelle(db);

            ViewBag.ActiveCompetitions = db.ExecuteScalar<int>("SELECT COUNT(*) FROM Yarismalar WHERE Durum IN ('active','voting')");
            ViewBag.TotalParticipants = db.ExecuteScalar<int>("SELECT COUNT(*) FROM YarismaKatilimlari");
            ViewBag.MonthlyWinners = db.ExecuteScalar<int>("SELECT COUNT(*) FROM Yarismalar WHERE Durum = 'ended' AND OylamaBitisTarihi >= DATEADD(day, -30, GETUTCDATE())");

            ViewBag.Competitions = db.Query(@"
                SELECT y.Id, y.Baslik as Title, y.Aciklama as Description, y.KapakUrl as CoverImage,
                       y.Tema as Theme, y.Odul as Prize, y.Durum as Status,
                       COUNT(k.Id) as Participants,
                       CASE
                           WHEN y.Durum = 'active' THEN CONCAT(DATEDIFF(day, GETUTCDATE(), y.SonKatilimTarihi), ' gun')
                           WHEN y.Durum = 'voting' THEN CONCAT(DATEDIFF(day, GETUTCDATE(), y.OylamaBitisTarihi), ' gun')
                           ELSE 'tamamlandi'
                       END as Deadline,
                       MAX(CASE WHEN k.KullaniciId = @uid THEN 1 ELSE 0 END) as Joined
                FROM Yarismalar y
                LEFT JOIN YarismaKatilimlari k ON k.YarismaId = y.Id
                GROUP BY y.Id, y.Baslik, y.Aciklama, y.KapakUrl, y.Tema, y.Odul, y.Durum, y.SonKatilimTarihi, y.OylamaBitisTarihi, y.OlusturmaTarihi
                ORDER BY CASE y.Durum WHEN 'active' THEN 0 WHEN 'voting' THEN 1 ELSE 2 END, y.OlusturmaTarihi DESC",
                new { uid = userId }).ToList();

            ViewBag.TopEntries = db.Query(@"
                SELECT TOP 8 k.Id, k.YarismaId, k.Baslik, k.GorselUrl, k.OySayisi,
                       y.Baslik as YarismaBaslik, y.Odul,
                       u.AdSoyad as KullaniciAdi,
                       ISNULL(NULLIF(u.ProfilResmi,''), '/img/default-user.png') as ProfilResmi
                FROM YarismaKatilimlari k
                JOIN Yarismalar y ON y.Id = k.YarismaId
                JOIN Kullanicilar u ON u.Id = k.KullaniciId
                ORDER BY k.OySayisi DESC, k.OlusturmaTarihi DESC").ToList();
            
            return View();
        }

        private void EnsureYarismaTables(SqlConnection db)
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
END
");
            db.Execute(@"
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('YarismaKatilimlari') AND name = 'YarismaId')
    ALTER TABLE YarismaKatilimlari ADD YarismaId INT NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('YarismaKatilimlari') AND name = 'KullaniciId')
    ALTER TABLE YarismaKatilimlari ADD KullaniciId INT NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('YarismaKatilimlari') AND name = 'Baslik')
    ALTER TABLE YarismaKatilimlari ADD Baslik NVARCHAR(200) NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('YarismaKatilimlari') AND name = 'Aciklama')
    ALTER TABLE YarismaKatilimlari ADD Aciklama NVARCHAR(1000) NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('YarismaKatilimlari') AND name = 'GorselUrl')
    ALTER TABLE YarismaKatilimlari ADD GorselUrl NVARCHAR(500) NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('YarismaKatilimlari') AND name = 'OySayisi')
    ALTER TABLE YarismaKatilimlari ADD OySayisi INT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('YarismaKatilimlari') AND name = 'OlusturmaTarihi')
    ALTER TABLE YarismaKatilimlari ADD OlusturmaTarihi DATETIME NOT NULL DEFAULT GETUTCDATE();
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
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Yarismalar') AND name = 'Title')
    ALTER TABLE Yarismalar ADD Title NVARCHAR(200) NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Yarismalar') AND name = 'Description')
    ALTER TABLE Yarismalar ADD Description NVARCHAR(1000) NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Yarismalar') AND name = 'Theme')
    ALTER TABLE Yarismalar ADD Theme NVARCHAR(100) NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Yarismalar') AND name = 'Prize')
    ALTER TABLE Yarismalar ADD Prize NVARCHAR(100) NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Yarismalar') AND name = 'CoverImage')
    ALTER TABLE Yarismalar ADD CoverImage NVARCHAR(500) NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Yarismalar') AND name = 'Status')
    ALTER TABLE Yarismalar ADD Status NVARCHAR(20) NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Yarismalar') AND name = 'EndDate')
    ALTER TABLE Yarismalar ADD EndDate DATETIME NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Yarismalar') AND name = 'CreatedById')
    ALTER TABLE Yarismalar ADD CreatedById INT NULL;
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Yarismalar') AND name = 'Title' AND is_nullable = 0)
    ALTER TABLE Yarismalar ALTER COLUMN Title NVARCHAR(200) NULL;
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Yarismalar') AND name = 'Description' AND is_nullable = 0)
    ALTER TABLE Yarismalar ALTER COLUMN Description NVARCHAR(1000) NULL;
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Yarismalar') AND name = 'CoverImage' AND is_nullable = 0)
    ALTER TABLE Yarismalar ALTER COLUMN CoverImage NVARCHAR(500) NULL;
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Yarismalar') AND name = 'Theme' AND is_nullable = 0)
    ALTER TABLE Yarismalar ALTER COLUMN Theme NVARCHAR(100) NULL;
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Yarismalar') AND name = 'Prize' AND is_nullable = 0)
    ALTER TABLE Yarismalar ALTER COLUMN Prize NVARCHAR(100) NULL;
");
            db.Execute(@"
UPDATE Yarismalar
SET Baslik = ISNULL(Baslik, Title),
    Aciklama = ISNULL(Aciklama, Description),
    Tema = ISNULL(Tema, Theme),
    Odul = ISNULL(Odul, Prize),
    KapakUrl = ISNULL(KapakUrl, CoverImage),
    Durum = ISNULL(Durum, Status),
    SonKatilimTarihi = ISNULL(SonKatilimTarihi, EndDate),
    OylamaBitisTarihi = ISNULL(OylamaBitisTarihi, DATEADD(day, 3, EndDate));

IF NOT EXISTS (SELECT 1 FROM Yarismalar)
BEGIN
    DECLARE @seedUserId INT = ISNULL((SELECT TOP 1 Id FROM Kullanicilar ORDER BY Id), 1);
    INSERT INTO Yarismalar (Baslik, Aciklama, Tema, Odul, KapakUrl, Durum, SonKatilimTarihi, OylamaBitisTarihi, Title, EndDate, CreatedById) VALUES
    ('Gelecek Icin Tasarla', 'Surdurulebilirlik temali afis, sosyal medya veya arayuz tasarimi.', 'Sustainability', '10,000 TL + Premium', 'https://images.unsplash.com/photo-1451187580459-43490279c0fa?w=800', 'active', DATEADD(day, 5, GETUTCDATE()), DATEADD(day, 8, GETUTCDATE()), 'Gelecek Icin Tasarla', DATEADD(day, 5, GETUTCDATE()), @seedUserId),
    ('AI x Design', 'Yapay zeka ve tasarimin kesisim noktasini anlatan etkileyici bir calisma.', 'AI', '15,000 TL + Davetiye', 'https://images.unsplash.com/photo-1677442136019-21780ecad995?w=800', 'voting', DATEADD(day, -1, GETUTCDATE()), DATEADD(day, 3, GETUTCDATE()), 'AI x Design', DATEADD(day, 3, GETUTCDATE()), @seedUserId),
    ('Kartist Master Design Challenge #1', 'En iyi koyu tema deneyimini kim tasarlayacak?', 'Dark Mode', '5,000 TL', 'https://images.unsplash.com/photo-1618005182384-a83a8bd57fbe?w=800', 'ended', DATEADD(day, -10, GETUTCDATE()), DATEADD(day, -2, GETUTCDATE()), 'Kartist Master Design Challenge #1', DATEADD(day, -2, GETUTCDATE()), @seedUserId);
END");
        }

        private void YarismaDurumlariniGuncelle(SqlConnection db)
        {
            db.Execute("UPDATE Yarismalar SET Durum = 'voting' WHERE Durum = 'active' AND SonKatilimTarihi < GETUTCDATE()");
            db.Execute("UPDATE Yarismalar SET Durum = 'ended' WHERE Durum = 'voting' AND OylamaBitisTarihi IS NOT NULL AND OylamaBitisTarihi < GETUTCDATE()");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> YarismaKatil(int yarismaId, string baslik, string aciklama, IFormFile tasarim)
        {
            if (CurrentUserId == 0) return Json(new { success = false, message = "Giris yapmalisin." });
            using var db = new SqlConnection(_conn);
            EnsureYarismaTables(db);

            var yarisma = db.QueryFirstOrDefault("SELECT * FROM Yarismalar WHERE Id = @id", new { id = yarismaId });
            if (yarisma == null) return Json(new { success = false, message = "YarÄ±ÅŸma bulunamadÄ±." });
            if ((string)yarisma.Durum != "active") return Json(new { success = false, message = "Bu yarÄ±ÅŸma katÄ±lÄ±ma kapalÄ±." });

            if (db.ExecuteScalar<int>("SELECT COUNT(*) FROM YarismaKatilimlari WHERE YarismaId = @yid AND KullaniciId = @uid", new { yid = yarismaId, uid = CurrentUserId }) > 0)
                return Json(new { success = false, message = "Bu yarÄ±ÅŸmaya zaten katÄ±ldÄ±n." });

            if (!Kartist.Helpers.FileUploadValidator.TryValidateImage(tasarim, 10 * 1024 * 1024, out var ext, out var err))
                return Json(new { success = false, message = err });

            var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "competitions");
            Directory.CreateDirectory(uploadsDir);
            var fileName = $"yarisma_{Guid.NewGuid():N}{ext}";
            var filePath = Path.Combine(uploadsDir, fileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
                await tasarim.CopyToAsync(stream);

            var gorselUrl = $"/uploads/competitions/{fileName}";
            var temizAciklama = Kartist.Helpers.InputValidator.SanitizeHtml(aciklama ?? "");
            db.Execute(@"INSERT INTO YarismaKatilimlari (YarismaId, KullaniciId, Baslik, Aciklama, GorselUrl)
                         VALUES (@yid, @uid, @baslik, @aciklama, @url)",
                new { yid = yarismaId, uid = CurrentUserId, baslik = string.IsNullOrWhiteSpace(baslik) ? (string)yarisma.Baslik : baslik, aciklama = temizAciklama, url = gorselUrl });

            KazanXP(db, CurrentUserId, 60, "yarisma_katilim", "YarÄ±ÅŸmaya katÄ±ldÄ±");
            return Json(new { success = true, message = "TasarÄ±mÄ±n yarÄ±ÅŸmaya eklendi!" });
        }

        [HttpGet]
        public IActionResult YarismaKatilimlari(int yarismaId)
        {
            using var db = new SqlConnection(_conn);
            EnsureYarismaTables(db);
            var entries = db.Query(@"
                SELECT k.Id, k.YarismaId, k.Baslik, k.Aciklama, k.GorselUrl, k.OySayisi,
                       u.AdSoyad as KullaniciAdi,
                       ISNULL(NULLIF(u.ProfilResmi,''), '/img/default-user.png') as ProfilResmi
                FROM YarismaKatilimlari k
                JOIN Kullanicilar u ON u.Id = k.KullaniciId
                WHERE k.YarismaId = @id
                ORDER BY k.OySayisi DESC, k.OlusturmaTarihi DESC", new { id = yarismaId }).ToList();
            return Json(new { success = true, entries });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult YarismaOyVer(int yarismaId, int katilimId)
        {
            if (CurrentUserId == 0) return Json(new { success = false, message = "Giris yapmalisin." });
            using var db = new SqlConnection(_conn);
            EnsureYarismaTables(db);
            var durum = db.ExecuteScalar<string>("SELECT Durum FROM Yarismalar WHERE Id = @id", new { id = yarismaId });
            if (durum != "voting") return Json(new { success = false, message = "Bu yarÄ±ÅŸma oylamada deÄŸil." });
            if (db.ExecuteScalar<int>("SELECT COUNT(*) FROM YarismaOylari WHERE YarismaId = @yid AND KullaniciId = @uid", new { yid = yarismaId, uid = CurrentUserId }) > 0)
                return Json(new { success = false, message = "Bu yarÄ±ÅŸmada zaten oy kullandÄ±n." });

            db.Execute("INSERT INTO YarismaOylari (YarismaId, KatilimId, KullaniciId) VALUES (@yid, @kid, @uid)", new { yid = yarismaId, kid = katilimId, uid = CurrentUserId });
            db.Execute("UPDATE YarismaKatilimlari SET OySayisi = OySayisi + 1 WHERE Id = @id", new { id = katilimId });
            KazanXP(db, CurrentUserId, 10, "yarisma_oy", "YarÄ±ÅŸma oyu kullandÄ±");
            var oy = db.ExecuteScalar<int>("SELECT OySayisi FROM YarismaKatilimlari WHERE Id = @id", new { id = katilimId });
            return Json(new { success = true, votes = oy, message = "Oyun kaydedildi!" });
        }

        // ===== SETTINGS =====
        [AllowAnonymous]
        public IActionResult Settings()
        {
            using var db = new SqlConnection(_conn);
            string email = GetEmail() ?? "test@test.com";
            int userId = GetUserId(db, email);
            if (userId == 0) userId = 1;
            
            var user = db.QueryFirstOrDefault(@"SELECT Id, AdSoyad, Email, Biyografi, ProfilResmi, ProfilTema, IkiFactorAktif,
                                                       Seviye as Level, ToplamXP as XP
                                                FROM Kullanicilar WHERE Id = @id", new { id = userId });
            ViewBag.UserLevel = user?.Level ?? 1;
            ViewBag.UserXP = user?.XP ?? 0;
            ViewBag.UserMaxXP = ((user?.Level ?? 1) + 1) * 200;
            ViewBag.ProfileUser = user;
            
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AyarlarGuncelle(string adSoyad, string biyografi, string profilTema, IFormFile profilResmi)
        {
            using var db = new SqlConnection(_conn);
            string email = GetEmail() ?? "test@test.com";
            int userId = GetUserId(db, email);
            if (userId == 0) userId = 1;

            adSoyad = Helpers.InputValidator.SanitizeHtml(adSoyad ?? "").Trim();
            biyografi = Helpers.InputValidator.SanitizeHtml(biyografi ?? "").Trim();
            profilTema = string.IsNullOrWhiteSpace(profilTema) ? "#c6ff00" : profilTema.Trim();

            if (adSoyad.Length < 2 || adSoyad.Length > 100)
                return Json(new { success = false, message = "Ad soyad 2-100 karakter aralığında olmalı." });
            if (biyografi.Length > 500)
                return Json(new { success = false, message = "Biyografi en fazla 500 karakter olabilir." });
            if (!System.Text.RegularExpressions.Regex.IsMatch(profilTema, "^#[0-9a-fA-F]{6}$"))
                profilTema = "#c6ff00";

            string profilUrl = null;
            if (profilResmi != null && profilResmi.Length > 0)
            {
                if (!Kartist.Helpers.FileUploadValidator.TryValidateImage(profilResmi, 5 * 1024 * 1024, out var ext, out var err))
                    return Json(new { success = false, message = err });

                var klasor = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "avatars");
                Directory.CreateDirectory(klasor);
                var dosyaAdi = $"avatar_{userId}_{Guid.NewGuid():N}{ext}";
                var yol = Path.Combine(klasor, dosyaAdi);
                await using var stream = new FileStream(yol, FileMode.Create);
                await profilResmi.CopyToAsync(stream);
                profilUrl = "/uploads/avatars/" + dosyaAdi;
            }

            db.Execute(@"
                UPDATE Kullanicilar
                SET AdSoyad = @ad,
                    Biyografi = @bio,
                    ProfilTema = @tema,
                    ProfilResmi = COALESCE(@resim, ProfilResmi)
                WHERE Id = @uid",
                new { ad = adSoyad, bio = biyografi, tema = profilTema, resim = profilUrl, uid = userId });

            return Json(new { success = true, message = "Ayarlar kaydedildi.", profilResmi = profilUrl });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AyarSifreDegistir(string mevcutSifre, string yeniSifre)
        {
            if (string.IsNullOrWhiteSpace(mevcutSifre) || string.IsNullOrWhiteSpace(yeniSifre))
                return Json(new { success = false, message = "Mevcut şifre ve yeni şifre gerekli." });
            if (yeniSifre.Length < 6 || yeniSifre.Length > 50)
                return Json(new { success = false, message = "Yeni şifre 6-50 karakter aralığında olmalı." });

            using var db = new SqlConnection(_conn);
            string email = GetEmail() ?? "test@test.com";
            int userId = GetUserId(db, email);
            if (userId == 0) userId = 1;

            var user = db.QueryFirstOrDefault("SELECT Sifre FROM Kullanicilar WHERE Id = @id", new { id = userId });
            if (user == null) return Json(new { success = false, message = "Kullanıcı bulunamadı." });

            string dbSifre = (string)user.Sifre;
            bool dogruMu = Kartist.Helpers.PasswordHasher.IsHashed(dbSifre)
                && Kartist.Helpers.PasswordHasher.VerifyPassword(mevcutSifre, dbSifre);
            if (!dogruMu) return Json(new { success = false, message = "Mevcut şifre hatalı." });

            string hashed = Kartist.Helpers.PasswordHasher.HashPassword(yeniSifre);
            db.Execute("UPDATE Kullanicilar SET Sifre = @s WHERE Id = @id", new { s = hashed, id = userId });
            return Json(new { success = true, message = "Şifren başarıyla güncellendi." });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AyarIkiFactor(bool aktif, string mevcutSifre)
        {
            using var db = new SqlConnection(_conn);
            string email = GetEmail() ?? "test@test.com";
            int userId = GetUserId(db, email);
            if (userId == 0) userId = 1;

            if (!aktif)
            {
                var user = db.QueryFirstOrDefault("SELECT Sifre FROM Kullanicilar WHERE Id = @id", new { id = userId });
                string dbSifre = user == null ? "" : (string)user.Sifre;
                bool dogruMu = Kartist.Helpers.PasswordHasher.IsHashed(dbSifre)
                    && Kartist.Helpers.PasswordHasher.VerifyPassword(mevcutSifre ?? "", dbSifre);
                if (!dogruMu) return Json(new { success = false, message = "2FA kapatmak için mevcut şifren gerekli." });
            }

            db.Execute("UPDATE Kullanicilar SET IkiFactorAktif = @aktif WHERE Id = @id", new { aktif, id = userId });
            return Json(new { success = true, message = aktif ? "İki faktörlü doğrulama açıldı." : "İki faktörlü doğrulama kapatıldı." });
        }

        // ===== DESIGN DETAIL =====
        [AllowAnonymous]
        public IActionResult Design(int id)
        {
            using var db = new SqlConnection(_conn);
            string email = GetEmail() ?? "test@test.com";
            int userId = GetUserId(db, email);
            if (userId == 0) userId = 1;
            
            // Kullanici bilgileri
            var user = db.QueryFirstOrDefault("SELECT Seviye as Level, ToplamXP as XP FROM Kullanicilar WHERE Id = @id", new { id = userId });
            ViewBag.UserLevel = user?.Level ?? 1;
            ViewBag.UserXP = user?.XP ?? 0;
            ViewBag.UserMaxXP = ((user?.Level ?? 1) + 1) * 200;
            
            // Design detail
            var design = db.QueryFirstOrDefault(@"
                SELECT g.Id, g.Icerik as Title, g.GorselUrl as ImageUrl, g.BegeniSayisi as Likes, 
                       g.YorumSayisi as Comments, g.GoruntulemeSayisi as Views, 
                       g.Icerik as Description,
                       k.Id as DesignerId, k.AdSoyad as DesignerName, 
                       k.Seviye as DesignerLevel, REPLACE(k.Email, '@gmail.com', '') as DesignerUsername,
                       CASE WHEN k.ProfilResmi IS NULL OR k.ProfilResmi = '' OR k.ProfilResmi LIKE '/uploads/%'
                            THEN 'https://ui-avatars.com/api/?name=' + REPLACE(k.AdSoyad, ' ', '+') + '&background=random&size=128'
                            ELSE k.ProfilResmi END as DesignerAvatar
                FROM SosyalGonderiler g
                JOIN Kullanicilar k ON g.KullaniciId = k.Id
                WHERE g.Id = @id", new { id });
            
            if (design == null)
            {
                return RedirectToAction("Trending");
            }
            
            // Comments
            ViewBag.Comments = new List<dynamic> {
                new { Id = 1, UserName = "Ahmet Yilmaz", UserAvatar = "https://ui-avatars.com/api/?name=Ahmet+Yilmaz&background=random&size=128", Text = "Harika bir calisma!", Time = "2 saat once", Likes = 12 },
                new { Id = 2, UserName = "Mehmet Can", UserAvatar = "https://ui-avatars.com/api/?name=Mehmet+Can&background=random&size=128", Text = "Mukemmel!", Time = "1 saat once", Likes = 5 }
            };
            
            // Related designs
            ViewBag.RelatedDesigns = db.Query(@"
                SELECT TOP 3 g.Id, g.Icerik as Title, g.GorselUrl as ImageUrl
                FROM SosyalGonderiler g
                WHERE g.Id != @id
                ORDER BY g.BegeniSayisi DESC", new { id }).ToList();
            
            // Designer stats
            ViewBag.DesignerStats = new { Designs = 127, Followers = 5200, Following = 892 };
            
            // Color Palette
            ViewBag.ColorPalette = new[] { "#c6ff00", "#ec4899", "#3b82f6", "#8b5cf6", "#f59e0b", "#10b981", "#ef4444", "#06b6d4", "#f97316", "#6366f1" };
            
            return View(design);
        }

        // ===== GÃ–NDERÄ° OLUÅTUR =====
        [HttpPost]
        public async Task<IActionResult> GonderiOlustur(string icerik, IFormFile gorsel, IFormFile onceSonraGorsel, string kodSinipet)
        {
            if (CurrentUserId == 0) return Json(new { success = false, message = "GiriÅŸ yapmalÄ±sÄ±n." });

            string webRootPath = System.IO.Directory.GetCurrentDirectory() + "/wwwroot";
            
            dynamic result = await _socialService.CreatePostAsync(CurrentUserId, icerik, gorsel, onceSonraGorsel, kodSinipet, webRootPath);
            
            if (result.success)
            {
                // Gamification triggers after successful creation
                using var db = new SqlConnection(_conn);
                KazanXP(db, CurrentUserId, 50, "gonderi", "Yeni gÃ¶nderi paylaÅŸtÄ±n");
                GunlukGorevIlerle(db, CurrentUserId, "gonderi");
                RozetKontrol(db, CurrentUserId);
            }

            return Json(result);
        }

        // ===== GÃ–NDERÄ° SÄ°L =====
        [HttpPost]
        public async Task<IActionResult> GonderiSil(int gonderiId)
        {
            if (CurrentUserId == 0) return Json(new { success = false, message = "GiriÅŸ yapmalÄ±sÄ±n." });
            
            string webRootPath = System.IO.Directory.GetCurrentDirectory() + "/wwwroot";
            var result = await _socialService.DeletePostAsync(gonderiId, CurrentUserId, webRootPath);
            return Json(result);
        }

        // ===== GÃ–NDERÄ° DÃœZENLE =====
        [HttpPost]
        public async Task<IActionResult> GonderiDuzenle(int gonderiId, string icerik)
        {
            if (CurrentUserId == 0) return Json(new { success = false, message = "GiriÅŸ yapmalÄ±sÄ±n." });

            var result = await _socialService.EditPostAsync(gonderiId, CurrentUserId, icerik);
            return Json(result);
        }

        // (Eski duplicate Repost metodu kaldÄ±rÄ±ldÄ± - gÃ¼ncel versiyon aÅŸaÄŸÄ±da Sprint 2 bÃ¶lÃ¼mÃ¼nde)

        // Hikaye metodlarÄ± aÅŸaÄŸÄ±da tanÄ±mlÄ± olduÄŸu iÃ§in burasÄ± silindi.

        // ===== BEÄENÄ° =====
        // ===== BEÄENÄ° =====
        [HttpPost]
        public async Task<IActionResult> Begen(int gonderiId)
        {
            if (CurrentUserId == 0) return Json(new { success = false });

            // Call Service
            var result = await _socialService.ToggleLikeAsync(gonderiId, CurrentUserId);

            int xpGained = 0;

            if (result.liked)
            {
                // Send Notification if we liked someone else's post
                if (result.ownerId != 0 && result.ownerId != CurrentUserId)
                {
                    using var db = new SqlConnection(_conn);
                    var ad = db.ExecuteScalar<string>("SELECT AdSoyad FROM Kullanicilar WHERE Id = @id", new { id = CurrentUserId });
                    string msg = $"{ad} gÃ¶nderini beÄŸendi â¤ï¸";
                    
                    db.Execute(@"INSERT INTO Bildirimler (KullaniciId, Tip, Mesaj, BaglantiliId, GonderenId)
                                 VALUES (@kid, 'begeni', @msg, @gid, @sid)",
                        new { kid = result.ownerId, msg, gid = gonderiId, sid = CurrentUserId });
                        
                    if (!string.IsNullOrEmpty(result.ownerEmail) && Kartist.Hubs.NotificationHub.UserConnections.TryGetValue(result.ownerEmail, out var connId))
                    {
                        await _hubContext.Clients.Client(connId).SendAsync("ReceiveNotification", msg, $"/Social/Profil/{CurrentUserId}", "begeni");
                    }
                }

                // Internal Gamification
                using var db2 = new SqlConnection(_conn);
                KazanXP(db2, CurrentUserId, 5, "begeni");
                GunlukGorevIlerle(db2, CurrentUserId, "begeni");
                RozetKontrol(db2, CurrentUserId);
                xpGained = 5;
            }

            return Json(new { success = true, liked = result.liked, count = result.begeniSayisi, xp = xpGained });
        }

        // ===== YORUM =====
        [HttpPost]
        public async Task<IActionResult> YorumYap(int gonderiId, string icerik, int? ustYorumId = null)
        {
            if (CurrentUserId == 0) return Json(new { success = false });

            dynamic result = await _socialService.CreateCommentAsync(CurrentUserId, gonderiId, icerik, ustYorumId);
            
            if (result.success)
            {
                // Bildirim gÃ¶nder (Gonderi Sahibine)
                using var db = new SqlConnection(_conn);
                var gonderiSahibiID = db.ExecuteScalar<int>("SELECT KullaniciId FROM SosyalGonderiler WHERE Id = @gid", new { gid = gonderiId });
                
                if (gonderiSahibiID != 0 && gonderiSahibiID != CurrentUserId)
                {
                    var ad = db.ExecuteScalar<string>("SELECT AdSoyad FROM Kullanicilar WHERE Id = @id", new { id = CurrentUserId });
                    var email = db.ExecuteScalar<string>("SELECT Email FROM Kullanicilar WHERE Id = @id", new { id = gonderiSahibiID });
                    string msg = $"{ad} gÃ¶nderine yorum yaptÄ± ğŸ’¬";
                    
                    db.Execute(@"INSERT INTO Bildirimler (KullaniciId, Tip, Mesaj, BaglantiliId, GonderenId)
                                 VALUES (@kid, 'yorum', @msg, @gid, @sid)",
                        new { kid = gonderiSahibiID, msg, gid = gonderiId, sid = CurrentUserId });
                        
                    if (!string.IsNullOrEmpty(email) && Kartist.Hubs.NotificationHub.UserConnections.TryGetValue(email, out var connId))
                    {
                        await _hubContext.Clients.Client(connId).SendAsync("ReceiveNotification", msg, $"/Social/Profil/{CurrentUserId}", "yorum");
                    }
                }

                KazanXP(db, CurrentUserId, 15, "yorum");
                GunlukGorevIlerle(db, CurrentUserId, "yorum");
                RozetKontrol(db, CurrentUserId);

                return Json(new { success = true, id = result.id, xp = 15 });
            }

            return Json(result);
        }
        
        [HttpGet]
        public async Task<IActionResult> GetYorumlar(int gonderiId)
        {
            var result = await _socialService.GetCommentsAsync(gonderiId);
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> YorumSil(int yorumId)
        {
            if (CurrentUserId == 0) return Json(new { success = false });
            var result = await _socialService.DeleteCommentAsync(yorumId, CurrentUserId);
            return Json(result);
        }

        // ===== TAKÄ°P =====
        [HttpPost]
        public IActionResult TakipEt(int hedefId)
        {
            string email = GetEmail();
            if (email == null) return Json(new { success = false });

            using var db = new SqlConnection(_conn);
            int userId = GetUserId(db, email);
            if (userId == hedefId) return Json(new { success = false, message = "Kendini takip edemezsin." });

            var varMi = db.ExecuteScalar<int>("SELECT COUNT(*) FROM Takipciler WHERE TakipEdenId = @ben AND TakipEdilenId = @o",
                new { ben = userId, o = hedefId });

            if (varMi > 0)
            {
                db.Execute("DELETE FROM Takipciler WHERE TakipEdenId = @ben AND TakipEdilenId = @o", new { ben = userId, o = hedefId });
                return Json(new { success = true, following = false });
            }
            else
            {
                db.Execute("INSERT INTO Takipciler (TakipEdenId, TakipEdilenId) VALUES (@ben, @o)", new { ben = userId, o = hedefId });

                var ad = db.ExecuteScalar<string>("SELECT AdSoyad FROM Kullanicilar WHERE Id = @id", new { id = userId });
                db.Execute(@"INSERT INTO Bildirimler (KullaniciId, Tip, Mesaj, GonderenId)
                             VALUES (@kid, 'takip', @msg, @sid)",
                    new { kid = hedefId, msg = $"{ad} seni takip etmeye baÅŸladÄ± ğŸ””", sid = userId });

                KazanXP(db, userId, 10, "takip");
                GunlukGorevIlerle(db, userId, "takip");
                RozetKontrol(db, userId);

                return Json(new { success = true, following = true });
            }
        }

        // ===== SOSYAL PROFÄ°L =====
        public IActionResult Profil(string id)
        {
            if (!User.Identity.IsAuthenticated) return RedirectToAction("Giris", "Account");
            
            using var db = new SqlConnection(_conn);
            int profilId = 0;
            string email = GetEmail();
            int myId = GetUserId(db, email);
            
            // "me" = kendi profilim
            if (string.IsNullOrEmpty(id) || id.Equals("me", StringComparison.OrdinalIgnoreCase))
            {
                profilId = myId;
            }
            else if (int.TryParse(id, out int numId))
            {
                profilId = numId;
            }
            else
            {
                profilId = db.ExecuteScalar<int>(
                    "SELECT TOP 1 Id FROM Kullanicilar WHERE AdSoyad = @name OR Email LIKE @prefix + '%'",
                    new { name = id, prefix = id });
            }

            if (profilId == 0) profilId = myId;
            
            // Mevcut kullanici (sidebar icin)
            var currentUser = db.QueryFirstOrDefault("SELECT Seviye as Level, ToplamXP as XP FROM Kullanicilar WHERE Id = @id", new { id = myId });
            ViewBag.UserLevel = currentUser?.Level ?? 1;
            ViewBag.UserXP = currentUser?.XP ?? 0;
            ViewBag.UserMaxXP = ((currentUser?.Level ?? 1) + 1) * 200;
            
            // Profil kullanicisi
            var user = db.QueryFirstOrDefault(@"SELECT k.Id, k.AdSoyad, k.Email, k.Biyografi as Bio, k.Seviye, k.ToplamXP,
                ISNULL(NULLIF(k.ProfilResmi, ''), '/img/default-user.png') as ProfilResmi,
                (SELECT COUNT(*) FROM Takipciler WHERE TakipEdilenId = k.Id) as TakipciSayisi,
                (SELECT COUNT(*) FROM Takipciler WHERE TakipEdenId = k.Id) as TakipSayisi,
                (SELECT COUNT(*) FROM SosyalGonderiler WHERE KullaniciId = k.Id) as GonderiSayisi,
                (SELECT COUNT(*) FROM SosyalBegeniler WHERE KullaniciId = k.Id) as BegenilenSayisi,
                CASE WHEN EXISTS(SELECT 1 FROM Takipciler WHERE TakipEdenId = @ben AND TakipEdilenId = k.Id) THEN 1 ELSE 0 END as TakipEdiyorum
                FROM Kullanicilar k WHERE k.Id = @id", new { id = profilId, ben = myId });
            
            // ViewBag verileri
            ViewBag.User = user;
            ViewBag.UserName = user?.AdSoyad ?? "Kullanici";
            ViewBag.UserHandle = (user?.Email ?? "user").Replace("@gmail.com", "");
            ViewBag.UserAvatar = user?.ProfilResmi;
            ViewBag.UserBio = user?.Bio ?? "Tasarimci | Kartist";
            ViewBag.UserLevel = user?.Seviye ?? 1;
            ViewBag.UserXP = user?.ToplamXP ?? 0;
            ViewBag.UserMaxXP = ((user?.Seviye ?? 1) + 1) * 200;
            ViewBag.IsOwnProfile = (profilId == myId);
            ViewBag.ProfileUserId = profilId;
            ViewBag.Followers = user?.TakipciSayisi ?? 0;
            ViewBag.Following = user?.TakipSayisi ?? 0;
            ViewBag.DesignCount = user?.GonderiSayisi ?? 0;
            ViewBag.LikedCount = user?.BegenilenSayisi ?? 0;
            ViewBag.SavedCount = 89; // Mock
            ViewBag.BadgeCount = 8; // Mock
            ViewBag.TotalLikes = 8900; // Mock
            ViewBag.IsFollowing = (user?.TakipEdiyorum ?? 0) == 1;
            
            // Kullanici gonderileri
            var posts = db.Query(@"SELECT g.Id, g.Icerik as Title, g.GorselUrl as ImageUrl, 
                g.BegeniSayisi as Likes, g.YorumSayisi as Comments, g.GoruntulemeSayisi as Views
                FROM SosyalGonderiler g 
                WHERE g.KullaniciId = @id
                ORDER BY g.OlusturmaTarihi DESC", new { id = profilId }).ToList();
            
            ViewBag.UserPosts = posts;
            
            // Mock badges
            ViewBag.Badges = new List<dynamic> {
                new { Name = "Yeni" },
                new { Name = "Populer" }
            };
            
            return View("ProfilModern");
        }

        [HttpGet]
        public IActionResult GetProfil(int id, string list = "gonderiler")
        {
            string email = GetEmail();
            using var db = new SqlConnection(_conn);
            int myId = GetUserId(db, email);

            var user = db.QueryFirstOrDefault(@"SELECT k.Id, k.AdSoyad, k.ProfilResmi, k.Biyografi, k.UyelikTipi,
                    k.Seviye, k.ToplamXP, k.Streak, k.KapakResmi, k.ProfilTema, k.SonGorulenTarihi,
                    (SELECT COUNT(*) FROM Takipciler WHERE TakipEdilenId = k.Id) as TakipciSayisi,
                    (SELECT COUNT(*) FROM Takipciler WHERE TakipEdenId = k.Id) as TakipSayisi,
                    (SELECT COUNT(*) FROM SosyalGonderiler WHERE KullaniciId = k.Id) as GonderiSayisi,
                    (SELECT COUNT(*) FROM KullaniciRozetleri WHERE KullaniciId = k.Id) as RozetSayisi,
                    CASE WHEN EXISTS(SELECT 1 FROM Takipciler WHERE TakipEdenId = @ben AND TakipEdilenId = k.Id) THEN 1 ELSE 0 END as TakipEdiyorum
                FROM Kullanicilar k WHERE k.Id = @id", new { id, ben = myId });

            if (user == null) return Json(new { success = false });

            IEnumerable<dynamic> gonderiler;
            if (list == "begeniler") {
                gonderiler = db.Query(@"SELECT g.Id, g.GorselUrl, g.OnceSonraResim, g.KodSinipet, g.AiVibe, g.Icerik, g.BegeniSayisi, g.YorumSayisi, g.OlusturmaTarihi 
                                        FROM SosyalGonderiler g JOIN SosyalBegeniler b ON g.Id = b.GonderiId 
                                        WHERE b.KullaniciId = @id ORDER BY b.Tarih DESC", new { id });
            } else if (list == "kaydedilenler" && myId == id) {
                gonderiler = db.Query(@"SELECT g.Id, g.GorselUrl, g.OnceSonraResim, g.KodSinipet, g.AiVibe, g.Icerik, g.BegeniSayisi, g.YorumSayisi, g.OlusturmaTarihi 
                                        FROM SosyalGonderiler g JOIN Kaydedilenler kay ON g.Id = kay.GonderiId 
                                        WHERE kay.KullaniciId = @id ORDER BY kay.Id DESC", new { id });
            } else {
                gonderiler = db.Query(@"SELECT g.Id, g.GorselUrl, g.OnceSonraResim, g.KodSinipet, g.AiVibe, g.Icerik, g.BegeniSayisi, g.YorumSayisi, g.OlusturmaTarihi
                                        FROM SosyalGonderiler g WHERE g.KullaniciId = @id ORDER BY g.OlusturmaTarihi DESC",
                    new { id });
            }

            // Rozet vitrini
            var rozetler = db.Query(@"SELECT r.Ad, r.Kod, r.Ikon as Emoji, r.XPOdulu 
                                      FROM KullaniciRozetleri kr JOIN Rozetler r ON kr.RozetId = r.Id
                                      WHERE kr.KullaniciId = @id ORDER BY kr.KazanmaTarihi DESC", new { id }).ToList();

            return Json(new { success = true, user, gonderiler = gonderiler.ToList(), rozetler, benimProfilim = (myId == id) });
        }

        // ===== HÄ°KAYELER =====
        [HttpPost]
        public async Task<IActionResult> HikayeOlustur(IFormFile gorsel)
        {
            string email = GetEmail();
            if (email == null) return Json(new { success = false });

            if (gorsel == null || gorsel.Length == 0) return Json(new { success = false, message = "Gorsel gerekli." });
            if (!Kartist.Helpers.FileUploadValidator.TryValidateImage(gorsel, 10 * 1024 * 1024, out var ext, out var err))
                return Json(new { success = false, message = err });

            using var db = new SqlConnection(_conn);
            int userId = GetUserId(db, email);

            var dosyaAdi = $"story_{Guid.NewGuid():N}{ext}";
            var klasor = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads/stories");
            if (!Directory.Exists(klasor)) Directory.CreateDirectory(klasor);
            var yol = Path.Combine(klasor, dosyaAdi);
            await using var stream = new FileStream(yol, FileMode.Create);
            await gorsel.CopyToAsync(stream);

            db.Execute(@"INSERT INTO Hikayeler (KullaniciId, GorselUrl, BitisTarihi) VALUES (@uid, @url, @bitis)",
                new { uid = userId, url = "/uploads/stories/" + dosyaAdi, bitis = DateTime.UtcNow.AddHours(24) });

            return Json(new { success = true });
        }

        [HttpGet]
        public IActionResult GetHikayeler()
        {
            using var db = new SqlConnection(_conn);
            string email = GetEmail() ?? "test@test.com";
            int userId = GetUserId(db, email);
            if (userId == 0) userId = 1;

            // SÃ¼resi dolmuÅŸ hikayeleri sil
            db.Execute("DELETE FROM Hikayeler WHERE BitisTarihi < GETUTCDATE()");

            var hikayeler = db.Query(@"
                SELECT k.Id as KullaniciId, k.AdSoyad, k.ProfilResmi,
                       (SELECT COUNT(*) FROM Hikayeler WHERE KullaniciId = k.Id AND BitisTarihi > GETUTCDATE()) as HikayeSayisi
                FROM Kullanicilar k
                WHERE EXISTS (SELECT 1 FROM Hikayeler WHERE KullaniciId = k.Id AND BitisTarihi > GETUTCDATE())
                ORDER BY CASE WHEN k.Id = @uid THEN 0 ELSE 1 END, k.AdSoyad", new { uid = userId }).ToList();

            return Json(new { success = true, hikayeler, myId = userId });
        }

        [HttpGet]
        public IActionResult GetKullaniciHikayeleri(int kullaniciId)
        {
            using var db = new SqlConnection(_conn);
            var list = db.Query(@"SELECT Id, GorselUrl, OlusturmaTarihi FROM Hikayeler
                                  WHERE KullaniciId = @uid AND BitisTarihi > GETUTCDATE()
                                  ORDER BY OlusturmaTarihi ASC", new { uid = kullaniciId }).ToList();
            return Json(new { success = true, hikayeler = list });
        }

        // ===== MESSAGES =====
        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        public IActionResult Messages()
        {
            // DEBUG: Auth check disabled for testing
            // if (!User.Identity.IsAuthenticated) return RedirectToAction("Giris", "Account");
            
            using var db = new SqlConnection(_conn);
            string email = GetEmail() ?? "test@test.com";
            int userId = GetUserId(db, email);
            if (userId == 0) userId = 1;
            
            // Kullanici bilgileri
            var user = db.QueryFirstOrDefault("SELECT Seviye as Level, ToplamXP as XP FROM Kullanicilar WHERE Id = @id", new { id = userId });
            ViewBag.UserLevel = user?.Level ?? 1;
            ViewBag.UserXP = user?.XP ?? 0;
            ViewBag.UserMaxXP = ((user?.Level ?? 1) + 1) * 200;
            
            ViewBag.CurrentUserId = userId;

            var conversations = db.Query(@"
                SELECT k.Id, k.AdSoyad as Name,
                       SUBSTRING(k.Email, 1, CHARINDEX('@', k.Email) - 1) as Handle,
                       ISNULL(NULLIF(k.ProfilResmi, ''),
                              'https://ui-avatars.com/api/?name=' + REPLACE(k.AdSoyad, ' ', '+') + '&background=random&size=128') as Avatar,
                       (SELECT TOP 1 Mesaj FROM DirektMesajlar
                        WHERE (GonderenId = @uid AND AliciId = k.Id) OR (GonderenId = k.Id AND AliciId = @uid)
                        ORDER BY Tarih DESC) as LastMessage,
                       (SELECT TOP 1
                            CASE
                                WHEN CAST(Tarih AS DATE) = CAST(GETDATE() AS DATE) THEN FORMAT(Tarih, 'HH:mm')
                                WHEN CAST(Tarih AS DATE) = CAST(DATEADD(DAY,-1,GETDATE()) AS DATE) THEN 'DÃ¼n'
                                ELSE FORMAT(Tarih, 'dd.MM')
                            END
                        FROM DirektMesajlar
                        WHERE (GonderenId = @uid AND AliciId = k.Id) OR (GonderenId = k.Id AND AliciId = @uid)
                        ORDER BY Tarih DESC) as LastMessageAt,
                       (SELECT COUNT(*) FROM DirektMesajlar WHERE GonderenId = k.Id AND AliciId = @uid AND OkunduMu = 0) as UnreadCount
                FROM Kullanicilar k
                WHERE k.Id != @uid
                ORDER BY 
                    CASE WHEN EXISTS (SELECT 1 FROM DirektMesajlar WHERE (GonderenId = @uid AND AliciId = k.Id) OR (GonderenId = k.Id AND AliciId = @uid)) THEN 0 ELSE 1 END ASC,
                    (SELECT TOP 1 Tarih FROM DirektMesajlar
                     WHERE (GonderenId = @uid AND AliciId = k.Id) OR (GonderenId = k.Id AND AliciId = @uid)
                     ORDER BY Tarih DESC) DESC, k.AdSoyad ASC", new { uid = userId }).ToList();

            if (conversations.Any())
                ViewBag.Conversations = conversations;

            return View();
        }

        // ===== DÄ°REKT MESAJ (ESKI) =====
        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        public IActionResult Mesajlar()
        {
            // DEBUG: Auth check disabled for testing
            // if (!User.Identity.IsAuthenticated) return RedirectToAction("Giris", "Account");
            return RedirectToAction("Messages");
        }

        [HttpGet]
        public IActionResult GetSohbetler()
        {
            using var db = new SqlConnection(_conn);
            string email = GetEmail() ?? "test@test.com";
            int userId = GetUserId(db, email);
            if (userId == 0) userId = 1;

            var sohbetler = db.Query(@"
                SELECT k.Id, k.AdSoyad, k.ProfilResmi,
                    SUBSTRING(k.Email, 1, CHARINDEX('@', k.Email) - 1) as Handle,
                    (SELECT TOP 1 Mesaj FROM DirektMesajlar
                     WHERE (GonderenId = @uid AND AliciId = k.Id) OR (GonderenId = k.Id AND AliciId = @uid)
                     ORDER BY Tarih DESC) as SonMesaj,
                    (SELECT TOP 1 Tarih FROM DirektMesajlar
                     WHERE (GonderenId = @uid AND AliciId = k.Id) OR (GonderenId = k.Id AND AliciId = @uid)
                     ORDER BY Tarih DESC) as SonTarih,
                    (SELECT COUNT(*) FROM DirektMesajlar WHERE GonderenId = k.Id AND AliciId = @uid AND OkunduMu = 0) as OkunmamisSayi
                FROM Kullanicilar k
                WHERE k.Id != @uid
                ORDER BY 
                    CASE WHEN EXISTS (SELECT 1 FROM DirektMesajlar WHERE (GonderenId = @uid AND AliciId = k.Id) OR (GonderenId = k.Id AND AliciId = @uid)) THEN 0 ELSE 1 END ASC,
                    SonTarih DESC, k.AdSoyad ASC", new { uid = userId }).ToList();

            return Json(new { success = true, sohbetler });
        }

        [HttpGet]
        public IActionResult GetMesajlar(int hedefId)
        {
            using var db = new SqlConnection(_conn);
            string email = GetEmail() ?? "test@test.com";
            int userId = GetUserId(db, email);
            if (userId == 0) userId = 1;
            if (hedefId == userId) return Json(new { success = false, message = "Kendine mesaj gönderemezsin." });

            var hedef = db.QueryFirstOrDefault("SELECT Id, AdSoyad, ProfilResmi, Biyografi FROM Kullanicilar WHERE Id = @id", new { id = hedefId });
            if (hedef == null) return Json(new { success = false, message = "Kullanıcı bulunamadı." });

            db.Execute("UPDATE Kullanicilar SET SonGorulenTarihi = GETUTCDATE() WHERE Id = @uid", new { uid = userId });

            // Okundu olarak işaretle
            db.Execute("UPDATE DirektMesajlar SET OkunduMu = 1 WHERE GonderenId = @o AND AliciId = @ben AND OkunduMu = 0",
                new { o = hedefId, ben = userId });

            var mesajlar = db.Query(@"SELECT Id, GonderenId, AliciId, Mesaj, GorselUrl, Tip, OkunduMu, Tarih
                                      FROM DirektMesajlar
                                      WHERE (GonderenId = @ben AND AliciId = @o) OR (GonderenId = @o AND AliciId = @ben)
                                      ORDER BY Tarih ASC",
                new { ben = userId, o = hedefId }).ToList();

            return Json(new { success = true, mesajlar, hedef, myId = userId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MesajGonder(int aliciId, string mesaj, IFormFile gorsel)
        {
            using var db = new SqlConnection(_conn);
            string email = GetEmail() ?? "test@test.com";
            int userId = GetUserId(db, email);
            if (userId == 0) userId = 1;
            if (userId == aliciId) return Json(new { success = false, message = "Kendine mesaj gönderemezsin." });

            var aliciVarMi = db.ExecuteScalar<int>("SELECT COUNT(*) FROM Kullanicilar WHERE Id = @id", new { id = aliciId });
            if (aliciVarMi == 0) return Json(new { success = false, message = "Alıcı bulunamadı." });

            mesaj = Helpers.InputValidator.SanitizeHtml(mesaj ?? "").Trim();
            if (mesaj.Length > 2000) mesaj = mesaj[..2000];

            string gorselUrl = null;
            if (gorsel != null && gorsel.Length > 0)
            {
                if (!Kartist.Helpers.FileUploadValidator.TryValidateImage(gorsel, 10 * 1024 * 1024, out var ext, out var err))
                    return Json(new { success = false, message = err });

                var klasor = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "messages");
                Directory.CreateDirectory(klasor);
                var dosyaAdi = $"dm_{userId}_{Guid.NewGuid():N}{ext}";
                var yol = Path.Combine(klasor, dosyaAdi);
                await using var stream = new FileStream(yol, FileMode.Create);
                await gorsel.CopyToAsync(stream);
                gorselUrl = "/uploads/messages/" + dosyaAdi;
            }

            if (string.IsNullOrWhiteSpace(mesaj) && string.IsNullOrWhiteSpace(gorselUrl))
                return Json(new { success = false, message = "Mesaj veya görsel eklemelisin." });

            var mesajId = db.ExecuteScalar<int>(@"
                INSERT INTO DirektMesajlar (GonderenId, AliciId, Mesaj, GorselUrl, Tip)
                OUTPUT INSERTED.Id
                VALUES (@ben, @o, @msg, @gorsel, @tip)",
                new { ben = userId, o = aliciId, msg = string.IsNullOrWhiteSpace(mesaj) ? "Görsel gönderdi" : mesaj, gorsel = gorselUrl, tip = gorselUrl == null ? "Normal" : "Gorsel" });

            var ad = db.ExecuteScalar<string>("SELECT AdSoyad FROM Kullanicilar WHERE Id = @id", new { id = userId });
            db.Execute(@"INSERT INTO Bildirimler (KullaniciId, Tip, Mesaj, GonderenId)
                         VALUES (@kid, 'mesaj', @msg, @sid)",
                new { kid = aliciId, msg = $"{ad} sana mesaj gönderdi", sid = userId });

            var yeniMesaj = db.QueryFirstOrDefault(@"SELECT Id, GonderenId, AliciId, Mesaj, GorselUrl, Tip, OkunduMu, Tarih
                                                     FROM DirektMesajlar WHERE Id = @id", new { id = mesajId });

            KazanXP(db, userId, 2, "mesaj", "Direkt mesaj gönderdi");
            return Json(new { success = true, mesaj = yeniMesaj });
        }
        [HttpPost]
        public IActionResult CollabGonder(int hedefId, int gonderiId)
        {
            string email = GetEmail();
            if (email == null) return Json(new { success = false });

            using var db = new SqlConnection(_conn);
            int userId = GetUserId(db, email);

            var varMi = db.ExecuteScalar<int>("SELECT COUNT(*) FROM DirektMesajlar WHERE GonderenId = @ben AND AliciId = @o AND Tip = 'Collab' AND BaglantiliId = @gid",
                new { ben = userId, o = hedefId, gid = gonderiId });
            
            if (varMi > 0) return Json(new { success = false, message = "Zaten iÅŸ birliÄŸi talebi gÃ¶nderilmiÅŸ." });

            string onYazi = "Selam! GÃ¶nderinle (TasarÄ±mÄ±nla) ilgili aklÄ±mda ÅŸahane bir Collab (Ä°ÅŸ BirliÄŸi) fikri var. Birlikte Ã§alÄ±ÅŸmaya ne dersin?";
            
            db.Execute(@"INSERT INTO DirektMesajlar (GonderenId, AliciId, Mesaj, Tip, BaglantiliId) 
                         VALUES (@ben, @o, @msg, 'Collab', @gid)",
                new { ben = userId, o = hedefId, msg = onYazi, gid = gonderiId });

            var ad = db.ExecuteScalar<string>("SELECT AdSoyad FROM Kullanicilar WHERE Id = @id", new { id = userId });
            db.Execute(@"INSERT INTO Bildirimler (KullaniciId, Tip, Mesaj, BaglantiliId, GonderenId)
                         VALUES (@kid, 'collab', @msg, @gid, @sid)",
                new { kid = hedefId, msg = $"{ad} sana bir Ä°ÅŸ BirliÄŸi teklifi yolladÄ± âš¡", gid = gonderiId, sid = userId });

            return Json(new { success = true });
        }

        // ===== BÄ°LDÄ°RÄ°MLER =====
        [HttpGet]
        public IActionResult GetBildirimler()
        {
            string email = GetEmail();
            if (email == null) return Json(new { success = false });

            try
            {
                using var db = new SqlConnection(_conn);
                int userId = GetUserId(db, email);

                var bildirimler = db.Query(@"SELECT b.Id, b.Tip, b.Mesaj, b.BaglantiliId, b.OkunduMu, b.Tarih,
                                                    k.AdSoyad as GonderenAd, k.ProfilResmi as GonderenResim
                                             FROM Bildirimler b LEFT JOIN Kullanicilar k ON b.GonderenId = k.Id
                                             WHERE b.KullaniciId = @uid ORDER BY b.Tarih DESC
                                             OFFSET 0 ROWS FETCH NEXT 30 ROWS ONLY", new { uid = userId }).ToList();

                int okunmamis = db.ExecuteScalar<int>("SELECT COUNT(*) FROM Bildirimler WHERE KullaniciId = @uid AND OkunduMu = 0", new { uid = userId });

                return Json(new { success = true, bildirimler, okunmamis });
            }
            catch
            {
                return Json(new { success = true, bildirimler = new List<object>(), okunmamis = 0 });
            }
        }

        [HttpPost]
        public IActionResult BildirimleriOku()
        {
            string email = GetEmail();
            if (email == null) return Json(new { success = false });

            try
            {
                using var db = new SqlConnection(_conn);
                int userId = GetUserId(db, email);
                db.Execute("UPDATE Bildirimler SET OkunduMu = 1 WHERE KullaniciId = @uid AND OkunduMu = 0", new { uid = userId });
            }
            catch { }
            return Json(new { success = true });
        }

        // ===== SWIPE =====
        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        public IActionResult Swipe()
        {
            // DEBUG: Auth check disabled for testing
            // if (!User.Identity.IsAuthenticated) return RedirectToAction("Giris", "Account");
            
            using var db = new SqlConnection(_conn);
            string email = GetEmail() ?? "test@test.com";
            int userId = GetUserId(db, email);
            if (userId == 0) userId = 1;
            
            // Kullanici bilgileri
            var user = db.QueryFirstOrDefault("SELECT Seviye as Level, ToplamXP as XP FROM Kullanicilar WHERE Id = @id", new { id = userId });
            ViewBag.UserLevel = user?.Level ?? 1;
            ViewBag.UserXP = user?.XP ?? 0;
            ViewBag.UserMaxXP = ((user?.Level ?? 1) + 1) * 200;
            
            return View();
        }

        // ===== KEÅFET =====
        public IActionResult Kesf()
        {
            if (!User.Identity.IsAuthenticated) return RedirectToAction("Giris", "Account");
            return RedirectToAction("Feed");
        }

        [HttpGet]
        public IActionResult GetKesf(string hashtag = null)
        {
            using var db = new SqlConnection(_conn);
            string email = GetEmail();
            int userId = GetUserId(db, email);

            if (!string.IsNullOrEmpty(hashtag))
            {
                var posts = db.Query(@"SELECT g.Id, g.Icerik, g.GorselUrl, g.BegeniSayisi, g.YorumSayisi, g.OlusturmaTarihi,
                                              k.Id as KullaniciId, k.AdSoyad, k.ProfilResmi,
                                              CASE WHEN b.Id IS NOT NULL THEN 1 ELSE 0 END as Begenildi
                                       FROM SosyalGonderiler g
                                       JOIN Kullanicilar k ON g.KullaniciId = k.Id
                                       JOIN Hashtagler h ON h.GonderiId = g.Id
                                       LEFT JOIN SosyalBegeniler b ON b.GonderiId = g.Id AND b.KullaniciId = @uid
                                       WHERE h.Etiket = @tag
                                       ORDER BY g.OlusturmaTarihi DESC", new { uid = userId, tag = hashtag.ToLowerInvariant() }).ToList();
                return Json(new { success = true, posts });
            }

            var trending = db.Query(@"SELECT TOP 12 g.Id, g.GorselUrl, g.BegeniSayisi, g.YorumSayisi
                                      FROM SosyalGonderiler g WHERE g.GorselUrl IS NOT NULL
                                      ORDER BY g.BegeniSayisi DESC, g.OlusturmaTarihi DESC").ToList();

            var tags = db.Query(@"SELECT TOP 20 Etiket, COUNT(*) as Sayi FROM Hashtagler
                                  GROUP BY Etiket ORDER BY COUNT(*) DESC").ToList();

            var oneriler = db.Query(@"SELECT TOP 10 k.Id, k.AdSoyad, k.ProfilResmi, k.Biyografi,
                                      (SELECT COUNT(*) FROM Takipciler WHERE TakipEdilenId = k.Id) as TakipciSayisi
                                      FROM Kullanicilar k
                                      WHERE k.Id != @uid AND k.Id NOT IN (SELECT TakipEdilenId FROM Takipciler WHERE TakipEdenId = @uid)
                                      ORDER BY TakipciSayisi DESC", new { uid = userId }).ToList();

            return Json(new { success = true, trending, tags, oneriler });
        }

        // ===== KULLANICI ARA =====
        [HttpGet]
        public IActionResult Ara(string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return Json(new { success = true, sonuclar = new List<object>() });

            using var db = new SqlConnection(_conn);
            var sonuclar = db.Query(@"SELECT TOP 15 Id, AdSoyad, ProfilResmi, Biyografi FROM Kullanicilar
                                      WHERE AdSoyad LIKE @ara ORDER BY AdSoyad",
                new { ara = $"%{q}%" }).ToList();
            return Json(new { success = true, sonuclar });
        }

        // ===== PROFIL GÃœNCELLE =====
        [HttpPost]
        public async Task<IActionResult> ProfilGuncelle(string biyografi, IFormFile profilResmi)
        {
            string email = GetEmail();
            if (email == null) return Json(new { success = false });

            using var db = new SqlConnection(_conn);
            int userId = GetUserId(db, email);

            if (!string.IsNullOrWhiteSpace(biyografi))
            {
                biyografi = Helpers.InputValidator.SanitizeHtml(biyografi);
                if (biyografi.Length > 500) biyografi = biyografi[..500];
                db.Execute("UPDATE Kullanicilar SET Biyografi = @bio WHERE Id = @uid", new { bio = biyografi, uid = userId });
            }

            if (profilResmi != null && profilResmi.Length > 0 &&
                Kartist.Helpers.FileUploadValidator.TryValidateImage(profilResmi, 5 * 1024 * 1024, out var avatarExt, out _))
            {
                var dosyaAdi = $"avatar_{userId}_{Guid.NewGuid():N}{avatarExt}";
                var klasor = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads/avatars");
                if (!Directory.Exists(klasor)) Directory.CreateDirectory(klasor);
                var yol = Path.Combine(klasor, dosyaAdi);
                await using var stream = new FileStream(yol, FileMode.Create);
                await profilResmi.CopyToAsync(stream);
                db.Execute("UPDATE Kullanicilar SET ProfilResmi = @url WHERE Id = @uid",
                    new { url = "/uploads/avatars/" + dosyaAdi, uid = userId });
            }

            return Json(new { success = true });
        }

        // =====================================================
        // ===== SPRINT 1: GAMIFICATION =====
        // =====================================================

        private void KazanXP(SqlConnection db, int userId, int miktar, string kaynak, string aciklama = null)
        {
            try
            {
                db.Execute("INSERT INTO KullaniciXP (KullaniciId, Miktar, Kaynak, Aciklama) VALUES (@uid, @m, @k, @a)",
                    new { uid = userId, m = miktar, k = kaynak, a = aciklama });
                db.Execute("UPDATE Kullanicilar SET ToplamXP = ToplamXP + @m WHERE Id = @uid", new { m = miktar, uid = userId });

                // Seviye hesapla (her 200 XP = 1 seviye)
                var toplamXP = db.ExecuteScalar<int>("SELECT ToplamXP FROM Kullanicilar WHERE Id = @uid", new { uid = userId });
                int yeniSeviye = Math.Max(1, toplamXP / 200 + 1);
                db.Execute("UPDATE Kullanicilar SET Seviye = @s WHERE Id = @uid", new { s = yeniSeviye, uid = userId });
            }
            catch { }
        }

        private void RozetKontrol(SqlConnection db, int userId)
        {
            try
            {
                var gonderiSayisi = db.ExecuteScalar<int>("SELECT COUNT(*) FROM SosyalGonderiler WHERE KullaniciId = @uid", new { uid = userId });
                var begeniSayisi = db.ExecuteScalar<int>("SELECT ISNULL(SUM(BegeniSayisi),0) FROM SosyalGonderiler WHERE KullaniciId = @uid", new { uid = userId });
                var takipciSayisi = db.ExecuteScalar<int>("SELECT COUNT(*) FROM Takipciler WHERE TakipEdilenId = @uid", new { uid = userId });
                var yorumSayisi = db.ExecuteScalar<int>("SELECT COUNT(*) FROM SosyalYorumlar WHERE KullaniciId = @uid", new { uid = userId });
                var streak = db.ExecuteScalar<int>("SELECT ISNULL(Streak,0) FROM Kullanicilar WHERE Id = @uid", new { uid = userId });
                var hikayeSayisi = db.ExecuteScalar<int>("SELECT COUNT(*) FROM Hikayeler WHERE KullaniciId = @uid", new { uid = userId });

                var kontroller = new Dictionary<string, bool>
                {
                    { "ilk_gonderi", gonderiSayisi >= 1 }, { "10_gonderi", gonderiSayisi >= 10 }, { "50_gonderi", gonderiSayisi >= 50 },
                    { "ilk_begeni", begeniSayisi >= 1 }, { "100_begeni", begeniSayisi >= 100 }, { "500_begeni", begeniSayisi >= 500 },
                    { "ilk_takipci", takipciSayisi >= 1 }, { "50_takipci", takipciSayisi >= 50 }, { "100_takipci", takipciSayisi >= 100 },
                    { "ilk_yorum", yorumSayisi >= 1 }, { "7_streak", streak >= 7 }, { "30_streak", streak >= 30 },
                    { "ilk_hikaye", hikayeSayisi >= 1 }
                };

                // Gece kuÅŸu kontrolÃ¼
                var saat = DateTime.UtcNow.Hour;
                if (saat >= 23 || saat < 2) kontroller["gece_kusu"] = true;

                foreach (var kv in kontroller)
                {
                    if (!kv.Value) continue;
                    var rozet = db.QueryFirstOrDefault("SELECT Id, XPOdulu FROM Rozetler WHERE Kod = @kod", new { kod = kv.Key });
                    if (rozet == null) continue;
                    var varMi = db.ExecuteScalar<int>("SELECT COUNT(*) FROM KullaniciRozetleri WHERE KullaniciId = @uid AND RozetId = @rid",
                        new { uid = userId, rid = (int)rozet.Id });
                    if (varMi == 0)
                    {
                        db.Execute("INSERT INTO KullaniciRozetleri (KullaniciId, RozetId) VALUES (@uid, @rid)",
                            new { uid = userId, rid = (int)rozet.Id });
                        if ((int)rozet.XPOdulu > 0) KazanXP(db, userId, (int)rozet.XPOdulu, "rozet", $"Rozet: {kv.Key}");
                    }
                }
            }
            catch { }
        }

        private void GunlukGorevIlerle(SqlConnection db, int userId, string gorevTipi)
        {
            try
            {
                var bugun = DateTime.UtcNow.Date;
                // GÃ¶revleri oluÅŸtur (yoksa)
                var gorevVarMi = db.ExecuteScalar<int>("SELECT COUNT(*) FROM GunlukGorevler WHERE KullaniciId = @uid AND Tarih = @t",
                    new { uid = userId, t = bugun });
                if (gorevVarMi == 0)
                {
                    db.Execute(@"INSERT INTO GunlukGorevler (KullaniciId, Tarih, GorevTipi, HedefSayi, XPOdulu) VALUES
                        (@uid, @t, 'begeni', 5, 30), (@uid, @t, 'yorum', 3, 40), (@uid, @t, 'gonderi', 1, 50),
                        (@uid, @t, 'hikaye', 1, 25), (@uid, @t, 'takip', 2, 20)", new { uid = userId, t = bugun });
                }

                db.Execute(@"UPDATE GunlukGorevler SET MevcutSayi = MevcutSayi + 1 
                    WHERE KullaniciId = @uid AND Tarih = @t AND GorevTipi = @gt AND Tamamlandi = 0",
                    new { uid = userId, t = bugun, gt = gorevTipi });

                // TamamlananlarÄ± iÅŸaretle ve XP ver
                var tamamlanan = db.Query(@"SELECT Id, XPOdulu FROM GunlukGorevler 
                    WHERE KullaniciId = @uid AND Tarih = @t AND Tamamlandi = 0 AND MevcutSayi >= HedefSayi",
                    new { uid = userId, t = bugun }).ToList();
                foreach (var g in tamamlanan)
                {
                    db.Execute("UPDATE GunlukGorevler SET Tamamlandi = 1 WHERE Id = @id", new { id = (int)g.Id });
                    KazanXP(db, userId, (int)g.XPOdulu, "gorev", "GÃ¼nlÃ¼k gÃ¶rev tamamlandÄ±");
                }
            }
            catch { }
        }

        [HttpGet]
        public IActionResult StreakKaydet()
        {
            string email = GetEmail();
            if (email == null) return Json(new { success = false });

            try
            {
                using var db = new SqlConnection(_conn);
                int userId = GetUserId(db, email);
                var bugun = DateTime.UtcNow.Date;

                // Son gÃ¶rÃ¼leni gÃ¼ncelle
                db.Execute("UPDATE Kullanicilar SET SonGorulenTarihi = GETUTCDATE() WHERE Id = @uid", new { uid = userId });

                // BugÃ¼n zaten giriÅŸ yaptÄ± mÄ±?
                var varMi = db.ExecuteScalar<int>("SELECT COUNT(*) FROM GirisKayitlari WHERE KullaniciId = @uid AND Tarih = @t",
                    new { uid = userId, t = bugun });
                if (varMi > 0)
                {
                    var streak = db.ExecuteScalar<int>("SELECT ISNULL(Streak,0) FROM Kullanicilar WHERE Id = @uid", new { uid = userId });
                    return Json(new { success = true, streak, alreadyLogged = true });
                }

                db.Execute("INSERT INTO GirisKayitlari (KullaniciId, Tarih) VALUES (@uid, @t)", new { uid = userId, t = bugun });

                // Streak hesapla
                var dun = bugun.AddDays(-1);
                var dunVarMi = db.ExecuteScalar<int>("SELECT COUNT(*) FROM GirisKayitlari WHERE KullaniciId = @uid AND Tarih = @t",
                    new { uid = userId, t = dun });
                if (dunVarMi > 0)
                    db.Execute("UPDATE Kullanicilar SET Streak = Streak + 1 WHERE Id = @uid", new { uid = userId });
                else
                    db.Execute("UPDATE Kullanicilar SET Streak = 1 WHERE Id = @uid", new { uid = userId });

                var yeniStreak = db.ExecuteScalar<int>("SELECT Streak FROM Kullanicilar WHERE Id = @uid", new { uid = userId });
                KazanXP(db, userId, 10, "giris", "GÃ¼nlÃ¼k giriÅŸ");
                RozetKontrol(db, userId);

                return Json(new { success = true, streak = yeniStreak, alreadyLogged = false, xp = 10 });
            }
            catch { return Json(new { success = true, streak = 0 }); }
        }

        [HttpGet]
        public IActionResult GetGunlukGorevler()
        {
            string email = GetEmail();
            if (email == null) return Json(new { success = false });
            try
            {
                using var db = new SqlConnection(_conn);
                int userId = GetUserId(db, email);
                var bugun = DateTime.UtcNow.Date;

                // GÃ¶revleri oluÅŸtur (yoksa)
                var gorevVarMi = db.ExecuteScalar<int>("SELECT COUNT(*) FROM GunlukGorevler WHERE KullaniciId = @uid AND Tarih = @t",
                    new { uid = userId, t = bugun });
                if (gorevVarMi == 0)
                {
                    db.Execute(@"INSERT INTO GunlukGorevler (KullaniciId, Tarih, GorevTipi, HedefSayi, XPOdulu) VALUES
                        (@uid, @t, 'begeni', 5, 30), (@uid, @t, 'yorum', 3, 40), (@uid, @t, 'gonderi', 1, 50),
                        (@uid, @t, 'hikaye', 1, 25), (@uid, @t, 'takip', 2, 20)", new { uid = userId, t = bugun });
                }

                var gorevler = db.Query("SELECT * FROM GunlukGorevler WHERE KullaniciId = @uid AND Tarih = @t",
                    new { uid = userId, t = bugun }).ToList();
                return Json(new { success = true, gorevler });
            }
            catch { return Json(new { success = true, gorevler = new List<object>() }); }
        }

        [HttpGet]
        public IActionResult GetMyStats()
        {
            string email = GetEmail();
            if (email == null) return Json(new { success = false });
            try
            {
                using var db = new SqlConnection(_conn);
                int userId = GetUserId(db, email);
                var stats = db.QueryFirstOrDefault(@"SELECT k.Seviye, k.ToplamXP, k.Streak, k.ProfilResmi, k.AdSoyad,
                    (SELECT COUNT(*) FROM KullaniciRozetleri WHERE KullaniciId = k.Id) as RozetSayisi
                    FROM Kullanicilar k WHERE k.Id = @uid", new { uid = userId });
                var rozetler = db.Query(@"SELECT r.Kod, r.Ad, r.Ikon, r.Renk, r.Aciklama, kr.KazanmaTarihi
                    FROM KullaniciRozetleri kr JOIN Rozetler r ON kr.RozetId = r.Id
                    WHERE kr.KullaniciId = @uid ORDER BY kr.KazanmaTarihi DESC", new { uid = userId }).ToList();
                return Json(new { success = true, stats, rozetler });
            }
            catch { return Json(new { success = true, stats = new { Seviye = 1, ToplamXP = 0, Streak = 0, RozetSayisi = 0 }, rozetler = new List<object>() }); }
        }

        // ===== LÄ°DERLÄ°K =====
        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        public IActionResult Liderlik()
        {
            // DEBUG: Auth check disabled for testing
            // if (!User.Identity.IsAuthenticated) return RedirectToAction("Giris", "Account");
            return View();
        }

        [HttpGet]
        public IActionResult GetLiderlik(string donem = "hafta")
        {
            try
            {
                using var db = new SqlConnection(_conn);
                string email = GetEmail();
                int myId = GetUserId(db, email);

                string dateFilter = donem == "ay"
                    ? "AND xp.Tarih >= DATEADD(MONTH, -1, GETUTCDATE())"
                    : "AND xp.Tarih >= DATEADD(WEEK, -1, GETUTCDATE())";

                var liderler = db.Query($@"SELECT TOP 20 k.Id, k.AdSoyad, k.ProfilResmi, k.Seviye, k.Streak,
                    ISNULL(SUM(xp.Miktar), 0) as DonemXP
                    FROM Kullanicilar k LEFT JOIN KullaniciXP xp ON k.Id = xp.KullaniciId {dateFilter}
                    GROUP BY k.Id, k.AdSoyad, k.ProfilResmi, k.Seviye, k.Streak
                    ORDER BY DonemXP DESC").ToList();

                return Json(new { success = true, liderler, myId });
            }
            catch { return Json(new { success = true, liderler = new List<object>(), myId = 0 }); }
        }

        // =====================================================
        // ===== SPRINT 2: ZENGÄ°N ETKÄ°LEÅÄ°M =====
        // =====================================================

        // ===== EMOJI REACTIONS =====
        [HttpPost]
        public IActionResult Reaksiyon(int gonderiId, string emoji)
        {
            string email = GetEmail();
            if (email == null) return Json(new { success = false });
            var izinliEmojiler = new[] { "â¤ï¸", "ğŸ”¥", "ğŸ˜‚", "ğŸ˜¢", "ğŸ˜¡", "ğŸ‘", "ğŸ¤©" };
            if (!izinliEmojiler.Contains(emoji)) return Json(new { success = false });

            using var db = new SqlConnection(_conn);
            int userId = GetUserId(db, email);

            var mevcut = db.QueryFirstOrDefault("SELECT Id, Reaksiyon FROM GonderiReaksiyonlar WHERE GonderiId = @gid AND KullaniciId = @uid",
                new { gid = gonderiId, uid = userId });

            if (mevcut != null)
            {
                if ((string)mevcut.Reaksiyon == emoji)
                {
                    db.Execute("DELETE FROM GonderiReaksiyonlar WHERE Id = @id", new { id = (int)mevcut.Id });
                    db.Execute("UPDATE SosyalGonderiler SET BegeniSayisi = BegeniSayisi - 1 WHERE Id = @id AND BegeniSayisi > 0", new { id = gonderiId });
                    return Json(new { success = true, removed = true });
                }
                db.Execute("UPDATE GonderiReaksiyonlar SET Reaksiyon = @e WHERE Id = @id", new { e = emoji, id = (int)mevcut.Id });
                return Json(new { success = true, changed = true, emoji });
            }

            db.Execute("INSERT INTO GonderiReaksiyonlar (GonderiId, KullaniciId, Reaksiyon) VALUES (@gid, @uid, @e)",
                new { gid = gonderiId, uid = userId, e = emoji });
            db.Execute("UPDATE SosyalGonderiler SET BegeniSayisi = BegeniSayisi + 1 WHERE Id = @id", new { id = gonderiId });

            // eski SosyalBegeniler'e de ekle (uyumluluk)
            var eskiVarMi = db.ExecuteScalar<int>("SELECT COUNT(*) FROM SosyalBegeniler WHERE GonderiId = @gid AND KullaniciId = @uid",
                new { gid = gonderiId, uid = userId });
            if (eskiVarMi == 0)
                db.Execute("INSERT INTO SosyalBegeniler (GonderiId, KullaniciId) VALUES (@gid, @uid)", new { gid = gonderiId, uid = userId });

            KazanXP(db, userId, 3, "reaksiyon");
            GunlukGorevIlerle(db, userId, "begeni");
            RozetKontrol(db, userId);

            return Json(new { success = true, added = true, emoji });
        }

        [HttpGet]
        public IActionResult GetReaksiyonlar(int gonderiId)
        {
            using var db = new SqlConnection(_conn);
            var reaksiyonlar = db.Query(@"SELECT Reaksiyon, COUNT(*) as Sayi FROM GonderiReaksiyonlar 
                WHERE GonderiId = @gid GROUP BY Reaksiyon ORDER BY Sayi DESC", new { gid = gonderiId }).ToList();
            string email = GetEmail();
            int userId = GetUserId(db, email);
            var benim = db.QueryFirstOrDefault<string>("SELECT Reaksiyon FROM GonderiReaksiyonlar WHERE GonderiId = @gid AND KullaniciId = @uid",
                new { gid = gonderiId, uid = userId });
            return Json(new { success = true, reaksiyonlar, benim });
        }

        // ===== BOOKMARK =====
        [HttpPost]
        public IActionResult Kaydet(int gonderiId)
        {
            string email = GetEmail();
            if (email == null) return Json(new { success = false });

            using var db = new SqlConnection(_conn);
            int userId = GetUserId(db, email);
            var varMi = db.ExecuteScalar<int>("SELECT COUNT(*) FROM Kaydedilenler WHERE KullaniciId = @uid AND GonderiId = @gid",
                new { uid = userId, gid = gonderiId });

            if (varMi > 0)
            {
                db.Execute("DELETE FROM Kaydedilenler WHERE KullaniciId = @uid AND GonderiId = @gid", new { uid = userId, gid = gonderiId });
                return Json(new { success = true, saved = false });
            }
            db.Execute("INSERT INTO Kaydedilenler (KullaniciId, GonderiId) VALUES (@uid, @gid)", new { uid = userId, gid = gonderiId });
            return Json(new { success = true, saved = true });
        }

        [HttpGet]
        public IActionResult GetKaydedilenler()
        {
            string email = GetEmail();
            if (email == null) return Json(new { success = false });
            using var db = new SqlConnection(_conn);
            int userId = GetUserId(db, email);
            var posts = db.Query(@"SELECT g.Id, g.Icerik, g.GorselUrl, g.BegeniSayisi, g.YorumSayisi, g.OlusturmaTarihi, g.GoruntulemeSayisi,
                k.Id as KullaniciId, k.AdSoyad, k.ProfilResmi, 1 as Kaydedildi,
                CASE WHEN b.Id IS NOT NULL THEN 1 ELSE 0 END as Begenildi
                FROM Kaydedilenler kd JOIN SosyalGonderiler g ON kd.GonderiId = g.Id
                JOIN Kullanicilar k ON g.KullaniciId = k.Id
                LEFT JOIN SosyalBegeniler b ON b.GonderiId = g.Id AND b.KullaniciId = @uid
                WHERE kd.KullaniciId = @uid ORDER BY kd.Tarih DESC", new { uid = userId }).ToList();
            return Json(new { success = true, posts });
        }

        // ===== REPOST =====
        [HttpPost]
        public IActionResult Repost(int gonderiId, string yorum = null)
        {
            string email = GetEmail();
            if (email == null) return Json(new { success = false });

            using var db = new SqlConnection(_conn);
            int userId = GetUserId(db, email);
            db.Execute("INSERT INTO Repostlar (KullaniciId, OrijinalGonderiId, Yorum) VALUES (@uid, @gid, @y)",
                new { uid = userId, gid = gonderiId, y = yorum });
            db.Execute("UPDATE SosyalGonderiler SET RepostSayisi = RepostSayisi + 1 WHERE Id = @gid", new { gid = gonderiId });

            var gonderiSahibiId = db.ExecuteScalar<int>("SELECT KullaniciId FROM SosyalGonderiler WHERE Id = @id", new { id = gonderiId });
            if (gonderiSahibiId != userId)
            {
                var ad = db.ExecuteScalar<string>("SELECT AdSoyad FROM Kullanicilar WHERE Id = @id", new { id = userId });
                db.Execute(@"INSERT INTO Bildirimler (KullaniciId, Tip, Mesaj, BaglantiliId, GonderenId) VALUES (@kid, 'repost', @msg, @gid, @sid)",
                    new { kid = gonderiSahibiId, msg = $"{ad} gÃ¶nderini paylaÅŸtÄ± ğŸ”", gid = gonderiId, sid = userId });
            }

            KazanXP(db, userId, 10, "repost");
            return Json(new { success = true });
        }

        // ===== ANKET =====
        [HttpPost]
        public IActionResult AnketOlustur(string soru, string secenek1, string secenek2, string secenek3 = null, string secenek4 = null)
        {
            string email = GetEmail();
            if (email == null) return Json(new { success = false });

            using var db = new SqlConnection(_conn);
            int userId = GetUserId(db, email);

            int gonderiId = db.ExecuteScalar<int>(
                @"INSERT INTO SosyalGonderiler (KullaniciId, Icerik, AnketMi) OUTPUT INSERTED.Id VALUES (@uid, @s, 1)",
                new { uid = userId, s = Helpers.InputValidator.SanitizeHtml(soru) });

            var secenekler = new[] { secenek1, secenek2, secenek3, secenek4 }.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            for (int i = 0; i < secenekler.Count; i++)
                db.Execute("INSERT INTO AnketSecenekler (GonderiId, Metin, Sira) VALUES (@gid, @m, @s)",
                    new { gid = gonderiId, m = Helpers.InputValidator.SanitizeHtml(secenekler[i]), s = i });

            KazanXP(db, userId, 40, "anket", "Anket oluÅŸturuldu");
            GunlukGorevIlerle(db, userId, "gonderi");
            return Json(new { success = true, id = gonderiId });
        }

        [HttpPost]
        public IActionResult AnketOyVer(int secenekId)
        {
            string email = GetEmail();
            if (email == null) return Json(new { success = false });

            using var db = new SqlConnection(_conn);
            int userId = GetUserId(db, email);

            // AynÄ± ankette daha Ã¶nceden oy verilmiÅŸ mi kontrol et
            var gonderiId = db.ExecuteScalar<int>("SELECT GonderiId FROM AnketSecenekler WHERE Id = @id", new { id = secenekId });
            var oyVarMi = db.ExecuteScalar<int>(@"SELECT COUNT(*) FROM AnketOylari ao 
                JOIN AnketSecenekler ase ON ao.SecenekId = ase.Id 
                WHERE ase.GonderiId = @gid AND ao.KullaniciId = @uid", new { gid = gonderiId, uid = userId });

            if (oyVarMi > 0) return Json(new { success = false, message = "Bu ankete zaten oy verdiniz." });

            db.Execute("INSERT INTO AnketOylari (SecenekId, KullaniciId) VALUES (@sid, @uid)", new { sid = secenekId, uid = userId });
            db.Execute("UPDATE AnketSecenekler SET OySayisi = OySayisi + 1 WHERE Id = @sid", new { sid = secenekId });

            return Json(new { success = true });
        }

        [HttpGet]
        public IActionResult GetAnketSonuc(int gonderiId)
        {
            using var db = new SqlConnection(_conn);
            string email = GetEmail();
            int userId = GetUserId(db, email);
            var secenekler = db.Query("SELECT * FROM AnketSecenekler WHERE GonderiId = @gid ORDER BY Sira", new { gid = gonderiId }).ToList();
            var oyVerdimMi = db.ExecuteScalar<int>(@"SELECT COUNT(*) FROM AnketOylari ao 
                JOIN AnketSecenekler ase ON ao.SecenekId = ase.Id 
                WHERE ase.GonderiId = @gid AND ao.KullaniciId = @uid", new { gid = gonderiId, uid = userId });
            return Json(new { success = true, secenekler, oyVerdim = oyVerdimMi > 0 });
        }

        // ===== GÃ–RÃœNTÃœLEME SAYACI =====
        [HttpPost]
        public IActionResult GonderiGoruntule(int gonderiId)
        {
            try
            {
                using var db = new SqlConnection(_conn);
                db.Execute("UPDATE SosyalGonderiler SET GoruntulemeSayisi = GoruntulemeSayisi + 1 WHERE Id = @id", new { id = gonderiId });
            }
            catch { }
            return Json(new { success = true });
        }

        // ===== PROFÄ°L FOTOÄRAFI & KAPAK =====
        [HttpPost]
        public async Task<IActionResult> ProfilResmiYukle(IFormFile foto)
        {
            string email = GetEmail() ?? "test@test.com";
            if (foto == null) return Json(new { success = false });
            if (!Kartist.Helpers.FileUploadValidator.TryValidateImage(foto, 5 * 1024 * 1024, out var ext, out var err))
                return Json(new { success = false, message = err });

            var dosyaAdi = $"avatar_{Guid.NewGuid():N}{ext}";
            var klasor = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads/avatars");
            if (!Directory.Exists(klasor)) Directory.CreateDirectory(klasor);
            var yol = Path.Combine(klasor, dosyaAdi);
            await using var stream = new FileStream(yol, FileMode.Create);
            await foto.CopyToAsync(stream);
            var url = "/uploads/avatars/" + dosyaAdi;

            using var db = new SqlConnection(_conn);
            int userId = GetUserId(db, email);
            db.Execute("UPDATE Kullanicilar SET ProfilResmi = @url WHERE Id = @uid", new { url, uid = userId });
            return Json(new { success = true, url });
        }

        [HttpPost]
        public async Task<IActionResult> KapakResmiYukle(IFormFile kapak)
        {
            string email = GetEmail() ?? "test@test.com";
            if (kapak == null) return Json(new { success = false });
            if (!Kartist.Helpers.FileUploadValidator.TryValidateImage(kapak, 10 * 1024 * 1024, out var ext, out var err))
                return Json(new { success = false, message = err });

            var dosyaAdi = $"cover_{Guid.NewGuid():N}{ext}";
            var klasor = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads/covers");
            if (!Directory.Exists(klasor)) Directory.CreateDirectory(klasor);
            var yol = Path.Combine(klasor, dosyaAdi);
            await using var stream2 = new FileStream(yol, FileMode.Create);
            await kapak.CopyToAsync(stream2);
            var url = "/uploads/covers/" + dosyaAdi;

            using var db = new SqlConnection(_conn);
            int userId = GetUserId(db, email);
            db.Execute("UPDATE Kullanicilar SET KapakResmi = @url WHERE Id = @uid", new { url, uid = userId });
            return Json(new { success = true, url });
        }

        // ===== GÃ–NDERÄ° SABÄ°TLE =====
        [HttpPost]
        public IActionResult GonderiSabitle(int gonderiId)
        {
            string email = GetEmail();
            if (email == null) return Json(new { success = false });

            using var db = new SqlConnection(_conn);
            int userId = GetUserId(db, email);
            // Ã–nce tÃ¼m sabitlemeleri kaldÄ±r
            db.Execute("UPDATE SosyalGonderiler SET Sabitlendi = 0 WHERE KullaniciId = @uid", new { uid = userId });
            db.Execute("UPDATE SosyalGonderiler SET Sabitlendi = 1 WHERE Id = @id AND KullaniciId = @uid", new { id = gonderiId, uid = userId });
            return Json(new { success = true });
        }

        // ===== TÃœM ROZETLER =====
        [HttpGet]
        public IActionResult GetTumRozetler()
        {
            try
            {
                using var db = new SqlConnection(_conn);
                string email = GetEmail();
                int userId = GetUserId(db, email);
                var tumRozetler = db.Query(@"SELECT r.*, 
                    CASE WHEN kr.Id IS NOT NULL THEN 1 ELSE 0 END as Kazanildi,
                    kr.KazanmaTarihi
                    FROM Rozetler r LEFT JOIN KullaniciRozetleri kr ON r.Id = kr.RozetId AND kr.KullaniciId = @uid
                    ORDER BY r.Sira", new { uid = userId }).ToList();
                return Json(new { success = true, rozetler = tumRozetler });
            }
            catch { return Json(new { success = true, rozetler = new List<object>() }); }
        }

        // ===== DASHBOARD Ä°STATÄ°STÄ°KLERÄ° =====
        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        public IActionResult Istatistikler()
        {
            // DEBUG: Auth check disabled for testing
            // if (!User.Identity.IsAuthenticated) return RedirectToAction("Giris", "Account");
            return View();
        }


        [HttpGet]
        public IActionResult GetDashboardStats()
        {
            string email = GetEmail() ?? "test@test.com";
            using var db = new SqlConnection(_conn);
            int userId = GetUserId(db, email);
            if (userId == 0) userId = 1;

            try 
            {

                var etkilesimDagilimi = new 
                {
                    Posts = db.ExecuteScalar<int>("SELECT COUNT(*) FROM SosyalGonderiler WHERE KullaniciId = @uid", new { uid = userId }),
                    Likes = db.ExecuteScalar<int>("SELECT COUNT(*) FROM SosyalBegeniler WHERE KullaniciId = @uid", new { uid = userId }),
                    Comments = db.ExecuteScalar<int>("SELECT COUNT(*) FROM SosyalYorumlar WHERE KullaniciId = @uid", new { uid = userId }),
                    Bookmarks = db.ExecuteScalar<int>("SELECT COUNT(*) FROM Kaydedilenler WHERE KullaniciId = @uid", new { uid = userId }),
                    Views = db.ExecuteScalar<int>("SELECT ISNULL(SUM(GoruntulemeSayisi), 0) FROM SosyalGonderiler WHERE KullaniciId = @uid", new { uid = userId })
                };

                var son7Gun = new List<object>();
                for(int i = 6; i >= 0; i--)
                {
                    string t = DateTime.UtcNow.AddDays(-i).ToString("yyyy-MM-dd");
                    int xpGun = db.ExecuteScalar<int>("SELECT ISNULL(SUM(Miktar), 0) FROM KullaniciXP WHERE KullaniciId = @uid AND CAST(Tarih AS DATE) = @t", new { uid = userId, t });
                    son7Gun.Add(new { Tarih = t, XP = xpGun });
                }

                return Json(new { success = true, etkilesim = etkilesimDagilimi, haftalikGelisim = son7Gun });
            }
            catch 
            {
                return Json(new { success = false, message = "Analitik datalarÄ± getirilemedi." });
            }
        }

        // ===== NEW API ENDPOINTS FOR V0.DEV DESIGN =====
        
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public IActionResult LikePost(int id)
        {
            try
            {
                using var db = new SqlConnection(_conn);
                string email = GetEmail() ?? "test@test.com";
                int userId = GetUserId(db, email);
                if (userId == 0) userId = 1;
                
                // Check if already liked
                var varMi = db.ExecuteScalar<int>("SELECT COUNT(*) FROM SosyalBegeniler WHERE GonderiId = @gid AND KullaniciId = @uid", 
                    new { gid = id, uid = userId });
                
                if (varMi > 0)
                {
                    // Unlike
                    db.Execute("DELETE FROM SosyalBegeniler WHERE GonderiId = @gid AND KullaniciId = @uid", 
                        new { gid = id, uid = userId });
                    db.Execute("UPDATE SosyalGonderiler SET BegeniSayisi = BegeniSayisi - 1 WHERE Id = @id", new { id });
                    return Json(new { success = true, liked = false });
                }
                else
                {
                    // Like
                    db.Execute("INSERT INTO SosyalBegeniler (GonderiId, KullaniciId) VALUES (@gid, @uid)", 
                        new { gid = id, uid = userId });
                    db.Execute("UPDATE SosyalGonderiler SET BegeniSayisi = BegeniSayisi + 1 WHERE Id = @id", new { id });
                    return Json(new { success = true, liked = true });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public IActionResult SavePost(int id)
        {
            try
            {
                using var db = new SqlConnection(_conn);
                string email = GetEmail() ?? "test@test.com";
                int userId = GetUserId(db, email);
                if (userId == 0) userId = 1;
                
                // Check if table exists, if not create it
                try
                {
                    var tableExists = db.ExecuteScalar<int>("SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Kaydedilenler'") > 0;
                    if (!tableExists)
                    {
                        db.Execute(@"CREATE TABLE Kaydedilenler (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            GonderiId INT NOT NULL,
                            KullaniciId INT NOT NULL,
                            KaydetmeTarihi DATETIME DEFAULT GETDATE()
                        )");
                    }
                }
                catch { }
                
                // Check if already saved
                var varMi = db.ExecuteScalar<int>("SELECT COUNT(*) FROM Kaydedilenler WHERE GonderiId = @gid AND KullaniciId = @uid", 
                    new { gid = id, uid = userId });
                
                if (varMi > 0)
                {
                    // Unsave
                    db.Execute("DELETE FROM Kaydedilenler WHERE GonderiId = @gid AND KullaniciId = @uid", 
                        new { gid = id, uid = userId });
                    return Json(new { success = true, saved = false });
                }
                else
                {
                    // Save
                    db.Execute("INSERT INTO Kaydedilenler (GonderiId, KullaniciId) VALUES (@gid, @uid)", 
                        new { gid = id, uid = userId });
                    return Json(new { success = true, saved = true });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public IActionResult FollowDesigner(int id)
        {
            try
            {
                using var db = new SqlConnection(_conn);
                string email = GetEmail() ?? "test@test.com";
                int userId = GetUserId(db, email);
                if (userId == 0) userId = 1;
                if (userId == id) return Json(new { success = false, message = "Kendini takip edemezsin." });
                
                var varMi = db.ExecuteScalar<int>("SELECT COUNT(*) FROM Takipciler WHERE TakipEdenId = @ben AND TakipEdilenId = @o",
                    new { ben = userId, o = id });
                
                if (varMi > 0)
                {
                    db.Execute("DELETE FROM Takipciler WHERE TakipEdenId = @ben AND TakipEdilenId = @o", new { ben = userId, o = id });
                    return Json(new { success = true, following = false });
                }
                else
                {
                    db.Execute("INSERT INTO Takipciler (TakipEdenId, TakipEdilenId) VALUES (@ben, @o)", new { ben = userId, o = id });
                    return Json(new { success = true, following = true });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> CreatePost()
        {
            try
            {
                // Get form data
                var form = Request.Form;
                // Support both new (title, image) and old (Baslik, Resim) form field names to defeat browser caching
                string title = form["title"].FirstOrDefault() ?? form["Baslik"].FirstOrDefault();
                var image = form.Files["image"] ?? form.Files["Resim"];
                
                if (string.IsNullOrWhiteSpace(title)) 
                {
                    TempData["ErrorMessage"] = "BaÅŸlÄ±k gerekli.";
                    return Redirect(Request.Headers["Referer"].ToString() ?? "/Social/Feed");
                }
                
                if (image == null || image.Length == 0) 
                {
                    TempData["ErrorMessage"] = "GÃ¶rsel gerekli.";
                    return Redirect(Request.Headers["Referer"].ToString() ?? "/Social/Feed");
                }
                
                using var db = new SqlConnection(_conn);
                string email = GetEmail() ?? "test@test.com";
                int userId = GetUserId(db, email);
                if (userId == 0) userId = 1;
                
                string fileName = $"post_{Guid.NewGuid():N}{Path.GetExtension(image.FileName)}";
                string uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "posts");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);
                
                string filePath = Path.Combine(uploadsFolder, fileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await image.CopyToAsync(stream);
                }
                
                var sql = @"INSERT INTO SosyalGonderiler (KullaniciId, Icerik, GorselUrl, OlusturmaTarihi, BegeniSayisi, YorumSayisi, GoruntulemeSayisi) 
                            VALUES (@uid, @title, @url, GETDATE(), 0, 0, 0);";
                            
                db.Execute(sql, new { 
                    uid = userId, 
                    title, 
                    url = $"/uploads/posts/{fileName}"
                });
                
                try { KazanXP(db, userId, 50, "gonderi", "Yeni gÃ¶nderi paylaÅŸtÄ±n"); } catch { }
                
                TempData["SuccessMessage"] = "GÃ¶nderi baÅŸarÄ±yla paylaÅŸÄ±ldÄ±!";
                return RedirectToAction("Feed");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return Redirect(Request.Headers["Referer"].ToString() ?? "/Social/Feed");
            }
        }

        // ===== SWIPE DATA =====
        [HttpGet]
        [IgnoreAntiforgeryToken]
        public IActionResult GetSwipeData()
        {
            try
            {
                using var db = new SqlConnection(_conn);
                string email = GetEmail();
                int userId = email != null ? GetUserId(db, email) : 0;
                
                var designs = db.Query(@"
                    SELECT TOP 20 g.Id, g.Icerik as Title, g.GorselUrl as ImageUrl, 
                           g.BegeniSayisi as Likes, g.GoruntulemeSayisi as Views,
                           k.AdSoyad as UserName, k.Seviye as UserLevel,
                           CASE WHEN k.ProfilResmi IS NULL OR k.ProfilResmi = '' OR k.ProfilResmi LIKE '/uploads/%'
                                THEN 'https://ui-avatars.com/api/?name=' + REPLACE(k.AdSoyad, ' ', '+') + '&background=random&size=128'
                                ELSE k.ProfilResmi END as UserAvatar
                    FROM SosyalGonderiler g
                    JOIN Kullanicilar k ON g.KullaniciId = k.Id
                    WHERE g.GorselUrl IS NOT NULL AND g.GorselUrl != ''
                    ORDER BY NEWID()").ToList();
                
                return Json(new { success = true, designs });
            }
            catch { return Json(new { success = true, designs = new List<object>() }); }
        }

        // ===== PROFIL - BEGENILERIM =====
        [HttpGet]
        [IgnoreAntiforgeryToken]
        public IActionResult GetBegenilerim()
        {
            try
            {
                string email = GetEmail();
                if (email == null) return Json(new { success = false });

                using var db = new SqlConnection(_conn);
                int userId = GetUserId(db, email);

                var posts = db.Query(@"SELECT g.Id, g.Icerik, g.GorselUrl, g.BegeniSayisi, g.GoruntulemeSayisi
                    FROM SosyalGonderiler g
                    JOIN SosyalBegeniler b ON g.Id = b.GonderiId
                    WHERE b.KullaniciId = @uid
                    ORDER BY b.Id DESC", new { uid = userId }).ToList();

                return Json(new { success = true, posts });
            }
            catch { return Json(new { success = true, posts = new List<object>() }); }
        }

    }
}


