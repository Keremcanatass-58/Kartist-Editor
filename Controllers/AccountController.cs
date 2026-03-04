using Dapper;
using Kartist.Hubs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using System.Net;
using System.Net.Mail;
using System.Security.Claims;



namespace Kartist.Controllers
{
    public class AccountController : Controller
    {
        private readonly string _baglanti;
        private readonly IHubContext<AdminHub> _adminHub;
        private readonly IConfiguration _configuration;


        public AccountController(IConfiguration config, IHubContext<AdminHub> adminHub)
        {
            _baglanti = config.GetConnectionString("DefaultConnection");
            _adminHub = adminHub;
            _configuration = config;
        }

        public IActionResult Giris()
        {
            if (User.Identity.IsAuthenticated) return RedirectToAction("Index", "Home");
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Giris(string email, string sifre)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(sifre))
            {
                ViewBag.Hata = "E-posta ve şifre alanları zorunludur.";
                return View();
            }

            if (!Kartist.Helpers.InputValidator.IsValidEmail(email))
            {
                ViewBag.Hata = "Geçerli bir e-posta adresi giriniz.";
                return View();
            }

            email = Kartist.Helpers.InputValidator.SanitizeHtml(email);
            if (!Kartist.Helpers.InputValidator.IsValidInput(email))
            {
                ViewBag.Hata = "Geçersiz karakterler tespit edildi.";
                return View();
            }

            using (var db = new SqlConnection(_baglanti))
            {
                var user = db.QueryFirstOrDefault("SELECT * FROM Kullanicilar WHERE Email = @e AND Sifre = @s", new { e = email, s = sifre });
                if (user != null)
                {
                    var claims = new List<Claim> { new Claim(ClaimTypes.Name, user.AdSoyad), new Claim(ClaimTypes.Email, user.Email) };
                    await HttpContext.SignInAsync("KartistCookie", new ClaimsPrincipal(new ClaimsIdentity(claims, "KartistCookie")));
                    TempData["Mesaj"] = $"Hoş geldin {user.AdSoyad}! Sahne senin. 🔥";
                    TempData["Tur"] = "success";
                    return RedirectToAction("Index", "Home");
                }
            }
            ViewBag.Hata = "E-posta veya şifre hatalı!";

            return View();
        }

        public IActionResult Kayit() { return View(); }

        [HttpPost]
        public IActionResult Kayit(string adsoyad, string email, string sifre)
        {
            if (string.IsNullOrWhiteSpace(adsoyad) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(sifre))
            {
                ViewBag.Hata = "Tüm alanlar zorunludur.";
                return View();
            }

            if (!Kartist.Helpers.InputValidator.IsValidEmail(email))
            {
                ViewBag.Hata = "Geçerli bir e-posta adresi giriniz.";
                return View();
            }

            if (sifre.Length < 6 || sifre.Length > 50)
            {
                ViewBag.Hata = "Şifre 6-50 karakter arasında olmalıdır.";
                return View();
            }

            adsoyad = Kartist.Helpers.InputValidator.SanitizeHtml(adsoyad);
            email = Kartist.Helpers.InputValidator.SanitizeHtml(email);

            if (!Kartist.Helpers.InputValidator.IsValidInput(adsoyad) || !Kartist.Helpers.InputValidator.IsValidInput(email))
            {
                ViewBag.Hata = "Geçersiz karakterler tespit edildi.";
                return View();
            }

            if (adsoyad.Length > 100)
            {
                ViewBag.Hata = "Ad soyad çok uzun (max 100 karakter).";
                return View();
            }

            using (var db = new SqlConnection(_baglanti))
            {
                var varMi = db.ExecuteScalar<int>("SELECT COUNT(*) FROM Kullanicilar WHERE Email = @e", new { e = email });
                if (varMi > 0) { ViewBag.Hata = "Bu mail zaten kayıtlı."; return View(); }

                int baslangicKredisi = 50;
                db.Execute(@"INSERT INTO Kullanicilar (AdSoyad, Email, Sifre, UyelikTipi, KalanKredi) 
                             VALUES (@a, @e, @s, 'Normal', @kredi)",
                    new { a = adsoyad, e = email, s = sifre, kredi = baslangicKredisi });
            }
            TempData["Mesaj"] = "Kaydın başarıyla oluşturuldu! Şimdi giriş yapabilirsin. 🚀";
            TempData["Tur"] = "success";
            return RedirectToAction("Giris");

        }

