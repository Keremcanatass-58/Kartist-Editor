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
                    string yetki = db.QueryFirstOrDefault<string>("SELECT Yetki FROM Kullanicilar WHERE Email = @e", new { e = email });
                    return yetki == "Admin";
                }
                catch { return true; }
            }
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
                var admin = db.QueryFirstOrDefault("SELECT * FROM Yoneticiler WHERE KullaniciAdi = @k AND Sifre = @s", new { k = kadi, s = sifre });
                if (admin != null)
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
            if (HttpContext.Session.GetString("AdminOturumu") == null && !AdminKontrol()) return RedirectToAction("Login");

            using (var db = new SqlConnection(_baglanti))
            {
                ViewBag.ToplamUye = db.ExecuteScalar<int>("SELECT COUNT(*) FROM Kullanicilar");
                ViewBag.ToplamTasarim = db.ExecuteScalar<int>("SELECT COUNT(*) FROM Sablonlar");
                ViewBag.BekleyenSayisi = db.ExecuteScalar<int>("SELECT COUNT(*) FROM Sablonlar WHERE OnayDurumu = 'Bekliyor'");
                ViewBag.ToplamCiro = db.ExecuteScalar<decimal>("SELECT ISNULL(SUM(Fiyat), 0) FROM Sablonlar");

                ViewBag.Bekleyenler = db.Query<Sablon>("SELECT * FROM Sablonlar WHERE OnayDurumu = 'Bekliyor' ORDER BY Id DESC").ToList();

                ViewBag.SonUyeler = db.Query<dynamic>("SELECT TOP 50 * FROM Kullanicilar ORDER BY Id DESC").ToList();

                var aktifKartlar = db.Query<Sablon>("SELECT * FROM Sablonlar WHERE OnayDurumu = 'Onaylandi' OR OnayDurumu IS NULL ORDER BY Id DESC").ToList();

                return View(aktifKartlar);
            }
        }

        [HttpPost]
        public async Task<IActionResult> KrediYukle(int id, int miktar)
        {
            using (var db = new SqlConnection(_baglanti))
            {
                string email = db.QueryFirstOrDefault<string>("SELECT Email FROM Kullanicilar WHERE Id = @id", new { id });
                db.Execute("UPDATE Kullanicilar SET KalanKredi = KalanKredi + @m WHERE Id = @id", new { m = miktar, id = id });

                if (!string.IsNullOrEmpty(email)) await _hubContext.Clients.Group(email).SendAsync("SayfayiYenile");
            }
            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> UyelikDegistir(int id, string tip)
        {
            using (var db = new SqlConnection(_baglanti))
            {
                string email = db.QueryFirstOrDefault<string>("SELECT Email FROM Kullanicilar WHERE Id = @id", new { id });
                db.Execute("UPDATE Kullanicilar SET UyelikTipi = @t WHERE Id = @id", new { t = tip, id = id });

                if (!string.IsNullOrEmpty(email)) await _hubContext.Clients.Group(email).SendAsync("SayfayiYenile");
            }
            return Ok();
        }

        [HttpPost]
        public IActionResult Ekle(Sablon model)
        {
            using (var db = new SqlConnection(_baglanti))
            {
                model.OnayDurumu = "Onaylandi";
                db.Execute("INSERT INTO Sablonlar (Baslik, Kategori, Fiyat, ResimUrl, OnayDurumu) VALUES (@Baslik, @Kategori, @Fiyat, @ResimUrl, @OnayDurumu)", model);
            }
            return RedirectToAction("Panel");
        }

        public IActionResult Sil(int id)
        {
            using (var db = new SqlConnection(_baglanti)) { db.Execute("DELETE FROM Sablonlar WHERE Id = @id", new { id }); }
            return RedirectToAction("Panel");
        }

        public IActionResult Onayla(int id)
        {
            using (var db = new SqlConnection(_baglanti)) { db.Execute("UPDATE Sablonlar SET OnayDurumu = 'Onaylandi' WHERE Id = @id", new { id }); }
            return RedirectToAction("Panel");
        }

        public IActionResult Reddet(int id)
        {
            using (var db = new SqlConnection(_baglanti)) { db.Execute("DELETE FROM Sablonlar WHERE Id = @id", new { id }); }
            return RedirectToAction("Panel");
        }

        public IActionResult Cikis()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }
    }
}