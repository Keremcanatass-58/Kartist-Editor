# Kartist Güvenlik Sprint — Devam Notları

**Tarih:** 2026-04-21
**Branch:** main
**Durum:** 3 security fix uygulandı, build temiz (0 uyarı/0 hata), **commit'ler YAPILMADI** — patch-mode staging planı onay bekliyor.

---

## Bu oturumda yapılanlar (özet)

### 1. `/api/deploy` hardcoded fallback secret kaldırıldı ✅
**Sorun:** Deploy endpoint, HMAC imzası yerine `"secret=kartist-deploy-secret-2026"` form alanı ile de kabul ediyordu. Değer hem `Program.cs`'te hem CI workflow'unda plaintext. Repo'yu gören herkes prod'a zip yükleyip webroot'un üzerine açabilirdi = RCE.

**Yapıldı:**
- `Program.cs` — `IsDeployRequestAuthorized` fallback bloğu silindi, sadece HMAC-SHA256 imza yolu kaldı. `async Task<bool>` → `bool` (artık `await` yok). Çağrı satırı da güncellendi.
- `.github/workflows/dotnet.yml` — curl çağrısından `-F "secret=kartist-deploy-secret-2026" \` satırı silindi. HMAC imza step'i (`DEPLOY_SECRET` secret'ı) olduğu gibi duruyor.

### 2. HomeController Gmail App Password kaldırıldı ✅
**Sorun:** `HomeController.MailGonder` satır 506-507'de `kartistt.official@gmail.com` + `"dvab taay cpba xunv "` App Password hardcoded. Iletisim formundaki alıcı adresi de hardcoded.

**Yapıldı:**
- `Controllers/HomeController.cs` — `MailGonder` gövdesi `AccountController.MailGonder` ile aynı dual-section pattern'e çevrildi (EmailSettings öncelikli, düşerse Smtp; ikisi de boşsa Exception). `try{}catch{throw;}` anlamsız sarıcısı kaldırıldı.
- `Iletisim` action'ındaki alıcı: hardcoded yerine `EmailSettings:ContactInbox` → `EmailSettings:Mail` → `Smtp:From` → `Smtp:User` fallback zinciri.
- `appsettings.json` — `EmailSettings` bölümüne `"ContactInbox": "your-email@gmail.com"` placeholder eklendi.

**⚠️ Hâlâ yapılması gereken (repo dışı):**
- Google Account → Security → App passwords'ten `dvab taay cpba xunv` değerini **revoke et**, yeni bir App Password üret.
- Yeni değeri hem `appsettings.Development.json` (local) hem GitHub `APPSETTINGS_PRODUCTION_JSON` secret'ındaki `EmailSettings.Password` / `Smtp.Pass` alanlarında güncelle.
- Eski App Password git history'de (`d2fd9ec` ve öncesi) hâlâ görünür durumda — rotate etmeden filter-repo purge yapmak anlamsız.

### 3. AdminController güvenlik sertleştirmesi ✅
**Sorunlar:**
- `AdminKontrol()` catch bloğu `return true` — DB hatasında fail-open.
- `KrediYukle`, `UyelikDegistir`, `Ekle`, `Sil`, `Onayla`, `Reddet` hiçbirinde yetki kontrolü yoktu.
- `Sil`, `Onayla`, `Reddet` GET endpoint'leriydi — `<img src=/Admin/Sil/42>` gibi CSRF saldırılarına açık.

**Yapıldı:**
- `Controllers/AdminController.cs`:
  - `AdminKontrol()` catch `return true` → `return false` (fail-closed).
  - Yeni `AdminYetkili()` helper: Session `AdminOturumu` ∨ `AdminKontrol()` — iki admin giriş yolunu da destekliyor.
  - 6 sensitive action'a guard + `[HttpPost] [ValidateAntiForgeryToken]` eklendi.
  - `Sil`/`Onayla`/`Reddet` artık POST only.
- `Views/Admin/Panel.cshtml`:
  - Onayla/Reddet/Sil GET `<a>` linkleri → inline `<form method="post">` + `@Html.AntiForgeryToken()`.
  - `krediGuncelle` / `uyelikDegistir` AJAX fonksiyonlarına `__RequestVerificationToken` eklendi. (Yan fayda: bu iki özellik global CSRF filtresi yüzünden sessizce 400 dönmekteydi, şimdi tekrar çalışır durumda.)

---

## Nerede kaldık — ÖNEMLİ

Kullanıcı 3 ayrı commit istedi: deploy → gmail → admin sırasıyla. `git status` incelemesinde şunu keşfettim: bu oturumdan önce de working tree'de birkaç dosyada ilişkisiz değişiklikler bekliyordu. Body-committed yaparsam security commit'lerinin içine alakasız kodlar karışır.

### Dosya bazında karışıklık tablosu

| Dosya | Durum | Notlar |
|---|---|---|
| `Program.cs` | **KARIŞIK** | Deploy fix (benim) + iki cookie `SecurePolicy = Always` sertleştirme (oturum öncesi) |
| `.github/workflows/dotnet.yml` | **ÇOK KARIŞIK** | 1 satır `-F "secret=..."` silme (benim) + büyük workflow refactor'ü: quality-gate job silinmiş, cache adımı yok, healthcheck bölünmüş, retention 7→30, job summary silinmiş (oturum öncesi) |
| `appsettings.json` | **KARIŞIK** | ContactInbox ekleme (benim) + `Ai.TimeoutSeconds 25→60` (oturum öncesi) |
| `Controllers/HomeController.cs` | ✅ Temiz | Sadece gmail fix |
| `Controllers/AdminController.cs` | ✅ Temiz | Sadece admin fix |
| `Views/Admin/Panel.cshtml` | ✅ Temiz | Sadece admin panel fix |

### Bu oturumda hiç dokunulmamış, working tree'de olan ilişkisiz değişiklikler
- `Controllers/SocialController.cs` (721 satır diff)
- `Views/Social/Feed.cshtml` (1193 satır diff)
- `Views/Social/Profil.cshtml` (608 satır diff)
- `err.txt`, `out.txt` (untracked dev log çıktıları)
- `CLAUDE.md` (bu oturumda oluşturduğum init dokümanı, sen commit istemedin)

### Önerdiğim commit planı (ONAY BEKLİYOR)

`git add -p` (patch mode) ile sadece security hunk'larını stage'liyoruz:

**Commit 1 — Deploy endpoint**
- Stage: `Program.cs`'ten yalnız `IsDeployRequestAuthorized` + çağrı hunk'ları; `.github/workflows/dotnet.yml`'den yalnız `-F "secret=..."` silme hunk'u.
- Mesaj şablonu: "Remove hardcoded fallback secret from /api/deploy endpoint" (tam metin aşağıda)

**Commit 2 — Gmail password**
- Stage: `Controllers/HomeController.cs` tümü; `appsettings.json`'dan yalnız ContactInbox hunk'u.
- Mesaj: "Read SMTP credentials and contact inbox from configuration"

**Commit 3 — Admin controller**
- Stage: `Controllers/AdminController.cs` + `Views/Admin/Panel.cshtml` tümü.
- Mesaj: "Harden AdminController with authorization and CSRF protection"

Her commit'e `Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>` trailer'ı gelecek. Push yok.

### Tam commit mesajları (referans için)

**1. Deploy:**
```
Remove hardcoded fallback secret from /api/deploy endpoint

