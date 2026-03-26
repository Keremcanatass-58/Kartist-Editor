using Dapper;
using Kartist.Helpers;
using Kartist.Hubs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

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

        private string GetClientIp()
        {
            var ip = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(ip)) return ip.Split(',')[0].Trim();
            return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }

        private void LogGirisDenemesi(SqlConnection db, string email, bool basarili)
        {
            try
            {
                db.Execute(@"INSERT INTO GirisLoglari (KullaniciEmail, IpAdresi, BasariliMi, UserAgent)
                    VALUES (@mail, @ip, @ok, @ua)",
                    new
                    {
                        mail = email,
                        ip = GetClientIp(),
                        ok = basarili,
                        ua = HttpContext.Request.Headers["User-Agent"].ToString().Length > 500
                            ? HttpContext.Request.Headers["User-Agent"].ToString().Substring(0, 500)
                            : HttpContext.Request.Headers["User-Agent"].ToString()
                    });
            }
            catch { }
        }

        public IActionResult Giris()
        {
            if (User.Identity.IsAuthenticated) return RedirectToAction("Index", "Home");
            return View();
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ExternalLogin(string provider, string returnUrl = null)
        {
            if (provider != "Google")
            {
                TempData["Mesaj"] = "Desteklenmeyen giris saglayicisi.";
                TempData["Tur"] = "error";
                return RedirectToAction("Giris");
            }

            returnUrl = returnUrl ?? Url.Action("Index", "Home") ?? "/";
            var redirectUrl = Url.Action(nameof(ExternalLoginCallback), "Account", new { returnUrl });
            var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
            return Challenge(properties, provider);
        }

        [HttpGet]
        public async Task<IActionResult> ExternalLoginCallback(string returnUrl = null, string remoteError = null)
        {
            returnUrl = returnUrl ?? Url.Action("Index", "Home") ?? "/";

            if (!string.IsNullOrWhiteSpace(remoteError))
            {
                TempData["Mesaj"] = "Harici giris sirasinda hata olustu: " + remoteError;
                TempData["Tur"] = "error";
                return RedirectToAction("Giris");
            }

            var authResult = await HttpContext.AuthenticateAsync("External");
            if (!authResult.Succeeded || authResult.Principal == null)
            {
                TempData["Mesaj"] = "Google oturumu dogrulanamadi.";
                TempData["Tur"] = "error";
                return RedirectToAction("Giris");
            }

            var principal = authResult.Principal;
            var email = principal.FindFirst(ClaimTypes.Email)?.Value
                        ?? principal.FindFirst("email")?.Value
                        ?? principal.FindFirst("preferred_username")?.Value;

            if (string.IsNullOrWhiteSpace(email))
            {
                await HttpContext.SignOutAsync("External");
                TempData["Mesaj"] = "Harici hesapta e-posta bilgisi bulunamadi.";
                TempData["Tur"] = "error";
                return RedirectToAction("Giris");
            }

            email = InputValidator.SanitizeHtml(email.Trim());
            var name = principal.FindFirst(ClaimTypes.Name)?.Value
                       ?? principal.FindFirst("name")?.Value
                       ?? email.Split('@')[0];
            name = InputValidator.SanitizeHtml(name);
            if (string.IsNullOrWhiteSpace(name)) name = "Kartist Kullanici";
            if (name.Length > 100) name = name.Substring(0, 100);

            using (var db = new SqlConnection(_baglanti))
            {
                var user = db.QueryFirstOrDefault("SELECT * FROM Kullanicilar WHERE Email = @e", new { e = email });

                if (user == null)
                {
                    string randomPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
                    string hashed = PasswordHasher.HashPassword(randomPassword);

                    db.Execute(@"INSERT INTO Kullanicilar (AdSoyad, Email, Sifre, UyelikTipi, KalanKredi)
                                 VALUES (@a, @e, @s, 'Normal', @kredi)",
                        new { a = name, e = email, s = hashed, kredi = 50 });

                    user = db.QueryFirstOrDefault("SELECT * FROM Kullanicilar WHERE Email = @e", new { e = email });
                }

                bool ikiFaktorAktif = user.IkiFactorAktif != null && (bool)user.IkiFactorAktif;
                if (ikiFaktorAktif)
                {
                    string kod = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
                    db.Execute(@"INSERT INTO IkiFactorKodlari (KullaniciEmail, Kod, BitisTarihi)
                                 VALUES (@mail, @kod, @bitis)",
                        new { mail = email, kod, bitis = DateTime.UtcNow.AddMinutes(5) });

                    try
                    {
                        string kodHtml = BuildIkiFaktorMailHtml(kod, email);
                        MailGonder(email, "Kartist - Iki Faktorlu Giris Kodunuz", kodHtml);
                    }
                    catch { }

                    await HttpContext.SignOutAsync("External");
                    TempData["2FA_Email"] = email;
                    return RedirectToAction("IkiFactorDogrula");
                }

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, (string)(user.AdSoyad ?? name)),
                    new Claim(ClaimTypes.Email, (string)user.Email)
                };

                await HttpContext.SignInAsync("KartistCookie", new ClaimsPrincipal(new ClaimsIdentity(claims, "KartistCookie")));
                await HttpContext.SignOutAsync("External");
            }

            TempData["Mesaj"] = "Giris basarili. Hos geldin!";
            TempData["Tur"] = "success";
            return LocalRedirect(returnUrl);
        }
        [HttpPost]
        public async Task<IActionResult> Giris(string email, string sifre)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(sifre))
            {
                ViewBag.Hata = "E-posta ve şifre alanları zorunludur.";
                return View();
            }

            if (!InputValidator.IsValidEmail(email))
            {
                ViewBag.Hata = "Geçerli bir e-posta adresi giriniz.";
                return View();
            }

            email = InputValidator.SanitizeHtml(email);
            if (!InputValidator.IsValidInput(email))
            {
                ViewBag.Hata = "Geçersiz karakterler tespit edildi.";
                return View();
            }

            using (var db = new SqlConnection(_baglanti))
            {
                var user = db.QueryFirstOrDefault("SELECT * FROM Kullanicilar WHERE Email = @e", new { e = email });

                if (user == null)
                {
                    LogGirisDenemesi(db, email, false);
                    ViewBag.Hata = "E-posta veya şifre hatalı!";
                    return View();
                }

                if (user.HesapKilitliMi != null && (bool)user.HesapKilitliMi)
                {
                    if (user.KilitBitisTarihi != null && (DateTime)user.KilitBitisTarihi > DateTime.UtcNow)
                    {
                        ViewBag.Hata = "Hesabınız çok fazla başarısız giriş denemesi nedeniyle geçici olarak kilitlendi. Lütfen 15 dakika sonra tekrar deneyin.";
                        return View();
                    }
                    db.Execute("UPDATE Kullanicilar SET HesapKilitliMi = 0, BasarisizGirisSayisi = 0, KilitBitisTarihi = NULL WHERE Email = @e", new { e = email });
                }

                bool sifreDogruMu = false;
                string dbSifre = (string)user.Sifre;

                if (PasswordHasher.IsHashed(dbSifre))
                {
                    sifreDogruMu = PasswordHasher.VerifyPassword(sifre, dbSifre);
                }
                else
                {
                    sifreDogruMu = (dbSifre == sifre);
                    if (sifreDogruMu)
                    {
                        string hashed = PasswordHasher.HashPassword(sifre);
                        db.Execute("UPDATE Kullanicilar SET Sifre = @s WHERE Email = @e", new { s = hashed, e = email });
                    }
                }

                if (!sifreDogruMu)
                {
                    int yeniSayac = (user.BasarisizGirisSayisi ?? 0) + 1;
                    LogGirisDenemesi(db, email, false);

                    if (yeniSayac >= 5)
                    {
                        db.Execute("UPDATE Kullanicilar SET BasarisizGirisSayisi = @s, HesapKilitliMi = 1, KilitBitisTarihi = @t WHERE Email = @e",
                            new { s = yeniSayac, t = DateTime.UtcNow.AddMinutes(15), e = email });

                        try
                        {
                            string uyariHtml = $@"
                                <div style='background-color:#050505; padding:40px 0; font-family:Segoe UI,sans-serif;'>
                                    <div style='max-width:500px; margin:0 auto; background:#111; border:1px solid #333; border-radius:16px; overflow:hidden;'>
                                        <div style='background:#000; padding:30px; text-align:center; border-bottom:2px solid #ff4444;'>
                                            <h1 style='color:#fff; margin:0;'>KART<span style='color:#c6ff00;'>IST</span></h1>
                                        </div>
                                        <div style='padding:40px 30px; text-align:center; color:#ddd;'>
                                            <h2 style='color:#ff4444;'>Hesabınız Kilitlendi</h2>
                                            <p>Hesabınıza 5 başarısız giriş denemesi yapıldı. Güvenliğiniz için hesabınız 15 dakika süreyle kilitlenmiştir.</p>
                                            <p style='color:#aaa; font-size:13px;'>IP: {GetClientIp()} - Tarih: {DateTime.Now:dd.MM.yyyy HH:mm}</p>
                                            <p style='color:#666; font-size:12px;'>Bu işlemi siz yapmadıysanız, şifrenizi değiştirmenizi öneririz.</p>
                                        </div>
                                    </div>
                                </div>";
                            MailGonder(email, "Kartist - Hesap Guvenlik Uyarisi", uyariHtml);
                        }
                        catch { }

                        ViewBag.Hata = "Çok fazla başarısız deneme! Hesabınız 15 dakika kilitlendi. E-postanızı kontrol edin.";
                        return View();
                    }
                    else
                    {
                        db.Execute("UPDATE Kullanicilar SET BasarisizGirisSayisi = @s WHERE Email = @e", new { s = yeniSayac, e = email });
                        ViewBag.Hata = $"E-posta veya şifre hatalı! (Kalan deneme: {5 - yeniSayac})";
                        return View();
                    }
                }

                db.Execute("UPDATE Kullanicilar SET BasarisizGirisSayisi = 0 WHERE Email = @e", new { e = email });
                LogGirisDenemesi(db, email, true);

                bool ikiFaktorAktif = user.IkiFactorAktif != null && (bool)user.IkiFactorAktif;
                if (ikiFaktorAktif)
                {
                    var kod = new Random().Next(100000, 999999).ToString();
                    db.Execute(@"INSERT INTO IkiFactorKodlari (KullaniciEmail, Kod, BitisTarihi) VALUES (@mail, @kod, @bitis)",
                        new { mail = email, kod = kod, bitis = DateTime.UtcNow.AddMinutes(5) });

                    try
                    {
                        string kodHtml = BuildIkiFaktorMailHtml(kod, email);
                        MailGonder(email, "Kartist - Iki Faktorlu Giris Kodunuz", kodHtml);
                    }
                    catch { }

                    TempData["2FA_Email"] = email;
                    return RedirectToAction("IkiFactorDogrula");
                }

                var claims = new List<Claim> { new Claim(ClaimTypes.Name, (string)user.AdSoyad), new Claim(ClaimTypes.Email, (string)user.Email) };
                await HttpContext.SignInAsync("KartistCookie", new ClaimsPrincipal(new ClaimsIdentity(claims, "KartistCookie")));
                TempData["Mesaj"] = $"Hos geldin {user.AdSoyad}!";
                TempData["Tur"] = "success";
                return RedirectToAction("Index", "Home");
            }
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
                string hashedSifre = PasswordHasher.HashPassword(sifre);
                db.Execute(@"INSERT INTO Kullanicilar (AdSoyad, Email, Sifre, UyelikTipi, KalanKredi) 
                             VALUES (@a, @e, @s, 'Normal', @kredi)",
                    new { a = adsoyad, e = email, s = hashedSifre, kredi = baslangicKredisi });
            }
            TempData["Mesaj"] = "Kaydin basariyla olusturuldu! Simdi giris yapabilirsin. ??";
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
                    return Json(new { success = false, message = "Zaten PRO üyesisin kral! gY''" });
                }

                if (user.KalanKredi >= proUcreti)
                {
                    db.Execute("UPDATE Kullanicilar SET UyelikTipi = 'Pro', UyelikBitisTarihi = @t, KalanKredi = KalanKredi - @ucret WHERE Email = @e",
                        new { t = DateTime.Now.AddDays(30), ucret = proUcreti, e = email });

                    await _adminHub.Clients.All.SendAsync("AdminiUyar");

                    return Json(new { success = true, message = "Hayırlı olsun! Artık PRO üyesin. 🎉" });
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

            // Şifreyi hash'leyerek kaydet
            string hashedSifre = PasswordHasher.HashPassword(yeniSifre);
            db.Execute("UPDATE Kullanicilar SET Sifre = @s WHERE Email = @e",
                new { s = hashedSifre, e = (string)rec.KullaniciEmail });

            db.Execute("UPDATE SifreSifirlamaTokenlari SET Kullanildi = 1 WHERE Id = @id",
                new { id = (int)rec.Id });

            TempData["Basari"] = "Şifre güncellendi. Giriş yapabilirsin.";
            return RedirectToAction("Giris");
        }
        private void MailGonder(string toEmail, string subject, string body)
        {
            var emailSettings = _configuration.GetSection("EmailSettings");
            var smtpSettings = _configuration.GetSection("Smtp");

            bool emailSettingsHazir = !string.IsNullOrWhiteSpace(emailSettings["Mail"]) &&
                                      !string.IsNullOrWhiteSpace(emailSettings["Password"]);
            bool smtpSettingsHazir = !string.IsNullOrWhiteSpace(smtpSettings["User"]) &&
                                     !string.IsNullOrWhiteSpace(smtpSettings["Pass"]);

            string host;
            int port;
            string gonderenMail;
            string kullanici;
            string uygulamaSifresi;
            string gonderenAd;
            bool enableSsl;

            if (emailSettingsHazir)
            {
                host = emailSettings["Host"] ?? "smtp.gmail.com";
                port = int.TryParse(emailSettings["Port"], out var p) ? p : 587;
                gonderenMail = emailSettings["Mail"]!;
                kullanici = emailSettings["Mail"]!;
                uygulamaSifresi = emailSettings["Password"]!;
                gonderenAd = smtpSettings["FromName"] ?? "Kartist";
                enableSsl = !bool.TryParse(smtpSettings["EnableSsl"], out var sslValue) || sslValue;
            }
            else if (smtpSettingsHazir)
            {
                host = smtpSettings["Host"] ?? "smtp.gmail.com";
                port = int.TryParse(smtpSettings["Port"], out var p) ? p : 587;
                gonderenMail = smtpSettings["From"] ?? smtpSettings["User"]!;
                kullanici = smtpSettings["User"]!;
                uygulamaSifresi = smtpSettings["Pass"]!;
                gonderenAd = smtpSettings["FromName"] ?? "Kartist";
                enableSsl = !bool.TryParse(smtpSettings["EnableSsl"], out var sslValue) || sslValue;
            }
            else
            {
                throw new Exception("SMTP ayarlari eksik. appsettings.json icinde EmailSettings veya Smtp alanlarini doldurun.");
            }

            if (string.IsNullOrWhiteSpace(gonderenMail) || string.IsNullOrWhiteSpace(kullanici) || string.IsNullOrWhiteSpace(uygulamaSifresi))
            {
                throw new Exception("SMTP ayarlari eksik. appsettings.json icinde EmailSettings/Smtp alanlarini doldurun.");
            }

            try
            {
                using (var smtp = new SmtpClient(host, port))
                {
                    smtp.EnableSsl = enableSsl;
                    smtp.DeliveryMethod = SmtpDeliveryMethod.Network;
                    smtp.UseDefaultCredentials = false;
                    smtp.Credentials = new NetworkCredential(kullanici, uygulamaSifresi);

                    var mail = new MailMessage
                    {
                        From = new MailAddress(gonderenMail, gonderenAd),
                        Subject = subject,
                        Body = body,
                        IsBodyHtml = true,
                        BodyEncoding = Encoding.UTF8,
                        SubjectEncoding = Encoding.UTF8
                    };

                    mail.To.Add(toEmail);
                    smtp.Send(mail);
                }
            }
            catch (Exception ex)
            {
                throw new Exception("SMTP Hatasi: " + ex.Message);
            }
        }

        private string BuildIkiFaktorMailHtml(string kod, string email)
        {
            var maskedEmail = MaskEmail(email);

            return $@"
<div style='margin:0;padding:0;background:#05070f;font-family:Segoe UI,Arial,sans-serif;'>
  <div style='max-width:620px;margin:0 auto;padding:32px 16px;'>
    <div style='border-radius:20px;overflow:hidden;background:linear-gradient(145deg,#0b1020,#12172a);border:1px solid #29314f;box-shadow:0 20px 60px rgba(0,0,0,.45);'>
      <div style='padding:26px 28px;background:radial-gradient(circle at 10% 10%,rgba(198,255,0,.14),transparent 45%),radial-gradient(circle at 90% 10%,rgba(0,209,255,.12),transparent 40%);border-bottom:1px solid #2a355c;'>
        <div style='font-size:28px;font-weight:800;letter-spacing:.3px;color:#fff;'>KART<span style='color:#c6ff00;'>IST</span></div>
        <div style='margin-top:6px;color:#8fa2d6;font-size:13px;'>Guvenli giris bildirimi</div>
      </div>

      <div style='padding:30px 28px 22px 28px;color:#dfe6ff;'>
        <h2 style='margin:0 0 10px 0;font-size:24px;color:#ffffff;'>Iki Faktorlu Giris Kodu</h2>
        <p style='margin:0 0 20px 0;line-height:1.6;color:#9fb0dc;'>Hesabiniza giris yapabilmek icin asagidaki kodu kullanin. Bu kod sadece 5 dakika boyunca gecerlidir.</p>

        <div style='text-align:center;margin:18px 0 22px 0;'>
          <div style='display:inline-block;padding:18px 26px;border-radius:14px;background:#0a0f1f;border:2px solid #c6ff00;color:#c6ff00;font-size:40px;font-weight:800;letter-spacing:10px;line-height:1;'>{WebUtility.HtmlEncode(kod)}</div>
        </div>

        <div style='background:#0b1228;border:1px solid #243056;border-radius:12px;padding:14px 16px;color:#9fb0dc;font-size:13px;line-height:1.5;'>
          <div><strong style='color:#d9e3ff;'>Hedef hesap:</strong> {WebUtility.HtmlEncode(maskedEmail)}</div>
          <div><strong style='color:#d9e3ff;'>Sure:</strong> 5 dakika</div>
          <div><strong style='color:#d9e3ff;'>Uyari:</strong> Bu islemi siz yapmadiysaniz sifrenizi degistirin.</div>
        </div>
      </div>

      <div style='padding:16px 28px;background:#080c1a;border-top:1px solid #242f53;color:#7f92c4;font-size:12px;'>
        Bu e-posta otomatik olarak gonderildi. Lutfen yanitlamayin.
      </div>
    </div>
  </div>
</div>";
        }

        private string MaskEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email) || !email.Contains("@")) return "gizli";

            var parts = email.Split('@');
            var local = parts[0];
            var domain = parts[1];

            if (local.Length <= 2)
            {
                return $"{local[0]}*@{domain}";
            }

            return $"{local[..2]}***@{domain}";
        }
        [HttpGet]
        public IActionResult IkiFactorDogrula()
        {
            string email = TempData["2FA_Email"] as string;
            if (string.IsNullOrEmpty(email)) return RedirectToAction("Giris");
            ViewBag.Email = email;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> IkiFactorDogrula(string email, string kod)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(kod))
            {
                ViewBag.Hata = "Lutfen dogrulama kodunu girin.";
                ViewBag.Email = email;
                return View();
            }

            using (var db = new SqlConnection(_baglanti))
            {
                var gecerliKod = db.QueryFirstOrDefault<dynamic>(
                    @"SELECT TOP 1 * FROM IkiFactorKodlari 
                      WHERE KullaniciEmail = @mail AND Kod = @kod AND Kullanildi = 0 AND BitisTarihi > GETUTCDATE()
                      ORDER BY Id DESC",
                    new { mail = email, kod = kod });

                if (gecerliKod == null)
                {
                    var toplamDeneme = db.ExecuteScalar<int>(
                        @"SELECT COUNT(*) FROM IkiFactorKodlari 
                          WHERE KullaniciEmail = @mail AND Kullanildi = 0 AND BitisTarihi > GETUTCDATE()",
                        new { mail = email });

                    if (toplamDeneme == 0)
                    {
                        ViewBag.Hata = "Kodun suresi dolmus. Lutfen tekrar giris yapin.";
                        return View("Giris");
                    }

                    ViewBag.Hata = "Gecersiz kod. Lutfen tekrar deneyin.";
                    ViewBag.Email = email;
                    return View();
                }

                db.Execute("UPDATE IkiFactorKodlari SET Kullanildi = 1 WHERE Id = @id", new { id = (int)gecerliKod.Id });

                var user = db.QueryFirstOrDefault("SELECT AdSoyad, Email FROM Kullanicilar WHERE Email = @e", new { e = email });
                if (user == null) return RedirectToAction("Giris");

                var claims = new List<Claim> { new Claim(ClaimTypes.Name, (string)user.AdSoyad), new Claim(ClaimTypes.Email, (string)user.Email) };
                await HttpContext.SignInAsync("KartistCookie", new ClaimsPrincipal(new ClaimsIdentity(claims, "KartistCookie")));
                TempData["Mesaj"] = $"Hos geldin {user.AdSoyad}!";
                TempData["Tur"] = "success";
                return RedirectToAction("Index", "Home");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult IkiFactorKoduTekrarGonder(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return Json(new { success = false, message = "E-posta bilgisi bulunamadi." });

            using (var db = new SqlConnection(_baglanti))
            {
                var user = db.QueryFirstOrDefault<dynamic>(
                    "SELECT Email, IkiFactorAktif FROM Kullanicilar WHERE Email = @e",
                    new { e = email });

                if (user == null)
                    return Json(new { success = false, message = "Kullanici bulunamadi." });

                bool ikiFaktorAktif = user.IkiFactorAktif != null && (bool)user.IkiFactorAktif;
                if (!ikiFaktorAktif)
                    return Json(new { success = false, message = "Iki faktor dogrulama bu hesapta aktif degil." });

                var sonKod = db.QueryFirstOrDefault<dynamic>(@"SELECT TOP 1 Id, OlusturmaTarihi
                                                           FROM IkiFactorKodlari
                                                           WHERE KullaniciEmail = @mail
                                                           ORDER BY Id DESC", new { mail = email });

                if (sonKod == null)
                {
                    return Json(new { success = false, message = "Oturum suresi dolmus olabilir. Lutfen tekrar giris yapin." });
                }

                DateTime sonOlusturma = sonKod.OlusturmaTarihi == null ? DateTime.UtcNow.AddMinutes(-5) : (DateTime)sonKod.OlusturmaTarihi;
                int gecenSaniye = (int)(DateTime.UtcNow - sonOlusturma).TotalSeconds;
                int bekleme = 60 - gecenSaniye;

                if (bekleme > 0)
                {
                    return Json(new { success = false, message = $"Lutfen {bekleme} saniye bekleyin.", waitSeconds = bekleme });
                }

                db.Execute("UPDATE IkiFactorKodlari SET Kullanildi = 1 WHERE KullaniciEmail = @mail AND Kullanildi = 0", new { mail = email });

                string kod = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
                db.Execute(@"INSERT INTO IkiFactorKodlari (KullaniciEmail, Kod, BitisTarihi)
                             VALUES (@mail, @kod, @bitis)",
                    new { mail = email, kod = kod, bitis = DateTime.UtcNow.AddMinutes(5) });

                try
                {
                    string kodHtml = BuildIkiFaktorMailHtml(kod, email);
                    MailGonder(email, "Kartist - Iki Faktorlu Giris Kodunuz", kodHtml);
                }
                catch
                {
                    return Json(new { success = false, message = "Kod olusturuldu fakat e-posta gonderilemedi. Lutfen tekrar deneyin." });
                }

                return Json(new { success = true, message = "Yeni dogrulama kodu e-posta adresinize gonderildi.", waitSeconds = 60 });
            }
        }
        [HttpPost]
        public IActionResult IkiFactorAktifEt()
        {
            if (!User.Identity.IsAuthenticated) return Json(new { success = false, message = "Giris yapmalsin." });
            string email = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;

            using (var db = new SqlConnection(_baglanti))
            {
                db.Execute("UPDATE Kullanicilar SET IkiFactorAktif = 1 WHERE Email = @e", new { e = email });
            }
            return Json(new { success = true, message = "Iki faktorlu dogrulama aktif edildi." });
        }

        [HttpPost]
        public IActionResult IkiFactorKapat()
        {
            if (!User.Identity.IsAuthenticated) return Json(new { success = false, message = "Giris yapmalsin." });
            string email = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;

            using (var db = new SqlConnection(_baglanti))
            {
                db.Execute("UPDATE Kullanicilar SET IkiFactorAktif = 0 WHERE Email = @e", new { e = email });
            }
            return Json(new { success = true, message = "Iki faktorlu dogrulama kapatildi." });
        }

        [HttpPost]
        public IActionResult SifreDegistir(string mevcutSifre, string yeniSifre)
        {
            if (!User.Identity.IsAuthenticated) return Json(new { success = false, message = "Giris yapmalsin." });

            if (string.IsNullOrWhiteSpace(mevcutSifre) || string.IsNullOrWhiteSpace(yeniSifre))
                return Json(new { success = false, message = "Tum alanlar zorunludur." });

            if (yeniSifre.Length < 6 || yeniSifre.Length > 50)
                return Json(new { success = false, message = "Yeni sifre 6-50 karakter arasinda olmali." });

            string email = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;

            using (var db = new SqlConnection(_baglanti))
            {
                var user = db.QueryFirstOrDefault("SELECT Sifre FROM Kullanicilar WHERE Email = @e", new { e = email });
                if (user == null) return Json(new { success = false, message = "Kullanici bulunamadi." });

                string dbSifre = (string)user.Sifre;
                bool dogruMu = PasswordHasher.IsHashed(dbSifre)
                    ? PasswordHasher.VerifyPassword(mevcutSifre, dbSifre)
                    : (dbSifre == mevcutSifre);

                if (!dogruMu) return Json(new { success = false, message = "Mevcut sifre hatali!" });

                string hashed = PasswordHasher.HashPassword(yeniSifre);
                db.Execute("UPDATE Kullanicilar SET Sifre = @s WHERE Email = @e", new { s = hashed, e = email });
            }
            return Json(new { success = true, message = "Sifreniz basariyla guncellendi." });
        }

        [HttpPost]
        public IActionResult BiyografiGuncelle(string biyografi)
        {
            if (!User.Identity.IsAuthenticated) return Json(new { success = false, message = "Giris yapmalsin." });
            string email = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;

            biyografi = InputValidator.SanitizeHtml(biyografi ?? "");
            if (biyografi.Length > 500) biyografi = biyografi.Substring(0, 500);

            using (var db = new SqlConnection(_baglanti))
            {
                db.Execute("UPDATE Kullanicilar SET Biyografi = @b WHERE Email = @e", new { b = biyografi, e = email });
            }
            return Json(new { success = true, message = "Biyografi guncellendi." });
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