        public async Task<IActionResult> Cikis()
        {
            // Çerezleri temizle (Oturumu kapat)
            await HttpContext.SignOutAsync("KartistCookie");

            // Veda mesajını hazırla
            TempData["Mesaj"] = "Başarıyla çıkış yaptın. Yine bekleriz şampiyon! 👋";
            TempData["Tur"] = "success";

            // Ana sayfaya gönder (Orada _Layout.cshtml mesajı yakalayıp gösterecek)
            return RedirectToAction("Index", "Home");
        }

        public IActionResult Profil()
        {
            if (!User.Identity.IsAuthenticated) return RedirectToAction("Giris");
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ProOlAjax()
        {
            if (!User.Identity.IsAuthenticated) return Json(new { success = false, message = "Giriş yapmalısın." });

            string email = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;
            int proUcreti = 50;

            using (var db = new SqlConnection(_baglanti))
            {
                var user = db.QueryFirstOrDefault("SELECT KalanKredi, UyelikTipi FROM Kullanicilar WHERE Email = @e", new { e = email });

                if (user.UyelikTipi == "Pro")
                {
                    return Json(new { success = false, message = "Zaten PRO üyesisin kral! 👑" });
                }

                if (user.KalanKredi >= proUcreti)
                {
                    db.Execute("UPDATE Kullanicilar SET UyelikTipi = 'Pro', UyelikBitisTarihi = @t, KalanKredi = KalanKredi - @ucret WHERE Email = @e",
                        new { t = DateTime.Now.AddDays(30), ucret = proUcreti, e = email });

                    await _adminHub.Clients.All.SendAsync("AdminiUyar");

                    return Json(new { success = true, message = "Hayırlı olsun! Artık PRO Üyesin. 🎉" });
                }
                else
                {
                    return Json(new { success = false, message = $"Yetersiz Kredi! PRO olmak için {proUcreti} kredin olmalı. (Sende: {user.KalanKredi})" });
                }
            }
        }

        [HttpGet]
        public IActionResult SifremiUnuttum()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SifremiUnuttum(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                ViewBag.Hata = "Lütfen bir e-posta adresi gir.";
                return View();
            }

            using var db = new SqlConnection(_baglanti);

            // 1. Kullanıcı var mı kontrol et
            var user = db.QueryFirstOrDefault<dynamic>(
                "SELECT Email FROM Kullanicilar WHERE Email = @e",
                new { e = email.Trim() });

            if (user == null)
            {
                // Güvenlik gereği kullanıcı yoksa bile "Gönderdik" deriz ama 
                // sen şu an test ediyorsun, o yüzden net konuşalım:
                ViewBag.Hata = "Bu e-posta adresiyle kayıtlı bir kullanıcı bulunamadı.";
                return View();
            }

            // 2. Token üret ve Kaydet
            var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            var expire = DateTime.UtcNow.AddMinutes(30);

            db.Execute(@"
        INSERT INTO SifreSifirlamaTokenlari (KullaniciEmail, Token, BitisTarihi)
        VALUES (@mail, @tkn, @exp)",
                new { mail = email.Trim(), tkn = token, exp = expire });

            // 3. Linki Oluştur
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var link = $"{baseUrl}/Account/SifreSifirla?token={token}";

            // 4. Mail Gönder (Hata yakalamalı)
            try
            {
                // HTML TASARIM (Bunu kopyala ve MailGonder çağırdığın yere yapıştır)
                string emailSablonu = $@"
                        <div style='background-color:#050505; padding:40px 0; font-family: ""Segoe UI"", Tahoma, Geneva, Verdana, sans-serif;'>
                            <div style='max-width:500px; margin:0 auto; background-color:#111; border:1px solid #333; border-radius:16px; overflow:hidden; box-shadow: 0 10px 30px rgba(0,0,0,0.5);'>
        
                                <div style='background-color:#000; padding:30px; text-align:center; border-bottom:2px solid #c6ff00;'>
                                    <h1 style='color:#fff; margin:0; font-size:28px; letter-spacing:-1px;'>KART<span style='color:#c6ff00;'>IST</span></h1>
                                </div>

                                <div style='padding:40px 30px; text-align:center; color:#ddd;'>
                                    <h2 style='color:#fff; font-size:22px; margin-top:0;'>Şifreni mi Unuttun?</h2>
                                    <p style='font-size:15px; line-height:1.6; color:#aaa; margin-bottom:30px;'>
                                        Endişelenme şampiyon, hesabını kurtarmak için sana özel bir bağlantı oluşturduk. 
                                        Aşağıdaki butona tıklayarak yeni şifreni belirleyebilirsin.
                                    </p>

                                    <a href='{link}' style='background-color:#c6ff00; color:#000; text-decoration:none; padding:16px 32px; font-weight:bold; font-size:16px; border-radius:50px; display:inline-block; transition:0.3s;'>
                                        ŞİFREYİ SIFIRLA &rarr;
                                    </a>

                                    <p style='font-size:13px; color:#666; margin-top:30px;'>
                                        * Bu bağlantı güvenliğin için <strong>30 dakika</strong> sonra geçersiz olacaktır.
                                    </p>
                                </div>

                                <div style='background-color:#080808; padding:20px; text-align:center; font-size:12px; color:#555; border-top:1px solid #222;'>
                                    <p style='margin:0;'>&copy; {DateTime.Now.Year} Kartist Tasarım Platformu</p>
                                    <p style='margin:5px 0 0 0;'>Bu işlemi sen yapmadıysan, bu maili görmezden gel.</p>
                                </div>
                            </div>
                        </div>";

                // Maili Gönder
                MailGonder(email.Trim(), "🔐 Kartist - Şifre Sıfırlama Talebi", emailSablonu);

                // BAŞARILI OLDUĞUNDA BURASI ÇALIŞACAK:
                ViewBag.Mesaj = "Sıfırlama bağlantısı e-posta adresine başarıyla gönderildi! Spam kutunu kontrol etmeyi unutma.";
            }
            catch (Exception ex)
            {
                // HATA OLURSA BURASI ÇALIŞACAK:
                ViewBag.Hata = "Mail gönderilirken bir sorun oluştu: " + ex.Message;
            }

            return View();
        }
        [HttpGet]
        public IActionResult SifreSifirla(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return RedirectToAction("Giris");
            ViewBag.Token = token;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SifreSifirla(string token, string yeniSifre)
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(yeniSifre))
            {
                ViewBag.Hata = "Geçersiz istek.";
                ViewBag.Token = token;
                return View();
            }

