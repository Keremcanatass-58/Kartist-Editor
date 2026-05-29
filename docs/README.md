# 📂 Kartist — Proje Dokümantasyonu

Haftalık geliştirme raporları, kullanım rehberi ve teknik detaylara aşağıdan ulaşabilirsiniz.

## 🏁 Final Raporu (Güncel)

| Doküman | PDF | HTML | Açıklama |
|---------|-----|------|----------|
| 🏆 **Final Proje Raporu** | [PDF](SAT_FinalRaporu_Keremcan_Atas_247017033.pdf) | [HTML](SAT_FinalRaporu_Keremcan_Atas_247017033.html) | Hafta 1–12 / Sprint 1–5 + Final detaylı rapor (14 sayfa, ekran görüntülü, kod örnekli, WebRTC/SignalR ağ teknolojisi bölümü) |
| 📖 **Kullanım Rehberi** | [PDF](Kartist_Kullanim_Rehberi.pdf) | [HTML](Kartist_Kullanim_Rehberi.html) | Adım adım test kılavuzu + değerlendirme giriş bilgileri (6 sayfa) |

> 💡 Hem hazır **PDF** hem de A4 baskıya uygun **HTML** sürümleri mevcuttur (HTML'i tarayıcıda Yazdır → PDF ile de kaydedebilirsiniz).

## 📄 Vize Raporu

| Doküman | Açıklama |
|---------|----------|
| [📋 Vize Raporu (HTML)](SAT_VizeRaporu_Keremcan_Atas_247017033.html) | Sprint 1–3 detaylı rapor (10 sayfa, resimli, kod örnekli) |
| [📋 Vize Raporu (PDF)](../KARTIST%20v2.0%20-%20Vize%20Proje%20Raporu.pdf) | PDF sürümü |

## 📸 Ekran Görüntüleri

| Görsel | Açıklama |
|--------|----------|
| [Ana Sayfa](screenshots/01-anasayfa.png) | Kartist koleksiyon / dashboard |
| [Giriş](screenshots/02-giris.png) | Login + Google OAuth |
| [Kayıt](screenshots/03-kayit.png) | Üye olma ekranı |
| [Editör](screenshots/05-editor.png) | Fabric.js tasarım tuvali |
| [AI Studio](screenshots/06-editor-ai-akisi.png) | AI ile tasarım üretim akışı |
| [Sosyal Akış](screenshots/09-feed.png) | Feed: beğeni, yorum, takip |
| [Profil](screenshots/10-profil.png) | Profil + rozet + XP |
| [Keşfet](screenshots/11-kesfet.png) | Topluluk tasarımları |
| [Liderlik](screenshots/12-liderlik.png) | XP liderlik tablosu |
| [Mesajlar](screenshots/13-mesajlar.png) | Direkt mesajlaşma (DM) |
| [İstatistikler](screenshots/15-istatistikler.png) | İstatistik panosu |
| [Yarışmalar](screenshots/16-yarismalar.png) | AI jürili tasarım yarışmaları |
| [Canlı Yayın](screenshots/17-canli-yayin.png) | WebRTC P2P canlı yayın |
| [Düellolar](screenshots/18-duello.png) | Tasarım düelloları |

## 🏗️ Sprint Geçmişi

### Sprint 1 — Güvenlik Altyapısı
Security Headers (CSP), BCrypt, global CSRF, Rate Limiting, SQL Injection önleme

### Sprint 2 — 2FA, Dashboard & CI/CD
İki Faktörlü Doğrulama (e-posta OTP), Google OAuth, Dashboard tasarımı, GitHub Actions

### Sprint 3 — AI Entegrasyonu & Canvas
Gemini/Groq LLM, AI görsel üretimi + fallback zinciri, Fabric.js canvas, JSON state kayıt

### Sprint 4 — Sosyal Ağ
Feed (gönderi CRUD), beğeni, iç içe yorum, takip, hikâyeler, AI içerik moderasyonu, mesajlaşma

### Sprint 5 — Güvenlik Sertleştirme & Gamification
HMAC-SHA256 imzalı deploy, magic-byte dosya doğrulama, çerez sertleştirme, CSPRNG 2FA;
XP/seviye/rozet, liderlik, günlük görevler

### Final — Canlı Yayın & Yarışmalar
WebRTC P2P canlı yayın + SignalR sinyalizasyon, tasarım düelloları, AI jürili yarışmalar
(yaşam döngüsü servisi), 3 varyasyonlu AI Studio
