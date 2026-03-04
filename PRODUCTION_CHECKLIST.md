# ✅ Production Deployment - Son Kontrol Listesi

Bu dosya, siteyi yayınlamadan önce kontrol edilmesi gereken tüm maddeleri içerir.

## 🔐 GÜVENLİK KONTROLLERİ

### Environment Variables
- [ ] `ConnectionStrings__DefaultConnection` ayarlandı
- [ ] `EmailSettings__Mail` ayarlandı
- [ ] `EmailSettings__Password` ayarlandı (Gmail App Password)
- [ ] `Groq__ApiKey` ayarlandı
- [ ] `AllowedOrigins` production domain'e güncellendi

### appsettings.json
- [ ] **Sensitive data yok** (şifreler, API key'ler)
- [ ] Sadece development değerleri var
- [ ] Production değerleri environment variable'dan gelecek

### Cookie Güvenliği
- [ ] Production'da `SecurePolicy = Always` aktif
- [ ] `HttpOnly = true` aktif
- [ ] `SameSite = Strict` aktif

### SSL/HTTPS
- [ ] SSL sertifikası kuruldu
- [ ] HTTPS zorunlu
- [ ] HSTS aktif (365 gün)
- [ ] HTTP → HTTPS redirect çalışıyor

## 🗄️ DATABASE

- [ ] Production database oluşturuldu
- [ ] Connection string güvenli
- [ ] Database backup stratejisi var
- [ ] Migration'lar çalıştırıldı (varsa)

## ⚙️ KONFİGÜRASYON

### Program.cs
- [ ] RazorRuntimeCompilation sadece Development'ta
- [ ] Cookie SecurePolicy production'da Always
- [ ] CORS doğru yapılandırılmış
- [ ] Error handling production'da aktif

### appsettings.Production.json
- [ ] Dosya oluşturuldu
- [ ] Sensitive data yok
- [ ] Environment variable placeholder'ları var

## 📁 DOSYA KONTROLLERİ

- [ ] `.gitignore` doğru yapılandırılmış
- [ ] `appsettings.Production.json` git'e commit edilmemeli
- [ ] Sensitive dosyalar ignore ediliyor
- [ ] Upload klasörü yapılandırılmış

## 🚀 DEPLOYMENT

### Build
- [ ] Release modunda build başarılı
- [ ] Tüm dependency'ler yüklendi
- [ ] Hata yok

### Deployment Platform
- [ ] IIS / Linux / Azure hazır
- [ ] Environment variable'lar ayarlandı
- [ ] SSL sertifikası kuruldu
- [ ] Domain ayarları yapıldı

## 🧪 TEST KONTROLLERİ

### Fonksiyonellik
- [ ] Ana sayfa açılıyor
- [ ] Giriş/Kayıt çalışıyor
- [ ] Tasarım editörü çalışıyor
- [ ] Admin paneli erişilebilir
- [ ] Email gönderimi çalışıyor
- [ ] API çağrıları çalışıyor
- [ ] Dosya yükleme çalışıyor

### Güvenlik
- [ ] HTTPS zorunlu
- [ ] Security headers aktif
- [ ] Rate limiting çalışıyor
- [ ] CSRF koruması aktif
- [ ] Input validation çalışıyor

### Performance
- [ ] Sayfa yükleme süreleri kabul edilebilir
- [ ] Static files cache'leniyor
- [ ] Database sorguları optimize

## 📊 MONİTORİNG

- [ ] Logging aktif
- [ ] Error tracking kuruldu (opsiyonel)
- [ ] Uptime monitoring (opsiyonel)
- [ ] Performance monitoring (opsiyonel)

## 🔄 BACKUP

- [ ] Database backup stratejisi
- [ ] Dosya yedekleme stratejisi
- [ ] Yedekleme otomasyonu (opsiyonel)

## 📝 DOKÜMANTASYON

- [ ] `DEPLOYMENT.md` okundu
- [ ] `SECURITY.md` okundu
- [ ] Environment variable'lar dokümante edildi
- [ ] Deployment adımları not edildi

## ⚠️ BİLİNEN SORUNLAR

### Acil Düzeltilmesi Gerekenler
- [ ] **Şifre Hash'leme**: Şu anda plain text. BCrypt/Argon2 eklenmeli
- [ ] Email şifresi environment variable'da olmalı

### İyileştirme Önerileri
- [ ] CDN kullanımı (opsiyonel)
- [ ] Database connection pooling optimize
- [ ] Caching stratejisi (opsiyonel)

## ✅ SON KONTROL

- [ ] Tüm maddeler kontrol edildi
- [ ] Production ortamı test edildi
- [ ] Rollback planı hazır
- [ ] Ekip bilgilendirildi

---

**Kontrol Tarihi**: _______________
**Kontrol Eden**: _______________
**Notlar**: 
_________________________________
_________________________________
_________________________________