The deploy endpoint accepted either a valid HMAC signature OR a
hardcoded form field "secret=kartist-deploy-secret-2026", which
was readable in both Program.cs and the CI workflow. Anyone who
saw the repository could trigger an arbitrary zip upload that
gets extracted over the production webroot.

Dropped the fallback branch entirely; only the HMAC-SHA256
signature path over X-Kartist-Timestamp remains (constant-time
compared, bounded by SignatureToleranceSeconds). The CI curl
call no longer sends the form field — it relies solely on the
existing signed-webhook step driven by the DEPLOY_SECRET
GitHub secret.
```

**2. Gmail:**
```
Read SMTP credentials and contact inbox from configuration

HomeController.MailGonder had the Gmail sender address and an
App Password hardcoded in source, and the Iletisim action
hardcoded the recipient inbox. Anyone with repo access could
use those credentials to send mail as the project's Gmail
account.

Rewrote MailGonder to mirror the existing pattern in
AccountController: prefer EmailSettings (Mail/Password), fall
back to Smtp (User/Pass/From/FromName/EnableSsl), and throw if
neither section is populated. Iletisim now reads the recipient
from EmailSettings:ContactInbox with sender addresses as
fallbacks. appsettings.json ships a placeholder ContactInbox
alongside the existing placeholders; real values come from the
APPSETTINGS_PRODUCTION_JSON secret in CI.

NOTE: the leaked App Password must be revoked in Google Account
and replaced in both the local appsettings.Development.json and
the APPSETTINGS_PRODUCTION_JSON GitHub secret. The string is
still present in earlier commits.
```

**3. Admin:**
```
Harden AdminController with authorization and CSRF protection

Every state-changing admin action was reachable by any
unauthenticated or non-admin visitor. The AdminKontrol helper
also failed open: a transient DB error let callers through as
if they were admin. Sil / Onayla / Reddet were GET endpoints,
triggerable via a third-party image tag or link.

- AdminKontrol now returns false on exception (fail-closed).
- Added AdminYetkili() helper combining the AdminOturumu
  session key and AdminKontrol(), preserving both the admin
  login page and the claim-based admin path.
- Guarded KrediYukle, UyelikDegistir, Ekle, Sil, Onayla,
  Reddet with AdminYetkili() and marked them
  [HttpPost] + [ValidateAntiForgeryToken] (Sil/Onayla/Reddet
  converted from GET to POST).
