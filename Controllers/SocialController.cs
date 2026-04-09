using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

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
        public IActionResult Feed(string filtre = "yeni")
        {
            if (!User.Identity.IsAuthenticated) return RedirectToAction("Giris", "Account");
            ViewBag.Filtre = filtre;
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetFeed(int sayfa = 0, string filtre = "yeni")
        {
            if (CurrentUserId == 0)
                return Json(new { success = false, message = "Oturumunuz süresi dolmuş olabilir. Lütfen tekrar giriş yapın." });

            try
            {
                var result = await _socialService.GetFeedAsync(CurrentUserId, sayfa, filtre);
                return Json(result);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Akış yüklenemedi: {ex.Message}" });
            }
        }

        // ===== GÖNDERİ OLUŞTUR =====
        [HttpPost]
        public async Task<IActionResult> GonderiOlustur(string icerik, IFormFile gorsel, IFormFile onceSonraGorsel, string kodSinipet)
        {
            if (CurrentUserId == 0) return Json(new { success = false, message = "Giriş yapmalısın." });

            string webRootPath = System.IO.Directory.GetCurrentDirectory() + "/wwwroot";
            
            dynamic result = await _socialService.CreatePostAsync(CurrentUserId, icerik, gorsel, onceSonraGorsel, kodSinipet, webRootPath);
            
            if (result.success)
            {
                // Gamification triggers after successful creation
                using var db = new SqlConnection(_conn);
                KazanXP(db, CurrentUserId, 50, "gonderi", "Yeni gönderi paylaştın");
                GunlukGorevIlerle(db, CurrentUserId, "gonderi");
                RozetKontrol(db, CurrentUserId);
            }

            return Json(result);
        }

        // ===== GÖNDERİ SİL =====
        [HttpPost]
        public async Task<IActionResult> GonderiSil(int gonderiId)
        {
            if (CurrentUserId == 0) return Json(new { success = false, message = "Giriş yapmalısın." });
            
            string webRootPath = System.IO.Directory.GetCurrentDirectory() + "/wwwroot";
            var result = await _socialService.DeletePostAsync(gonderiId, CurrentUserId, webRootPath);
            return Json(result);
        }

        // ===== GÖNDERİ DÜZENLE =====
        [HttpPost]
        public async Task<IActionResult> GonderiDuzenle(int gonderiId, string icerik)
        {
            if (CurrentUserId == 0) return Json(new { success = false, message = "Giriş yapmalısın." });

            var result = await _socialService.EditPostAsync(gonderiId, CurrentUserId, icerik);
            return Json(result);
        }

        // ===== REPOST (RETWEET) =====
        [HttpPost]
        public IActionResult Repost(int gonderiId)
        {
            string email = GetEmail();
            if (email == null) return Json(new { success = false, message="Giriş yapmalısın." });

            using var db = new SqlConnection(_conn);
            int userId = GetUserId(db, email);

            var varMi = db.ExecuteScalar<int>("SELECT COUNT(*) FROM Repostlar WHERE OrijinalGonderiId = @gid AND KullaniciId = @uid", new { gid = gonderiId, uid = userId });
            if(varMi > 0) return Json(new { success = false, message="Bunu zaten yeniden paylaştın." });

            db.Execute("INSERT INTO Repostlar (OrijinalGonderiId, KullaniciId) VALUES (@gid, @uid)", new { gid = gonderiId, uid = userId });
            
            var orj = db.QueryFirstOrDefault("SELECT Icerik, GorselUrl, KullaniciId, (SELECT AdSoyad FROM Kullanicilar WHERE Id=g.KullaniciId) as Ad FROM SosyalGonderiler g WHERE Id=@gid", new {gid = gonderiId});
            if(orj != null) {
               string repostIcerik = $"[Repost edildi: @{orj.Ad}]\n\n{orj.Icerik}";
               db.Execute("INSERT INTO SosyalGonderiler (KullaniciId, Icerik, GorselUrl) VALUES (@uid, @icerik, @gorsel)", new { uid=userId, icerik=repostIcerik, gorsel=orj.GorselUrl });
               KazanXP(db, userId, 20, "repost");
            }
            return Json(new { success = true, xp = 20 });
        }

        // Hikaye metodları aşağıda tanımlı olduğu için burası silindi.

        // ===== BEĞENİ =====
        // ===== BEĞENİ =====
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
                    string msg = $"{ad} gönderini beğendi ❤️";
                    
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
                // Bildirim gönder (Gonderi Sahibine)
                using var db = new SqlConnection(_conn);
                var gonderiSahibiID = db.ExecuteScalar<int>("SELECT KullaniciId FROM SosyalGonderiler WHERE Id = @gid", new { gid = gonderiId });
                
                if (gonderiSahibiID != 0 && gonderiSahibiID != CurrentUserId)
                {
                    var ad = db.ExecuteScalar<string>("SELECT AdSoyad FROM Kullanicilar WHERE Id = @id", new { id = CurrentUserId });
                    var email = db.ExecuteScalar<string>("SELECT Email FROM Kullanicilar WHERE Id = @id", new { id = gonderiSahibiID });
                    string msg = $"{ad} gönderine yorum yaptı 💬";
                    
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

        // ===== TAKİP =====
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
                    new { kid = hedefId, msg = $"{ad} seni takip etmeye başladı 🔔", sid = userId });

                KazanXP(db, userId, 10, "takip");
                GunlukGorevIlerle(db, userId, "takip");
                RozetKontrol(db, userId);

                return Json(new { success = true, following = true });
            }
        }

        // ===== SOSYAL PROFİL =====
        public IActionResult Profil(string id)
        {
            if (!User.Identity.IsAuthenticated) return RedirectToAction("Giris", "Account");
            
            using var db = new SqlConnection(_conn);
            int profilId = 0;
            
            // "me" = kendi profilim
            if (string.IsNullOrEmpty(id) || id.Equals("me", StringComparison.OrdinalIgnoreCase))
            {
                string email = GetEmail();
                profilId = GetUserId(db, email);
            }
            else if (int.TryParse(id, out int numId))
            {
                profilId = numId;
            }
            else
            {
                // İsme veya email prefix'ine göre bul
                profilId = db.ExecuteScalar<int>(
                    "SELECT TOP 1 Id FROM Kullanicilar WHERE AdSoyad = @name OR Email LIKE @prefix + '%'",
                    new { name = id, prefix = id });
            }

            if (profilId == 0) profilId = GetUserId(db, GetEmail()); // fallback
            ViewBag.ProfilId = profilId;
            return View();
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

        // ===== HİKAYELER =====
        [HttpPost]
        public async Task<IActionResult> HikayeOlustur(IFormFile gorsel)
        {
            string email = GetEmail();
            if (email == null) return Json(new { success = false });

            if (gorsel == null || gorsel.Length == 0) return Json(new { success = false, message = "Gorsel gerekli." });
            if (!gorsel.ContentType.StartsWith("image/")) return Json(new { success = false, message = "Sadece resim." });

            using var db = new SqlConnection(_conn);
            int userId = GetUserId(db, email);

            var dosyaAdi = $"story_{Guid.NewGuid():N}.{gorsel.ContentType.Split('/').Last()}";
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
            string email = GetEmail();
            using var db = new SqlConnection(_conn);
            int userId = GetUserId(db, email);

            // Süresi dolmuş hikayeleri sil
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

        // ===== DİREKT MESAJ =====
        public IActionResult Mesajlar()
        {
            if (!User.Identity.IsAuthenticated) return RedirectToAction("Giris", "Account");
            return View();
        }

        [HttpGet]
        public IActionResult GetSohbetler()
        {
            string email = GetEmail();
            using var db = new SqlConnection(_conn);
            int userId = GetUserId(db, email);

            var sohbetler = db.Query(@"
                SELECT k.Id, k.AdSoyad, k.ProfilResmi,
                    (SELECT TOP 1 Mesaj FROM DirektMesajlar
                     WHERE (GonderenId = @uid AND AliciId = k.Id) OR (GonderenId = k.Id AND AliciId = @uid)
                     ORDER BY Tarih DESC) as SonMesaj,
                    (SELECT TOP 1 Tarih FROM DirektMesajlar
                     WHERE (GonderenId = @uid AND AliciId = k.Id) OR (GonderenId = k.Id AND AliciId = @uid)
                     ORDER BY Tarih DESC) as SonTarih,
                    (SELECT COUNT(*) FROM DirektMesajlar WHERE GonderenId = k.Id AND AliciId = @uid AND OkunduMu = 0) as OkunmamisSayi
                FROM Kullanicilar k
                WHERE k.Id IN (
                    SELECT DISTINCT CASE WHEN GonderenId = @uid THEN AliciId ELSE GonderenId END
                    FROM DirektMesajlar WHERE GonderenId = @uid OR AliciId = @uid
                )
                ORDER BY SonTarih DESC", new { uid = userId }).ToList();

            return Json(new { success = true, sohbetler });
        }

        [HttpGet]
        public IActionResult GetMesajlar(int hedefId)
        {
            string email = GetEmail();
            using var db = new SqlConnection(_conn);
            int userId = GetUserId(db, email);

            // Okundu olarak işaretle
            db.Execute("UPDATE DirektMesajlar SET OkunduMu = 1 WHERE GonderenId = @o AND AliciId = @ben AND OkunduMu = 0",
                new { o = hedefId, ben = userId });

            var mesajlar = db.Query(@"SELECT Id, GonderenId, AliciId, Mesaj, GorselUrl, Tarih
                                      FROM DirektMesajlar
                                      WHERE (GonderenId = @ben AND AliciId = @o) OR (GonderenId = @o AND AliciId = @ben)
                                      ORDER BY Tarih ASC",
                new { ben = userId, o = hedefId }).ToList();

            var hedef = db.QueryFirstOrDefault("SELECT Id, AdSoyad, ProfilResmi FROM Kullanicilar WHERE Id = @id", new { id = hedefId });

            return Json(new { success = true, mesajlar, hedef, myId = userId });
        }

        [HttpPost]
        public IActionResult MesajGonder(int aliciId, string mesaj)
        {
            string email = GetEmail();
            if (email == null) return Json(new { success = false });
            if (string.IsNullOrWhiteSpace(mesaj)) return Json(new { success = false });

            mesaj = Helpers.InputValidator.SanitizeHtml(mesaj);
            if (mesaj.Length > 2000) mesaj = mesaj[..2000];

            using var db = new SqlConnection(_conn);
            int userId = GetUserId(db, email);

            db.Execute(@"INSERT INTO DirektMesajlar (GonderenId, AliciId, Mesaj) VALUES (@ben, @o, @msg)",
                new { ben = userId, o = aliciId, msg = mesaj });

            var ad = db.ExecuteScalar<string>("SELECT AdSoyad FROM Kullanicilar WHERE Id = @id", new { id = userId });
            db.Execute(@"INSERT INTO Bildirimler (KullaniciId, Tip, Mesaj, GonderenId)
                         VALUES (@kid, 'mesaj', @msg, @sid)",
                new { kid = aliciId, msg = $"{ad} sana mesaj gönderdi 💌", sid = userId });

            return Json(new { success = true });
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
            
            if (varMi > 0) return Json(new { success = false, message = "Zaten iş birliği talebi gönderilmiş." });

            string onYazi = "Selam! Gönderinle (Tasarımınla) ilgili aklımda şahane bir Collab (İş Birliği) fikri var. Birlikte çalışmaya ne dersin?";
            
            db.Execute(@"INSERT INTO DirektMesajlar (GonderenId, AliciId, Mesaj, Tip, BaglantiliId) 
                         VALUES (@ben, @o, @msg, 'Collab', @gid)",
                new { ben = userId, o = hedefId, msg = onYazi, gid = gonderiId });

            var ad = db.ExecuteScalar<string>("SELECT AdSoyad FROM Kullanicilar WHERE Id = @id", new { id = userId });
            db.Execute(@"INSERT INTO Bildirimler (KullaniciId, Tip, Mesaj, BaglantiliId, GonderenId)
                         VALUES (@kid, 'collab', @msg, @gid, @sid)",
                new { kid = hedefId, msg = $"{ad} sana bir İş Birliği teklifi yolladı ⚡", gid = gonderiId, sid = userId });

            return Json(new { success = true });
        }

        // ===== BİLDİRİMLER =====
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

        // ===== KEŞFET =====
        public IActionResult Kesf()
        {
            if (!User.Identity.IsAuthenticated) return RedirectToAction("Giris", "Account");
            return View();
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

        // ===== PROFIL GÜNCELLE =====
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

            if (profilResmi != null && profilResmi.Length > 0 && profilResmi.ContentType.StartsWith("image/"))
            {
                var dosyaAdi = $"avatar_{userId}_{Guid.NewGuid():N}.{profilResmi.ContentType.Split('/').Last()}";
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

                // Gece kuşu kontrolü
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
                // Görevleri oluştur (yoksa)
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

                // Tamamlananları işaretle ve XP ver
                var tamamlanan = db.Query(@"SELECT Id, XPOdulu FROM GunlukGorevler 
                    WHERE KullaniciId = @uid AND Tarih = @t AND Tamamlandi = 0 AND MevcutSayi >= HedefSayi",
                    new { uid = userId, t = bugun }).ToList();
                foreach (var g in tamamlanan)
                {
                    db.Execute("UPDATE GunlukGorevler SET Tamamlandi = 1 WHERE Id = @id", new { id = (int)g.Id });
                    KazanXP(db, userId, (int)g.XPOdulu, "gorev", "Günlük görev tamamlandı");
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

                // Son görüleni güncelle
                db.Execute("UPDATE Kullanicilar SET SonGorulenTarihi = GETUTCDATE() WHERE Id = @uid", new { uid = userId });

                // Bugün zaten giriş yaptı mı?
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
                KazanXP(db, userId, 10, "giris", "Günlük giriş");
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

                // Görevleri oluştur (yoksa)
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

        // ===== LİDERLİK =====
        public IActionResult Liderlik()
        {
            if (!User.Identity.IsAuthenticated) return RedirectToAction("Giris", "Account");
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
        // ===== SPRINT 2: ZENGİN ETKİLEŞİM =====
        // =====================================================

        // ===== EMOJI REACTIONS =====
        [HttpPost]
        public IActionResult Reaksiyon(int gonderiId, string emoji)
        {
            string email = GetEmail();
            if (email == null) return Json(new { success = false });
            var izinliEmojiler = new[] { "❤️", "🔥", "😂", "😢", "😡", "👏", "🤩" };
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
                    new { kid = gonderiSahibiId, msg = $"{ad} gönderini paylaştı 🔁", gid = gonderiId, sid = userId });
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

            KazanXP(db, userId, 40, "anket", "Anket oluşturuldu");
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

            // Aynı ankette daha önceden oy verilmiş mi kontrol et
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

        // ===== GÖRÜNTÜLEME SAYACI =====
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

        // ===== PROFİL FOTOĞRAFI & KAPAK =====
        [HttpPost]
        public async Task<IActionResult> ProfilResmiYukle(IFormFile foto)
        {
            string email = GetEmail();
            if (email == null || foto == null) return Json(new { success = false });
            if (!foto.ContentType.StartsWith("image/") || foto.Length > 5 * 1024 * 1024)
                return Json(new { success = false, message = "Geçersiz dosya." });

            var dosyaAdi = $"avatar_{Guid.NewGuid():N}.{foto.ContentType.Split('/').Last()}";
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
            string email = GetEmail();
            if (email == null || kapak == null) return Json(new { success = false });
            if (!kapak.ContentType.StartsWith("image/") || kapak.Length > 10 * 1024 * 1024)
                return Json(new { success = false, message = "Geçersiz dosya." });

            var dosyaAdi = $"cover_{Guid.NewGuid():N}.{kapak.ContentType.Split('/').Last()}";
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

        // ===== GÖNDERİ SABİTLE =====
        [HttpPost]
        public IActionResult GonderiSabitle(int gonderiId)
        {
            string email = GetEmail();
            if (email == null) return Json(new { success = false });

            using var db = new SqlConnection(_conn);
            int userId = GetUserId(db, email);
            // Önce tüm sabitlemeleri kaldır
            db.Execute("UPDATE SosyalGonderiler SET Sabitlendi = 0 WHERE KullaniciId = @uid", new { uid = userId });
            db.Execute("UPDATE SosyalGonderiler SET Sabitlendi = 1 WHERE Id = @id AND KullaniciId = @uid", new { id = gonderiId, uid = userId });
            return Json(new { success = true });
        }

        // ===== TÜM ROZETLER =====
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

        // ===== DASHBOARD İSTATİSTİKLERİ =====
        public IActionResult Istatistikler()
        {
            if (!User.Identity.IsAuthenticated) return RedirectToAction("Giris", "Account");
            return View();
        }

        [HttpGet]
        public IActionResult GetDashboardStats()
        {
            string email = GetEmail();
            if (email == null) return Json(new { success = false });

            try 
            {
                using var db = new SqlConnection(_conn);
                int userId = GetUserId(db, email);

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
                return Json(new { success = false, message = "Analitik dataları getirilemedi." });
            }
        }
    }
}