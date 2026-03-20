<p align="center">
  <img src="wwwroot/img/kartist-logo.svg" alt="Kartist Logo" width="200"/>
</p>

<h1 align="center">⚡ KARTIST</h1>

<p align="center">
  <strong>"Tasarımın Geleceği, Senin Hayalin."</strong><br>
  Yapay Zeka Destekli Modern Tasarım Stüdyosu
</p>

<p align="center">
  <a href="https://github.com/Keremcanatass-58/Kartist-Editor/actions/workflows/dotnet.yml">
    <img src="https://github.com/Keremcanatass-58/Kartist-Editor/actions/workflows/dotnet.yml/badge.svg" alt="Build Status"/>
  </a>
  <img src="https://img.shields.io/badge/.NET-9.0-purple" alt=".NET 9.0"/>
  <img src="https://img.shields.io/badge/Fabric.js-Canvas-orange" alt="Fabric.js"/>
  <img src="https://img.shields.io/badge/Groq-AI-blue" alt="Groq AI"/>
  <a href="https://kartistt.com.tr">
    <img src="https://img.shields.io/badge/Demo-Canlı-brightgreen" alt="Live Demo"/>
  </a>
</p>

---

## 🌟 Proje Hakkında

KARTIST, karmaşık tasarım süreçlerini herkes için erişilebilir kılan, ASP.NET Core altyapısıyla geliştirilmiş yeni nesil bir tasarım editörüdür. Kullanıcılar; kartvizit, davetiye ve sosyal medya içeriklerini yapay zeka desteğiyle saniyeler içinde profesyonel standartlarda oluşturabilirler.

🔗 **[Canlı Demoyu Görüntüle »](https://kartistt.com.tr)**

---

## ✨ Öne Çıkan Özellikler

| Özellik | Açıklama |
|---------|----------|
| 🤖 **AI Design Engine** | Groq API entegrasyonu ile akıllı tasarım önerileri ve içerik üretimi |
| 🎨 **Gelişmiş Editör** | Fabric.js altyapısı sayesinde pürüzsüz nesne yönetimi ve sürükle-bırak deneyimi |
| 🎭 **Katman Sistemi** | Profesyonel tasarım programlarındaki gibi katman bazlı çalışma imkanı |
| 🔐 **İki Faktörlü Doğrulama** | E-posta tabanlı 2FA ile güçlendirilmiş hesap güvenliği |
| 🎵 **Creative Beats** | Tasarım yaparken eşlik eden entegre Spotify müzik deneyimi |
| 📱 **Tam Duyarlı** | Mobil, tablet ve masaüstü cihazlarda kusursuz kullanıcı arayüzü |
| ⚡ **Hızlı ve Güvenli** | BCrypt şifre hashleme, CSRF koruması, güvenlik header'ları |

---

## 🏗️ Teknoloji Yığını

```
Backend:    ASP.NET Core 9.0 (MVC + Razor Views)
Veritabanı: SQL Server + Dapper ORM
Frontend:   HTML5, CSS3, JavaScript, Fabric.js
AI:         Groq API (LLM entegrasyonu)
Auth:       BCrypt.Net, Google OAuth 2.0, 2FA (E-posta)
Ödeme:      Iyzipay entegrasyonu
CI/CD:      GitHub Actions
```

---

## 📋 Sprint Geçmişi

### Sprint 1 — Güvenlik Altyapısı
- ✅ Security Headers Middleware aktivasyonu
- ✅ Şifre hashleme (BCrypt) — düz metin şifrelerden geçiş
- ✅ InputValidator güçlendirme
- ✅ Global CSRF koruması
- ✅ Rate Limiting aktif edildi
- ✅ Veritabanı migration (yeni tablolar & alanlar)

### Sprint 2 — 2FA, Dashboard & CI/CD
- ✅ İki Faktörlü Doğrulama (2FA) sistemi
- ✅ Brute-force koruması (hesap kilitleme)
- ✅ Dashboard/Profil sayfası yeniden tasarımı
- ✅ Tasarım editörü geliştirmeleri
- ✅ GitHub Actions CI/CD pipeline
- ✅ UI/UX iyileştirmeleri ve yeni görseller

---

## 🚀 Kurulum

### Gereksinimler
- .NET 9.0 SDK
- SQL Server
- Node.js (opsiyonel, frontend geliştirme için)

### Hızlı Başlangıç

```bash
# Projeyi klonla
git clone https://github.com/Keremcanatass-58/Kartist-Editor.git
cd Kartist-Editor

# Bağımlılıkları yükle
dotnet restore

# appsettings.Development.json dosyasını oluştur ve secret'ları ekle
# (appsettings.json'daki placeholder'ları kendi değerlerinle değiştir)

# Veritabanı migration'ını çalıştır
# migration.sql dosyasını SQL Server'da çalıştır

# Uygulamayı başlat
dotnet run
```

---

## 🚀 SEO & Performans Stratejisi

Arama motorlarında görünürlük ve kullanıcı deneyimi için şu teknikler uygulanmıştır:

1. **Semantic SEO**: Sayfa başlıkları `<h1>` ve `<h2>` hiyerarşisinde yapılandırıldı
2. **Glassmorphism UI**: Modern ve şık bir görünüm için yüksek performanslı CSS efektleri
3. **Hızlı İletişim**: AJAX tabanlı SweetAlert2 bildirimleri ve dinamik modal sistemi
4. **Open Graph**: Sosyal medya paylaşımları için optimize edilmiş yapı

---

## 🤝 İletişim & Geliştirici

**Keremcan Ataş** — Full Stack Developer

| Platform | Link |
|----------|------|
| 🌐 Web | [kartistt.com.tr](https://kartistt.com.tr) |
| 💼 LinkedIn | [linkedin.com/in/keremcan-ataş](https://www.linkedin.com/in/keremcan-ata%C5%9F) |
| 🐙 GitHub | [github.com/Keremcanatass-58](https://github.com/Keremcanatass-58) |
| 📸 Instagram | [@x.keremcan6z](https://www.instagram.com/x.keremcan6z/) |

---

<p align="center">
  Bu proje akademik standartlarda ve modern web trendleri gözetilerek geliştirilmiştir.
</p>