- Views/Admin/Panel.cshtml: replaced the GET anchor tags for
  Sil / Onayla / Reddet with inline form POSTs that include
  @Html.AntiForgeryToken(), keeping the existing confirm()
  dialog for destructive actions. The AJAX kredi and uyelik
  mutations now attach __RequestVerificationToken, which
  also fixes a latent bug: the global antiforgery filter was
  rejecting those requests because the token was never sent.
```

---

## Yarın ilk yapılacak

1. **Bu dosyayı aç ve "Önerdiğim commit planı" bölümündeki patch-mode staging planını Claude'a onayla** — ya "plan aynen devam et" de, ya da değişiklik iste (örn. "Program.cs'teki cookie SecurePolicy hunk'ını da Commit 1'e ekle" gibi).
2. Claude commit'leri atar, sonrasında `git log --oneline -5` ile doğrular.

## Kritik hatırlatmalar (bu sprint'in içeriğinden bağımsız)

- **DEPLOY_SECRET senkron mu?** Bir sonraki deploy'dan önce GitHub repo secrets'ındaki `DEPLOY_SECRET` ile prod'taki `Deployment:Secret` (yani `APPSETTINGS_PRODUCTION_JSON` içindeki değer) **birebir aynı** olmalı. Aksi halde imza doğrulaması başarısız, deploy 401 dönecek.
- **Gmail App Password revoke** — yukarıda detayı var. Rotate etmeden filter-repo işe yaramaz.
- **Deploy secret rotate** — `kartist-deploy-secret-2026` string'i de history'de. Yeni bir HMAC secret üretip GitHub `DEPLOY_SECRET`'ında ve `APPSETTINGS_PRODUCTION_JSON.Deployment.Secret` alanında güncelle. Sonra istersen `git filter-repo` ile eski string'leri history'den temizle.

---

## Bu sprint'te ele alınmayan, gelecek sprint adayı bulgular

Önceki güvenlik taramasından kalan, hâlâ açık kritik bulgular (öncelik sırasıyla):

1. **`AccountController.cs:221-228`** — Plaintext şifre fallback'i (BCrypt başarısızsa düz metin karşılaştırma). BCrypt migrasyonu eksik kullanıcılara bir defa verilen geçici kapı; production'da hâlâ açık mı bilmiyoruz.
2. **`AccountController.cs:278`** — `new Random().Next(100000, 999999)` ile 2FA kodu. Kriptografik güvenli değil; `RandomNumberGenerator` kullanılmalı.
3. **`AccountController.SifremiUnuttum`** — User enumeration (kullanıcı var/yok yanıtları farklı). Koddaki yorum bile bunu kabul ediyor.
4. **`AccountController.IkiFactorKapat`** — Yeniden kimlik doğrulama istemiyor; çalıntı cookie ile 2FA kapatılabilir.
5. **`AdminController.Login POST`** — `Sifre = @s` ile `Yoneticiler` tablosuna plaintext karşılaştırma. BCrypt migrasyonu gerekli. Admin tablosu küçük olduğu için yapılabilir ama mevcut admin şifrelerini resetlemek lazım.
6. **`SocialService.cs`** — `gorsel.ContentType.Split('/').Last()` kullanıcı-kontrollü dosya uzantısı üretiyor. Whitelist + magic byte doğrulaması lazım.
7. **`wwwroot/js/kartist-social.js`** — `innerHTML = ...${b.Mesaj}...` ile bildirim render'ı. DOM-based stored XSS. `textContent` veya güvenli template engine'e çevir.
8. **`Middleware/SecurityHeadersMiddleware.cs`** — CSP `unsafe-inline` + `unsafe-eval` içeriyor. `/uploads/` path'i tamamen skip'leniyor (yüklenen HTML dosyası siteyi kendi origin'inden çalıştırabilir). Nonce-based CSP'ye geçiş + uploads için ayrı header politikası.
9. **`Helpers/InputValidator.cs`** — `IsValidInput` blacklist keyword kontrolü. False security, kaldırmak veya allowlist'e çevirmek lazım. Mevcut kullanımları parameterization ile zaten korunmuş olmalı.
10. **`HomeController.Iletisim`** — Anonim endpoint (login gerektirmiyor), mesaj gövdesi HTML template'ine interpolate ediliyor. HTML injection mümkün, en azından `HtmlEncode` gerekli. Ayrıca `GetAiDebugLog` action'ı geçici log dosyasını public expose ediyor.

---

## Dosya konumu ve .gitignore

Bu dosya (`DEVAM_NOTLARI.md`) şu anda repo'nun root'unda ve **untracked**. Commit'e girmemesi gerektiğini düşünüyorum; istersen `.gitignore`'a `DEVAM_NOTLARI.md` satırı eklenebilir.
