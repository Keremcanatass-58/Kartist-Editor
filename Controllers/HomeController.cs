using Dapper;
using Kartist.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Net;
using System.Net.Mail;
using System.Security.Claims;

namespace Kartist.Controllers
{
    public class HomeController : Controller
    {
        private readonly string _baglantiCumlesi;
        private readonly IConfiguration _configuration;

        public HomeController(IConfiguration config)
        {
            _configuration = config;
            _baglantiCumlesi = config.GetConnectionString("DefaultConnection");
        }

        private string GetUserEmail()
        {
            return User.Identity.IsAuthenticated ? User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value : null;
        }
        public IActionResult Index()
        {
            string email = GetUserEmail();

            try
            {
                using (var baglanti = new SqlConnection(_baglantiCumlesi))
                {
                    const int take = 24;
                    string sql = @"
                        SELECT TOP (@take)
                               s.Id,
                               s.Baslik,
                               s.ResimUrl,
                               s.Kategori,
                               s.Fiyat,
                               s.SaticiId,
                               s.OnayDurumu,
                               CASE WHEN f.Id IS NOT NULL THEN 1 ELSE 0 END as IsFavori
                        FROM Sablonlar s
                        LEFT JOIN Favoriler f ON s.Id = f.SablonId AND f.KullaniciEmail = @mail
                        WHERE (s.OnayDurumu IN ('Onaylandi','Onayli') OR s.OnayDurumu IS NULL) AND s.Kategori != 'Ozel'
                        ORDER BY s.Id DESC";

                    var kartlar = baglanti.Query<Sablon>(sql, new { mail = email, take }, commandTimeout: 5).ToList();
                    return View(kartlar);
                }
            }
            catch
            {
                return View(new List<Sablon>());
            }
        }

        [HttpPost]
        public IActionResult Favorile(int id)
        {
            string email = GetUserEmail();

            if (email == null) return Json(new { success = false, message = "Giriş yapmalısın." });

            using (var db = new SqlConnection(_baglantiCumlesi))
            {
                var varMi = db.ExecuteScalar<int>("SELECT COUNT(*) FROM Favoriler WHERE KullaniciEmail = @e AND SablonId = @sid", new { e = email, sid = id });

                if (varMi > 0)
                {
                    db.Execute("DELETE FROM Favoriler WHERE KullaniciEmail = @e AND SablonId = @sid", new { e = email, sid = id });
                    return Json(new { success = true, isFav = false });
                }
                else
                {
                    db.Execute("INSERT INTO Favoriler (KullaniciEmail, SablonId) VALUES (@e, @sid)", new { e = email, sid = id });
                    return Json(new { success = true, isFav = true });
                }
            }
        }

