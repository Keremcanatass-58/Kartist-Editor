# 🔒 Güvenlik Dokümantasyonu - KARTIST

Bu dokümantasyon, KARTIST projesinde uygulanan güvenlik önlemlerini açıklar.

## 🛡️ Uygulanan Güvenlik Önlemleri

### 1. Security Headers (Güvenlik Başlıkları)
- **X-XSS-Protection**: XSS saldırılarına karşı koruma
- **X-Frame-Options**: Clickjacking saldırılarına karşı koruma (DENY)
- **X-Content-Type-Options**: MIME type sniffing saldırılarına karşı koruma
- **Referrer-Policy**: Referrer bilgilerinin kontrolü
- **Content-Security-Policy (CSP)**: XSS ve injection saldırılarına karşı kapsamlı koruma
- **Permissions-Policy**: Tarayıcı özelliklerinin kısıtlanması

### 2. Rate Limiting (İstek Sınırlama)
- **DDoS Koruması**: Dakikada maksimum 100 istek (POST/PUT/DELETE)
- **Brute Force Koruması**: Giriş denemelerinin sınırlandırılması
- IP bazlı takip ve sınırlama

### 3. Secure Cookies (Güvenli Çerezler)
- **HttpOnly**: JavaScript'ten erişilemez (XSS koruması)
- **Secure**: Sadece HTTPS üzerinden gönderilir (Production)
- **SameSite=Strict**: CSRF saldırılarına karşı koruma
- **Sliding Expiration**: Her istekte süre yenilenir

### 4. Input Validation & Sanitization (Girdi Doğrulama)
- **XSS Koruması**: HTML tag'lerinin temizlenmesi
- **SQL Injection Koruması**: Tehlikeli karakterlerin kontrolü
- **Email Validation**: Geçerli e-posta formatı kontrolü
- **File Type Validation**: Dosya tipi ve uzantı kontrolü
- **Length Validation**: Maksimum/minimum uzunluk kontrolü

### 5. CSRF Protection (Cross-Site Request Forgery)
- **AutoValidateAntiforgeryToken**: Tüm POST isteklerinde otomatik doğrulama
- **SameSite Cookie**: Çerez bazlı ek koruma

### 6. SQL Injection Protection
- **Parametreli Sorgular**: Dapper kullanılarak tüm sorgular parametreli
- **Input Validation**: Ek katman olarak input doğrulama
- **Sanitization**: Tehlikeli karakterlerin temizlenmesi

### 7. HTTPS & HSTS
- **HTTPS Redirection**: Tüm HTTP istekleri HTTPS'ye yönlendirilir
- **HSTS (HTTP Strict Transport Security)**: 365 gün, subdomain'ler dahil

### 8. Request Size Limits
- **Max File Size**: 10MB
- **Max Request Size**: 10MB
- **Multipart Headers**: 1MB limit

### 9. CORS Policy
- **Restricted Origins**: Sadece izin verilen origin'ler
- **Credentials**: Güvenli credential paylaşımı

### 10. API Key Security
- **Configuration-Based**: API key'ler appsettings.json'da (hardcoded değil)
- **Environment Variables**: Production'da environment variable kullanılmalı

## 📋 Güvenlik Checklist

### Development (Geliştirme)
- [x] Security Headers eklendi
- [x] Rate Limiting aktif
- [x] Input Validation eklendi
- [x] Secure Cookies yapılandırıldı
- [x] CSRF Protection aktif
- [x] SQL Injection koruması
- [x] HTTPS Redirection

### Production (Canlı Ortam)
- [ ] Environment variable'lara API key'ler taşınmalı
- [ ] Cookie SecurePolicy = Always olmalı
- [ ] AllowedOrigins production domain'e güncellenmeli
- [ ] HSTS aktif ve test edilmeli
- [ ] Logging ve monitoring eklenmeli
- [ ] Regular security audits yapılmalı

## 🚨 Güvenlik İpuçları

1. **API Key'ler**: Asla kod içinde hardcode etmeyin. Environment variable veya Azure Key Vault kullanın.

2. **Şifreler**: Şifreler hash'lenmeli (BCrypt, Argon2). Şu anda plain text saklanıyor - **ACİL DÜZELTİLMELİ**.

3. **HTTPS**: Production'da mutlaka SSL sertifikası kullanın.

4. **Logging**: Hassas bilgiler (şifreler, API key'ler) loglara yazılmamalı.

5. **Dependencies**: NuGet paketleri düzenli güncellenmeli (security patches).

6. **Database**: Connection string'ler environment variable'da olmalı.

## ⚠️ Bilinen Güvenlik Açıkları

1. **Şifre Hash'leme**: Şu anda şifreler plain text olarak saklanıyor. **ACİL DÜZELTİLMELİ**.
   - Çözüm: BCrypt veya Argon2 kullanılmalı

2. **Email Şifresi**: appsettings.json'da plain text. Production'da environment variable kullanılmalı.

## 🔧 Güvenlik Güncellemeleri

Güvenlik güncellemeleri için:
1. Düzenli olarak dependency'leri güncelleyin
2. OWASP Top 10 listesini takip edin
3. Security advisories'i kontrol edin
4. Penetration test yapın

## 📞 İletişim

Güvenlik açığı bulursanız: kartistt.official@gmail.com

---

**Son Güncelleme**: 2025-01-XX
**Versiyon**: 1.0






