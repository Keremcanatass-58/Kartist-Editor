using Dapper;
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
                var adminId = db.ExecuteScalar<int?>(
                    "SELECT TOP 1 Id FROM Yoneticiler WHERE KullaniciAdi = @k AND Sifre = @s",
                    new { k = kadi, s = sifre }, commandTimeout: 3);

                if (adminId.HasValue)
                {
                    HttpContext.Session.SetString("AdminOturumu", "Aktif");
                    return RedirectToAction("Panel");
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

        public IActionResult Cikis()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }
    }
}