        public IActionResult SatinAl(int id)
        {
            if (!User.Identity.IsAuthenticated) return RedirectToAction("Giris", "Account");

            using (var db = new SqlConnection(_baglantiCumlesi))
            {
                var kart = db.QueryFirstOrDefault<Sablon>("SELECT * FROM Sablonlar WHERE Id = @id", new { id });
                return View(kart);
            }
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public IActionResult OdemeYap([FromBody] OdemeModel veri)
        {
            try
            {
                if (string.IsNullOrEmpty(veri.KartNumarasi) || veri.KartNumarasi.Length < 13)
                    return Json(new { success = false, message = "Kart numarası eksik veya hatalı!" });

                System.Threading.Thread.Sleep(2000);
                return Json(new { success = true, redirectUrl = $"/Home/Tasarim/{veri.KartId}" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Sistem Hatası: " + ex.Message });
            }
        }

        public IActionResult Tasarim(int id, int? kayitliId = null)
        {
            if (!User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Giris", "Account");
            }
            string email = GetUserEmail();

            Sablon secilenKart;

            if (kayitliId.HasValue && kayitliId > 0)
            {
                if (email == null) return RedirectToAction("Giris", "Account");

                using (var db = new SqlConnection(_baglantiCumlesi))
                {
                    string sahipEmail = db.QueryFirstOrDefault<string>(
                        "SELECT KullaniciEmail FROM KayitliTasarimlar WHERE Id = @kid", new { kid = kayitliId });

                    if (sahipEmail != email)
                    {
                        return Content("? HATA: Bu tasarım size ait değil! Başkasının çalışmasını düzenleyemezsiniz.");
                    }
                }
            }

            if (id == 0)
            {
                secilenKart = new Sablon { Id = 0, Baslik = "Boş Tuval", ResimUrl = "", Fiyat = 0, Kategori = "Ozel" };
            }
            else
            {
                using (var baglanti = new SqlConnection(_baglantiCumlesi))
                {
                    secilenKart = baglanti.QueryFirstOrDefault<Sablon>("SELECT * FROM Sablonlar WHERE Id = @Id", new { Id = id });
                }
            }

            if (secilenKart == null) return RedirectToAction("Index");

            using (var baglanti = new SqlConnection(_baglantiCumlesi))
            {
                string jsonVerisi = null;
                if (kayitliId.HasValue)
                {
                    var kayit = baglanti.QueryFirstOrDefault<string>(
                        "SELECT JsonVerisi FROM KayitliTasarimlar WHERE Id = @kid AND KullaniciEmail = @mail",
                        new { kid = kayitliId, mail = email });
                    if (kayit != null) jsonVerisi = kayit;
                }

                int kalanKredi = 0;
                string uyelikTipi = "Normal";

                if (email != null)
                {
                    var user = baglanti.QueryFirstOrDefault("SELECT KalanKredi, UyelikTipi FROM Kullanicilar WHERE Email = @e", new { e = email });
                    if (user != null)
                    {
                        kalanKredi = user.KalanKredi;
                        uyelikTipi = user.UyelikTipi;
                    }
                }

                ViewBag.JsonVerisi = jsonVerisi;
                ViewBag.KayitliId = kayitliId;
                ViewBag.Kredi = kalanKredi;
                ViewBag.UyelikTipi = uyelikTipi;
                ViewBag.UserEmail = email;

                return View(secilenKart);
            }
        }

        [HttpPost]
        public IActionResult TasarimiKaydet(int sablonId, string jsonVerisi, int? kayitliId, string resimDataUrl, string muzikUrl)
        {
            try
            {
                string email = GetUserEmail();

                if (email == null)
                    return Json(new { success = false, message = "Kaydetmek için giriş yapmalısın." });

                // ? 0 veya negatif sablonId gelirse FK'ye takılmasın
                int? sid = sablonId > 0 ? sablonId : (int?)null;

                string onizlemeYolu = null;

                if (!string.IsNullOrEmpty(resimDataUrl))
                {
                    try
                    {
                        var base64Data = resimDataUrl.Split(',')[1];
                        byte[] imageBytes = Convert.FromBase64String(base64Data);
                        string dosyaAdi = "design_" + Guid.NewGuid().ToString().Substring(0, 8) + ".png";
                        string klasorYolu = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads/designs");
                        if (!Directory.Exists(klasorYolu)) Directory.CreateDirectory(klasorYolu);
                        string tamYol = Path.Combine(klasorYolu, dosyaAdi);
                        System.IO.File.WriteAllBytes(tamYol, imageBytes);
                        onizlemeYolu = "/uploads/designs/" + dosyaAdi;
                    }
                    catch
                    {
                        // preview üretilemezse kaydetmeyi engelleme
                    }
                }

                using (var db = new SqlConnection(_baglantiCumlesi))
                {
                    int yeniId = 0;

                    if (kayitliId.HasValue && kayitliId.Value > 0)
                    {
                        string sql = @"UPDATE KayitliTasarimlar
                               SET JsonVerisi = @json, Tarih = GETDATE(), MuzikUrl = @muz " +
                                       (onizlemeYolu != null ? ", OnizlemeResmi = @img" : "") +
                                       " WHERE Id = @id AND KullaniciEmail = @mail";

                        int rows = db.Execute(sql, new { json = jsonVerisi, id = kayitliId, mail = email, img = onizlemeYolu, muz = muzikUrl });

                        if (rows == 0)
                            return Json(new { success = false, message = "Hata: Bu tasarım size ait değil veya bulunamadı." });

                        yeniId = kayitliId.Value;
                    }
                    else
                    {
                        string sql = @"INSERT INTO KayitliTasarimlar (KullaniciEmail, SablonId, JsonVerisi, OnizlemeResmi, MuzikUrl)
                               OUTPUT INSERTED.Id
                               VALUES (@mail, @sid, @json, @img, @muz)";

                        yeniId = db.ExecuteScalar<int>(sql, new
                        {
                            mail = email,
                            sid = sid, // ? artık NULL gidebilir
                            json = jsonVerisi,
                            img = onizlemeYolu,
                            muz = muzikUrl
                        });
                    }

                    return Json(new { success = true, message = "Kaydedildi! ??", id = yeniId });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Kaydetme hatası: " + ex.Message });
            }
        }


        public IActionResult TasarimiSil(int id)
        {
            string email = GetUserEmail();

            if (email == null) return RedirectToAction("Index");

            using (var db = new SqlConnection(_baglantiCumlesi))
            {
                db.Execute("DELETE FROM KayitliTasarimlar WHERE Id = @id AND KullaniciEmail = @mail", new { id = id, mail = email });
            }
            return RedirectToAction("Profil", "Account");
        }

        [HttpPost]
        public IActionResult SatisYap(string baslik, decimal fiyat, string jsonVerisi, string resimDataUrl)
        {
            string email = GetUserEmail();

            if (email == null) return Json(new { success = false, message = "Giriş yapmalısın." });

            if (string.IsNullOrWhiteSpace(baslik))
            {
                return Json(new { success = false, message = "Başlık boş olamaz." });
            }

            baslik = Helpers.InputValidator.SanitizeHtml(baslik);
            if (!Helpers.InputValidator.IsValidInput(baslik))
            {
                return Json(new { success = false, message = "Geçersiz karakterler tespit edildi." });
            }

            if (baslik.Length > 200)
            {
                return Json(new { success = false, message = "Başlık çok uzun (max 200 karakter)." });
            }

            if (fiyat < 0 || fiyat > 10000)
            {
                return Json(new { success = false, message = "Fiyat 0 ile 10000 arasında olmalıdır." });
            }

            int userId = 0;
            string uyelikTipi = "";

            using (var db = new SqlConnection(_baglantiCumlesi))
            {
                var user = db.QueryFirstOrDefault("SELECT Id, UyelikTipi FROM Kullanicilar WHERE Email = @e", new { e = email });
                if (user == null) return Json(new { success = false, message = "Kullanıcı bulunamadı." });
                userId = user.Id;
                uyelikTipi = user.UyelikTipi;
            }

            if (uyelikTipi != "Pro") return Json(new { success = false, message = "Satış yapmak için PRO üye olmalısın!" });

            string resimUrl = "/img/default-card.jpg";
            try
            {
                var base64Data = resimDataUrl.Split(',')[1];
                byte[] imageBytes = Convert.FromBase64String(base64Data);
                string dosyaAdi = Guid.NewGuid().ToString() + ".png";
                string yol = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads/market", dosyaAdi);
                if (!Directory.Exists(Path.GetDirectoryName(yol))) Directory.CreateDirectory(Path.GetDirectoryName(yol));
                System.IO.File.WriteAllBytes(yol, imageBytes);
                resimUrl = "/uploads/market/" + dosyaAdi;
            }
            catch { }

            using (var db = new SqlConnection(_baglantiCumlesi))
            {
                string sql = @"INSERT INTO Sablonlar (Baslik, Kategori, Fiyat, ResimUrl, SaticiId, OnayDurumu, JsonVerisi) 
                               VALUES (@b, 'Kullanıcı Tasarımı', @f, @r, @uid, 'Bekliyor', @json)";
                db.Execute(sql, new { b = baslik, f = fiyat, r = resimUrl, uid = userId, json = jsonVerisi });
            }
            return Json(new { success = true, message = "Tasarımın onaya gönderildi! ??" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Iletisim(string email, string mesaj)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(mesaj))
            {
                return Json(new { success = false, message = "Lütfen tüm alanları doldur." });
            }

            // --- YENİ EFSANE TASARIM ---
            string emailSablonu = $@"
                            <!DOCTYPE html>
                            <html>
                            <head>
                                <style>
                                    .hover-effect:hover {{ border-color: #c6ff00 !important; }}
                                </style>
                            </head>
                            <body style='margin:0; padding:0; background-color:#050505; font-family:""Segoe UI"", Helvetica, Arial, sans-serif;'>
        
                                <table width='100%' border='0' cellspacing='0' cellpadding='0' style='background-color:#050505; padding: 40px 0;'>
                                    <tr>
                                        <td align='center'>
                    
                                            <table width='600' border='0' cellspacing='0' cellpadding='0' style='background-color:#0f0f13; border:1px solid #2a2a2a; border-radius:12px; overflow:hidden; box-shadow: 0 0 40px rgba(198, 255, 0, 0.05);'>
                        
                                                <tr>
                                                    <td style='padding: 30px 40px; border-bottom:1px solid #222; background: linear-gradient(90deg, #0f0f13 0%, #1a1a20 100%);'>
                                                        <table width='100%' border='0' cellspacing='0' cellpadding='0'>
                                                            <tr>
                                                                <td align='left'>
                                                                    <h1 style='margin:0; color:#fff; font-size:24px; letter-spacing:-1px; font-weight:800;'>
                                                                        KART<span style='color:#c6ff00;'>IST</span>
                                                                    </h1>
                                                                </td>
                                                                <td align='right'>
                                                                    <span style='background-color:rgba(198, 255, 0, 0.1); color:#c6ff00; border:1px solid rgba(198, 255, 0, 0.2); padding: 6px 12px; border-radius:50px; font-size:11px; font-weight:bold; letter-spacing:1px;'>
                                                                        YENİ MESAJ ?
                                                                    </span>
                                                                </td>
                                                            </tr>
                                                        </table>
                                                    </td>
                                                </tr>

                                                <tr>
                                                    <td style='padding: 40px;'>
                                                        <table width='100%' border='0' cellspacing='0' cellpadding='0' style='margin-bottom:25px;'>
                                                            <tr>
                                                                <td width='48%' valign='top' style='background-color:#16161a; padding:15px; border-radius:8px; border:1px solid #333;'>
                                                                    <p style='margin:0; font-size:10px; color:#666; text-transform:uppercase; font-weight:bold; letter-spacing:1px; margin-bottom:5px;'>GÖNDEREN</p>
                                                                    <p style='margin:0; font-size:14px; color:#fff; font-weight:600; overflow-wrap: break-word;'>{email}</p>
                                                                </td>
                                        
                                                                <td width='4%'></td> <td width='48%' valign='top' style='background-color:#16161a; padding:15px; border-radius:8px; border:1px solid #333;'>
                                                                    <p style='margin:0; font-size:10px; color:#666; text-transform:uppercase; font-weight:bold; letter-spacing:1px; margin-bottom:5px;'>TARİH</p>
                                                                    <p style='margin:0; font-size:14px; color:#fff; font-weight:600;'>{DateTime.Now.ToString("dd.MM.yyyy - HH:mm")}</p>
                                                                </td>
                                                            </tr>
                                                        </table>

                                                        <p style='margin:0 0 10px 0; font-size:11px; color:#888; text-transform:uppercase; font-weight:bold; letter-spacing:1px;'>MESAJ İÇERİĞİ:</p>
                                
                                                        <div style='background-color:#000; border:1px solid #333; border-left:4px solid #c6ff00; border-radius:6px; padding:20px; color:#ccc; font-size:14px; line-height:1.6; font-family:""Courier New"", Courier, monospace;'>
                                                            {mesaj}
                                                        </div>

                                                        <table width='100%' border='0' cellspacing='0' cellpadding='0' style='margin-top:30px;'>
                                                            <tr>
                                                                <td align='center'>
                                                                    <a href='mailto:{email}' style='background-color:#c6ff00; color:#000; text-decoration:none; padding:14px 30px; font-weight:800; font-size:14px; border-radius:6px; display:inline-block; transition:0.3s;'>
                                                                        YANITLA <span style='font-size:16px;'>&rarr;</span>
                                                                    </a>
                                                                </td>
                                                            </tr>
                                                        </table>

                                                    </td>
                                                </tr>

                                                <tr>
                                                    <td style='background-color:#0a0a0c; padding:20px; text-align:center; border-top:1px solid #222;'>
                                                        <p style='margin:0; font-size:12px; color:#444;'>
                                                            Bu mesaj <strong>Kartist Web Sitesi</strong> üzerinden gönderilmiştir.<br>
                                                            IP Adresi: {HttpContext.Connection.RemoteIpAddress}
                                                        </p>
                                                    </td>
                                                </tr>

                                            </table>
                    
                                            <p style='margin-top:20px; font-size:11px; color:#333; text-align:center;'>
                                                &copy; {DateTime.Now.Year} Kartist Inc. Tüm hakları saklıdır.
                                            </p>

                                        </td>
                                    </tr>
                                </table>
                            </body>
                            </html>";

            try
            {
                // Not: Alıcı (toEmail) yine senin mailin olacak.
                MailGonder("kartistt.official@gmail.com", "? Yeni Mesaj: " + email, emailSablonu);
                return Json(new { success = true, message = "Mesajın başarıyla iletildi!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Hata oluştu: " + ex.Message });
            }
        }

        // SMTP Fonksiyonu (Senin çalışan ayarlarınla güncellendi)
        private void MailGonder(string toEmail, string subject, string body)
        {
            // SENİN ÇALIŞAN AYARLARIN
            string gonderenMail = "kartistt.official@gmail.com";
            string uygulamaSifresi = "dvab taay cpba xunv "; // Senin App Password

            try
            {
                using (var smtp = new SmtpClient("smtp.gmail.com", 587))
                {
                    smtp.EnableSsl = true;
                    smtp.DeliveryMethod = SmtpDeliveryMethod.Network;
                    smtp.UseDefaultCredentials = false;
                    smtp.Credentials = new NetworkCredential(gonderenMail, uygulamaSifresi);

                    var mail = new MailMessage
                    {
                        From = new MailAddress(gonderenMail, "Kartist İletişim"),
                        Subject = subject,
                        Body = body,
                        IsBodyHtml = true // HTML Tasarımı için şart
                    };

                    mail.To.Add(toEmail);
                    smtp.Send(mail);
                }
            }
            catch
            {
                throw; // Hatayı yukarı fırlat ki JSON olarak kullanıcıya dönebilelim
            }
        }

        public IActionResult Rehber() { return View(); }
        public IActionResult Kurumsal() { return View(); }

        [HttpPost]
        public IActionResult LinkOlustur(int kayitId, string kategori = "Standart", string muzikUrl = "")
        {
            string email = GetUserEmail();

            if (email == null) return Json(new { success = false, message = "Giriş yapmalısın." });

            try
            {
                using (var db = new SqlConnection(_baglantiCumlesi))
                {
                    var kod = Guid.NewGuid().ToString("N").Substring(0, 10);

                    string sql = @"UPDATE KayitliTasarimlar 
                                   SET PaylasimKodu = @kod, 
                                       PaylasimKategorisi = @kat, 
                                       MuzikUrl = @muz 
                                   WHERE Id = @id AND KullaniciEmail = @mail";

                    int rows = db.Execute(sql, new { kod = kod, kat = kategori, muz = muzikUrl, id = kayitId, mail = email });

                    if (rows == 0) return Json(new { success = false, message = "Tasarım bulunamadı veya size ait değil." });

                    var link = $"{Request.Scheme}://{Request.Host}/Home/KartGoster/{kod}";
                    return Json(new { success = true, link = link });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Veritabanı Hatası: " + ex.Message });
            }
        }

        public IActionResult KartGoster(string id)
        {
            using (var db = new SqlConnection(_baglantiCumlesi))
            {
                string sql = @"
                    SELECT k.JsonVerisi, k.MuzikUrl,
                           COALESCE(s.ResimUrl, '') as ResimUrl, 
                           ISNULL(k.PaylasimKategorisi, 'Standart') as Kategori 
                    FROM KayitliTasarimlar k
                    LEFT JOIN Sablonlar s ON k.SablonId = s.Id
                    WHERE k.PaylasimKodu = @kod";

                var veri = db.QueryFirstOrDefault<dynamic>(sql, new { kod = id });

                if (veri == null) return Content("Kart bulunamadı.");

                ViewBag.Json = veri.JsonVerisi;
                ViewBag.Bg = veri.ResimUrl;
                ViewBag.Kategori = (string)veri.Kategori;
                ViewBag.Muzik = (string)veri.MuzikUrl;

                return View();
            }
        }

        [HttpPost]
        public async Task<IActionResult> MuzikYukle(IFormFile muzikDosyasi)
        {
            if (muzikDosyasi == null || muzikDosyasi.Length == 0) return Json(new { success = false, message = "Dosya seçilmedi." });

            try
            {
                if (!muzikDosyasi.ContentType.StartsWith("audio/")) return Json(new { success = false, message = "Lütfen geçerli bir ses dosyası yükleyin (MP3/WAV)." });

                var dosyaAdi = Guid.NewGuid().ToString() + Path.GetExtension(muzikDosyasi.FileName);
                var klasor = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads/music");
                if (!Directory.Exists(klasor)) Directory.CreateDirectory(klasor);

                var yol = Path.Combine(klasor, dosyaAdi);
                using (var stream = new FileStream(yol, FileMode.Create))
                {
                    await muzikDosyasi.CopyToAsync(stream);
                }

                return Json(new { success = true, url = "/uploads/music/" + dosyaAdi });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Hata: " + ex.Message });
            }
        }

        [HttpPost]
        public IActionResult TopluSil(List<int> secilenIdler)
        {
            string email = GetUserEmail();

            if (email == null) return RedirectToAction("Index");

            if (secilenIdler != null && secilenIdler.Count > 0)
            {
                using (var db = new SqlConnection(_baglantiCumlesi))
                {
                    string sql = "DELETE FROM KayitliTasarimlar WHERE Id IN @Ids AND KullaniciEmail = @Email";
                    db.Execute(sql, new { Ids = secilenIdler, Email = email });
                }
            }
            return RedirectToAction("Profil", "Account");
        }

        public IActionResult ProjeAdDegistir(int id, string yeniAd)
        {
            string email = GetUserEmail();

            if (email == null) return Json(new { success = false });

            using (var db = new SqlConnection(_baglantiCumlesi))
            {
                db.Execute("UPDATE KayitliTasarimlar SET ProjeAdi = @ad WHERE Id = @id AND KullaniciEmail = @mail",
                    new { ad = yeniAd, id = id, mail = email });
            }
            return Json(new { success = true });
        }

        public IActionResult ProjeKopyala(int id)
        {
            string email = GetUserEmail();

            if (email == null) return RedirectToAction("Index");

            using (var db = new SqlConnection(_baglantiCumlesi))
            {
                var mevcut = db.QueryFirstOrDefault("SELECT * FROM KayitliTasarimlar WHERE Id = @id AND KullaniciEmail = @mail",
                    new { id = id, mail = email });

                if (mevcut != null)
                {
                    string yeniBaslik = (mevcut.ProjeAdi ?? "Tasarım") + " (Kopya)";
                    string sql = @"INSERT INTO KayitliTasarimlar (KullaniciEmail, SablonId, JsonVerisi, OnizlemeResmi, ProjeAdi) 
                                   VALUES (@mail, @sid, @json, @img, @ad)";

                    db.Execute(sql, new { mail = email, sid = mevcut.SablonId, json = mevcut.JsonVerisi, img = mevcut.OnizlemeResmi, ad = yeniBaslik });
                }
            }
            return RedirectToAction("Profil", "Account");
        }

        [HttpPost]
        public IActionResult IndirmeHakkiKontrol()
        {
            string email = GetUserEmail();

            if (email == null) return Json(new { success = false, message = "Giriş yapmalısın." });

            using (var db = new SqlConnection(_baglantiCumlesi))
            {
                var user = db.QueryFirstOrDefault("SELECT UyelikTipi, KalanKredi, UyelikBitisTarihi FROM Kullanicilar WHERE Email = @e", new { e = email });

                if (user.UyelikTipi == "Pro")
                {
                    if (user.UyelikBitisTarihi < DateTime.Now)
                    {
                        db.Execute("UPDATE Kullanicilar SET UyelikTipi = 'Normal', KalanKredi = 0 WHERE Email = @e", new { e = email });
                        return Json(new { success = false, message = "PRO üyeliğinin süresi dolmuş." });
                    }
                    return Json(new { success = true });
                }

                if (user.KalanKredi > 0)
                {
                    db.Execute("UPDATE Kullanicilar SET KalanKredi = KalanKredi - 1 WHERE Email = @e", new { e = email });
                    return Json(new { success = true, kalan = user.KalanKredi - 1 });
                }
                else
                {
                    return Json(new { success = false, message = "İndirme hakkın bitti! Devam etmek için PRO'ya geç." });
                }
            }
        }

        [HttpGet]
        public IActionResult GetSablonlar(string kategori = null)
        {
            using (var db = new SqlConnection(_baglantiCumlesi))
            {
                string sql = "SELECT TOP 20 * FROM Sablonlar s WHERE s.Kategori != 'Ozel'";
                if (!string.IsNullOrEmpty(kategori) && kategori != "Hepsi") sql += " AND s.Kategori = @kat";
                sql += " ORDER BY s.Id DESC";
                var list = db.Query<Sablon>(sql, new { kat = kategori }).ToList();
                return Json(list);
            }
        }
        private string RemoveTurkishChars(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return text.Replace("ı", "i").Replace("ğ", "g").Replace("ü", "u").Replace("ş", "s").Replace("ö", "o").Replace("ç", "c")
                       .Replace("İ", "I").Replace("Ğ", "G").Replace("Ü", "U").Replace("Ş", "S").Replace("Ö", "O").Replace("Ç", "C");
        }

        [HttpPost]
        public async Task<IActionResult> TranslateImagePrompt(string prompt, string style)
        {
            try
            {
                var aiConfig = ResolveAiConfig();
                if (string.IsNullOrWhiteSpace(aiConfig.ApiKey))
                {
                    return Json(new { success = false, data = "API anahtarı bulunamadı." });
                }

                string systemPrompt = $@"You are an expert background scenery analyst. 
Your task is to extract ONLY the visual landscape/scenery keywords from the user's request for a background search.
The style is: {style ?? "Standard"}.

STRICT RULES:
1. IGNORE names of people (e.g., Mustafa, Ayşe), recipients (e.g., annem için), and the fact that it's a 'card' or 'design'.
2. FOCUS ONLY on the location (e.g., Paris, Eiffel Tower), elements (e.g., sunset, roses, sea), and aesthetic.
3. OUTPUT ONLY 3-6 English keywords separated by commas.
4. MANDATORY: Include 'no text' and 'no letters' as the last keywords to avoid text in images.
5. NO HUMANS, NO FACES. Scenery only.

Example: 'Mustafa için parisli doğum günü kartı' -> 'Paris, Eiffel Tower, city view, sunset, no text, no letters'
Example: 'Sevgilime orman manzaralı kart' -> 'Forest, pine trees, morning light, nature, no text, no letters'";

                var messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = $"User Request: {prompt}" }
                };

                var requestBody = new
                {
                    model = "llama-3.3-70b-versatile",
                    messages = messages,
                    temperature = 0.3,
                    max_tokens = 60
                };

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", aiConfig.ApiKey);
                var jsonContent = new StringContent(System.Text.Json.JsonSerializer.Serialize(requestBody), System.Text.Encoding.UTF8, "application/json");
                
                var response = await client.PostAsync("https://api.groq.com/openai/v1/chat/completions", jsonContent);

                if (!response.IsSuccessStatusCode)
                    return Json(new { success = false, data = "Groq servisi yanıt vermedi." });

                var responseString = await response.Content.ReadAsStringAsync();
                using var jsonDoc = System.Text.Json.JsonDocument.Parse(responseString);
                var content = jsonDoc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString()?.Trim().Replace("\"", "");

                return Json(new { success = true, prompt = content });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, data = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> FetchAiImageBase64(string prompt)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(prompt))
                    return Json(new { success = false, error = "Prompt boş olamaz." });

                // Pollinations.ai URL'i oluşturuluyor
                string encodedPrompt = Uri.EscapeDataString(prompt);
                int seed = new Random().Next(100000, 999999);
                string aiUrl = $"https://pollinations.ai/p/{encodedPrompt}?width=1024&height=1024&seed={seed}";

                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                
                var response = await client.GetAsync(aiUrl);
                if (!response.IsSuccessStatusCode)
                    return Json(new { success = false, error = "AI görsel motoru yanıt vermedi." });

                byte[] imageBytes = await response.Content.ReadAsByteArrayAsync();
                string contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                
                var base64 = Convert.ToBase64String(imageBytes);
                return Json(new { success = true, dataUrl = $"data:{contentType};base64,{base64}" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = "Sistem Hatası: " + ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> KartTasarimOner(string prompt, string kategori = null, string style = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(prompt))
                {
                    return Json(new { success = false, data = "Prompt boş olamaz." });
                }

                prompt = Helpers.InputValidator.SanitizeHtml(prompt);
                if (!Helpers.InputValidator.IsValidPrompt(prompt))
                {
                    return Json(new { success = false, data = "Gecersiz karakterler tespit edildi." });
                }

                if (prompt.Length > 1000)
                {
                    return Json(new { success = false, data = "Prompt çok uzun (max 1000 karakter)." });
                }

                var aiConfig = ResolveAiConfig();
                if (string.IsNullOrWhiteSpace(aiConfig.ApiKey))
                {
                    return Json(new { success = true, data = BuildFallbackDesignJson(prompt, kategori) });
                }

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", aiConfig.ApiKey);

                var systemPrompt = $@"
Sen, Kartist platformu için çalışan, dünyaca ünlü bir Baş Tasarımcısın.
Görevin, kullanıcının girdiği prompt'a göre duygusal derinliği olan, estetik ve kişiselleştirilmiş bir kart tasarımı kurgulamak.

Kullanıcı şu görsel tarzı tercih etti: {style ?? "Modern"}. 
Bu tarza uygun font ve renk seçimleri yap (Örn: Cyberpunk için neon pembe-mavi, Anime için canlı sıcak tonlar).

Kullanıcı bir isim (örn: Mustafa) veya bir ilişki (örn: sevgilim) belirtirse, bunu mutlaka anaMetin veya tema içerisinde doğal ve samimi bir şekilde kullan. 

Sadece geçerli JSON döndür:
{{
  ""renkPaleti"": [""#ArkaPlan"", ""#Panel"", ""#Vurgu""],
  ""tema"": ""Vurucu bir başlık"",
  ""yaziFontu"": ""Poppins, Montserrat, Roboto, Playfair Display veya Inter arasından seç"",
  ""layoutStyle"": ""'minimal', 'bold', 'elegant' veya 'modern' arasından seç"",
  ""anaMetin"": ""Kullanıcının duygusuna tercüman olan, 1-3 cümlelik etkileyici mesaj"",
  ""emojiler"": [""??"", ""??"", ""??""]
}}

Kurallar:
- Sadece JSON ver.
- Renk paleti 3 renkli olsun: 1. Baskın Koyu/Zemin, 2. Panel/Kart, 3. Yazı/Vurgu.
- Dil: Kullanıcı hangi dilde yazarsa o dilde cevap ver.
";

                var payload = new
                {
                    model = aiConfig.Model,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = $"İstek: {prompt}" }
                    },
                    temperature = 0.7
                };

                var jsonContent = new StringContent(
                    System.Text.Json.JsonSerializer.Serialize(payload),
                    System.Text.Encoding.UTF8,
                    "application/json"
                );

                var response = await client.PostAsync(aiConfig.Endpoint, jsonContent);
                var responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var err = responseString?.ToLowerInvariant() ?? "";
                    if (response.StatusCode == HttpStatusCode.Unauthorized || err.Contains("invalid api key") || err.Contains("invalid_api_key"))
                    {
                        return Json(new { success = true, data = BuildFallbackDesignJson(prompt, kategori) });
                    }

                    return Json(new
                    {
                        success = false,
                        data = "API Hatası: " + response.StatusCode
                    });
                }

                return Json(new { success = true, data = responseString });
            }
            catch
            {
                return Json(new { success = true, data = BuildFallbackDesignJson(prompt ?? "", kategori) });
            }
        }


        public IActionResult Koleksiyon()
        {
            string email = GetUserEmail();

            const int take = 24;

            using (var db = new SqlConnection(_baglantiCumlesi))
            {
                string sqlDefault = @"
                    SELECT TOP (@take)
                               s.Id,
                               s.Baslik,
                               s.ResimUrl,
                               s.Kategori,
                               s.Fiyat,
                               s.SaticiId,
                               s.OnayDurumu, CASE WHEN f.Id IS NOT NULL THEN 1 ELSE 0 END as IsFavori
                    FROM Sablonlar s
                    LEFT JOIN Favoriler f ON s.Id = f.SablonId AND f.KullaniciEmail = @mail
                    WHERE (s.SaticiId IS NULL OR s.SaticiId = 0)
                      AND s.Kategori != 'Ozel'
                      AND (s.OnayDurumu IN ('Onaylandi','Onayli') OR s.OnayDurumu IS NULL)
                    ORDER BY s.Id DESC";

                string sqlUser = @"
                    SELECT TOP (@take)
                               s.Id,
                               s.Baslik,
                               s.ResimUrl,
                               s.Kategori,
                               s.Fiyat,
                               s.SaticiId,
                               s.OnayDurumu, CASE WHEN f.Id IS NOT NULL THEN 1 ELSE 0 END as IsFavori
                    FROM Sablonlar s
                    LEFT JOIN Favoriler f ON s.Id = f.SablonId AND f.KullaniciEmail = @mail
                    WHERE s.SaticiId > 0
                      AND (s.OnayDurumu IN ('Onaylandi','Onayli'))
                    ORDER BY s.Id DESC";

                ViewBag.Varsayilanlar = db.Query<Sablon>(sqlDefault, new { mail = email, take }).ToList();
                ViewBag.KullaniciTasarimlari = db.Query<Sablon>(sqlUser, new { mail = email, take }).ToList();
                ViewBag.PageSize = take;

                return View();
            }
        }

        [HttpGet]
        public IActionResult KoleksiyonListe(string type, int offset = 0, int take = 24)
        {
            string email = GetUserEmail();

            if (take <= 0 || take > 60) take = 24;
            if (offset < 0) offset = 0;

            using (var db = new SqlConnection(_baglantiCumlesi))
            {
                if (string.Equals(type, "user", StringComparison.OrdinalIgnoreCase))
                {
                    string sqlUser = @"
                        SELECT s.Id, s.Baslik, s.ResimUrl, s.Fiyat, s.Kategori,
                               CASE WHEN f.Id IS NOT NULL THEN 1 ELSE 0 END as IsFavori
                        FROM Sablonlar s
                        LEFT JOIN Favoriler f ON s.Id = f.SablonId AND f.KullaniciEmail = @mail
                        WHERE s.SaticiId > 0
                          AND (s.OnayDurumu IN ('Onaylandi','Onayli'))
                        ORDER BY s.Id DESC
                        OFFSET @offset ROWS FETCH NEXT @take ROWS ONLY";

                    var list = db.Query(sqlUser, new { mail = email, offset, take }).ToList();
                    return Json(new { success = true, items = list });
                }
                else
                {
                    string sqlDefault = @"
                        SELECT s.Id, s.Baslik, s.ResimUrl, s.Fiyat, s.Kategori,
                               CASE WHEN f.Id IS NOT NULL THEN 1 ELSE 0 END as IsFavori
                        FROM Sablonlar s
                        LEFT JOIN Favoriler f ON s.Id = f.SablonId AND f.KullaniciEmail = @mail
                        WHERE (s.SaticiId IS NULL OR s.SaticiId = 0)
                          AND s.Kategori != 'Ozel'
                          AND (s.OnayDurumu IN ('Onaylandi','Onayli') OR s.OnayDurumu IS NULL)
                        ORDER BY s.Id DESC
                        OFFSET @offset ROWS FETCH NEXT @take ROWS ONLY";

                    var list = db.Query(sqlDefault, new { mail = email, offset, take }).ToList();
                    return Json(new { success = true, items = list });
                }
            }
        }
        private (string ApiKey, string Endpoint, string Model) ResolveAiConfig()
        {
            string openAiKey =
                _configuration["OpenAI:ApiKey"] ??
                _configuration["OPENAI_API_KEY"] ??
                Environment.GetEnvironmentVariable("OPENAI_API_KEY");

            string groqKey =
                _configuration["Groq:ApiKey"] ??
                _configuration["GROQ_API_KEY"] ??
                Environment.GetEnvironmentVariable("GROQ_API_KEY");

            if (!string.IsNullOrWhiteSpace(groqKey))
            {
                return (
                    groqKey.Trim(),
                    _configuration["Groq:Endpoint"] ?? "https://api.groq.com/openai/v1/chat/completions",
                    _configuration["Groq:Model"] ?? "llama-3.3-70b-versatile"
                );
            }

            if (!string.IsNullOrWhiteSpace(openAiKey))
            {
                return (
                    openAiKey.Trim(),
                    _configuration["OpenAI:Endpoint"] ?? "https://api.openai.com/v1/chat/completions",
                    _configuration["OpenAI:Model"] ?? "gpt-4o-mini"
                );
            }

            return ("", "", "");
        }

        private string BuildFallbackDesignJson(string prompt, string kategori)
        {
            var kat = string.IsNullOrWhiteSpace(kategori) ? "genel" : kategori.ToLowerInvariant();
            var promptLower = (prompt ?? "").ToLowerInvariant();

            // İsim tespiti
            string name = null;
            var patterns = new[] { " için ", " icin " };
            foreach (var p in patterns)
            {
                var idx = promptLower.IndexOf(p);
                if (idx > 0)
                {
                    var before = prompt.Substring(0, idx).Trim();
                    var words = before.Split(' ');
                    var lastWord = words[words.Length - 1].Trim();
                    if (lastWord.Length >= 2 && lastWord.Length <= 20)
                    {
                        name = char.ToUpper(lastWord[0]) + lastWord.Substring(1).ToLower();
                    }
                    break;
                }
            }

            // Kategori bazlı akıllı içerik
            string tema, anaMetin, layoutStyle;
            string[] palette, emojiler;

            if (kat.Contains("dogum") || kat.Contains("doğum") || promptLower.Contains("doğum") || promptLower.Contains("dogum"))
            {
                tema = name != null ? $"İyi ki Doğdun {name}! 🎂" : "Mutlu Yıllar! 🎂";
                anaMetin = name != null
                    ? $"Sevgili {name}, yeni yaşın sana sağlık, mutluluk ve başarı getirsin. İyi ki bu dünyaya geldin! 🎉"
                    : "Yeni yaşın kutlu olsun! Hayatın hep güzelliklerle dolsun. 🎉";
                palette = new[] { "#1a0533", "#ff6b35", "#ff1493" };
                emojiler = new[] { "🎂", "🎉", "🎈", "🥳", "✨" };
                layoutStyle = "bold";
            }
            else if (kat.Contains("ask") || kat.Contains("aşk") || kat.Contains("sevgi") || promptLower.Contains("sevgili"))
            {
                tema = name != null ? $"Sana Özel, {name} ❤️" : "Seni Seviyorum ❤️";
                anaMetin = name != null
                    ? $"{name}, seninle geçen her an hayatımın en güzel sayfası. Seni çok seviyorum. 💕"
                    : "İyi ki varsın, iyi ki hayatımdasın. Seni çok seviyorum. 💕";
                palette = new[] { "#1a0a2e", "#ff1493", "#8b5cf6" };
                emojiler = new[] { "❤️", "💕", "🌹", "💖", "✨" };
                layoutStyle = "elegant";
            }
            else if (kat.Contains("tesekkur") || kat.Contains("teşekkür"))
            {
                tema = name != null ? $"Teşekkürler {name} 🙏" : "Teşekkür Ederim 🙏";
                anaMetin = name != null
                    ? $"{name}, her şey için çok teşekkür ederim. Senin gibi birine sahip olduğum için kendimi şanslı hissediyorum."
                    : "Her şey için teşekkür ederim. İyi ki varsın. 💚";
                palette = new[] { "#0a1628", "#22c55e", "#38bdf8" };
                emojiler = new[] { "🙏", "💚", "🌟", "😊", "✨" };
                layoutStyle = "minimal";
            }
            else if (kat.Contains("tebrik"))
            {
                tema = name != null ? $"Tebrikler {name}! 🏆" : "Tebrikler! 🏆";
                anaMetin = name != null
                    ? $"{name}, başarınla gurur duyuyorum. Bu sadece bir başlangıç, en iyisi hep senin olacak! 🎉"
                    : "Başarınla gurur duyuyorum. Tebrik ederim! 🎉";
                palette = new[] { "#0f172a", "#f59e0b", "#10b981" };
                emojiler = new[] { "🏆", "🎉", "⭐", "🥇", "✨" };
                layoutStyle = "bold";
            }
            else if (kat.Contains("ozur") || kat.Contains("özür"))
            {
                tema = name != null ? $"Özür Dilerim {name} 💙" : "Özür Dilerim 💙";
                anaMetin = name != null
                    ? $"{name}, kalbini kırdıysam çok özür dilerim. Senin için ne kadar önemli olduğunu asla unutma."
                    : "Kalbini kırdıysam özür dilerim. Lütfen beni affet. 💙";
                palette = new[] { "#1b1030", "#6366f1", "#93c5fd" };
                emojiler = new[] { "💙", "🙏", "😔", "💐", "✨" };
                layoutStyle = "elegant";
            }
            else
            {
                tema = name != null ? $"{name} İçin Özel 💫" : "Sana Özel 💫";
                anaMetin = name != null
                    ? $"Sevgili {name}, senin için özel bir kart hazırladım. İyi ki hayatımdasın! ✨"
                    : (string.IsNullOrWhiteSpace(prompt) ? "Bugün senin için özel bir kart hazırladım. ✨" : prompt);
                palette = new[] { "#0f172a", "#06b6d4", "#c6ff00" };
                emojiler = new[] { "💫", "✨", "🌟", "💖", "🎨" };
                layoutStyle = "modern";
            }

            var fallback = new
            {
                renkPaleti = palette,
                tema = tema,
                yaziFontu = layoutStyle == "elegant" ? "Playfair Display" : layoutStyle == "bold" ? "Montserrat" : "Poppins",
                layoutStyle = layoutStyle,
                kategori = string.IsNullOrWhiteSpace(kategori) ? "kart" : kategori,
                anaMetin = anaMetin,
                emojiler = emojiler
            };

            return System.Text.Json.JsonSerializer.Serialize(fallback);
        }
    }
}





