            if (yeniSifre.Length < 6)
            {
                ViewBag.Hata = "Şifre en az 6 karakter olmalıdır.";
                ViewBag.Token = token;
                return View();
            }

            using var db = new SqlConnection(_baglanti);

            var rec = db.QueryFirstOrDefault<dynamic>(@"
        SELECT TOP 1 *
        FROM SifreSifirlamaTokenlari
        WHERE Token = @tkn AND Kullanildi = 0 AND BitisTarihi > GETUTCDATE()
        ORDER BY Id DESC",
                new { tkn = token });

            if (rec == null)
            {
                ViewBag.Hata = "Link geçersiz veya süresi dolmuş.";
                ViewBag.Token = token;
                return View();
            }

            // Şifreyi kaydet (şu an sistemin düz metin kullanıyorsa düz metin; HASH önerilir)
            db.Execute("UPDATE Kullanicilar SET Sifre = @s WHERE Email = @e",
                new { s = yeniSifre, e = (string)rec.KullaniciEmail });

            db.Execute("UPDATE SifreSifirlamaTokenlari SET Kullanildi = 1 WHERE Id = @id",
                new { id = (int)rec.Id });

            TempData["Basari"] = "Şifre güncellendi. Giriş yapabilirsin.";
            return RedirectToAction("Giris");
        }
        private void MailGonder(string toEmail, string subject, string body)
        {
            // 1. BURAYA GMAIL'DE GÖRDÜĞÜN MAİLİ KOPYALA (ELLE YAZMA!)
            string gonderenMail = "kartistt.official@gmail.com";
            // VEYA kartistt.official@gmail.com -> HANGİSİYSE ONU YAZ.

            // 2. BURAYA YENİ ALDIĞIN UYGULAMA ŞİFRESİNİ YAPIŞTIR
            string uygulamaSifresi = "dvab taay cpba xunv ";

            try
            {
                using (var smtp = new SmtpClient("smtp.gmail.com", 587))
                {
                    smtp.EnableSsl = true;
                    smtp.DeliveryMethod = SmtpDeliveryMethod.Network;
                    smtp.UseDefaultCredentials = false;

                    // Kimlik doğrulama
                    smtp.Credentials = new NetworkCredential(gonderenMail, uygulamaSifresi);

                    var mail = new MailMessage
                    {
                        From = new MailAddress(gonderenMail, "Kartist"),
                        Subject = subject,
                        Body = body,
                        IsBodyHtml = true
                    };

                    mail.To.Add(toEmail);
                    smtp.Send(mail);
                }
            }
            catch (Exception ex)
            {
                // Hata alırsan hatayı olduğu gibi fırlat
                throw new Exception("SMTP Hatası: " + ex.Message);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProfilResmiGuncelle(IFormFile profilResmi)
        {
            if (!User.Identity.IsAuthenticated)
                return RedirectToAction("Giris");

            if (profilResmi == null || profilResmi.Length == 0)
            {
                TempData["Hata"] = "Lütfen bir resim seçin.";
                return RedirectToAction("Profil");
            }

            // Basit güvenlik kontrolleri
            if (!profilResmi.ContentType.StartsWith("image/"))
            {
                TempData["Hata"] = "Sadece resim dosyası yükleyebilirsin.";
                return RedirectToAction("Profil");
            }

            var ext = Path.GetExtension(profilResmi.FileName).ToLowerInvariant();
            var allowed = new HashSet<string> { ".jpg", ".jpeg", ".png", ".webp" };
            if (!allowed.Contains(ext))
            {
                TempData["Hata"] = "Geçersiz dosya türü. (jpg, jpeg, png, webp)";
                return RedirectToAction("Profil");
            }

            // 2MB limit (istersen arttır)
            if (profilResmi.Length > 2 * 1024 * 1024)
            {
                TempData["Hata"] = "Dosya çok büyük. Maksimum 2MB.";
                return RedirectToAction("Profil");
            }

            var email = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;
            if (string.IsNullOrWhiteSpace(email))
            {
                TempData["Hata"] = "Kullanıcı bilgisi alınamadı. Tekrar giriş yap.";
                return RedirectToAction("Giris");
            }

            var klasor = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "avatars");
            Directory.CreateDirectory(klasor);

            var dosyaAdi = Guid.NewGuid().ToString("N") + ext;
            var tamYol = Path.Combine(klasor, dosyaAdi);

            using (var stream = new FileStream(tamYol, FileMode.Create))
                await profilResmi.CopyToAsync(stream);

            var dbPath = "/uploads/avatars/" + dosyaAdi;

            using (var db = new SqlConnection(_baglanti))
            {
                db.Execute("UPDATE Kullanicilar SET ProfilResmi = @p WHERE Email = @e",
                    new { p = dbPath, e = email });
            }

            return RedirectToAction("Profil");
        }
    }
}