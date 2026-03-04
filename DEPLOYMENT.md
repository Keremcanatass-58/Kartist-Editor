# 🚀 Production Deployment Checklist - KARTIST

Bu dokümantasyon, KARTIST projesini production ortamına deploy etmeden önce yapılması gereken adımları içerir.

## 📋 ÖN HAZIRLIK

### 1. Environment Variables Ayarları

Production sunucusunda aşağıdaki environment variable'ları ayarlayın:

#### Windows (IIS)
```powershell
# PowerShell ile
[System.Environment]::SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "Server=...;Database=...;", "Machine")
[System.Environment]::SetEnvironmentVariable("EmailSettings__Mail", "kartistt.official@gmail.com", "Machine")
[System.Environment]::SetEnvironmentVariable("EmailSettings__Password", "your-app-password", "Machine")
[System.Environment]::SetEnvironmentVariable("Groq__ApiKey", "your-api-key", "Machine")
[System.Environment]::SetEnvironmentVariable("AllowedOrigins", "https://yourdomain.com", "Machine")
```

#### Linux (systemd)
```bash
# /etc/systemd/system/kartist.service dosyasına ekle
[Service]
Environment="ConnectionStrings__DefaultConnection=Server=...;Database=...;"
Environment="EmailSettings__Mail=kartistt.official@gmail.com"
Environment="EmailSettings__Password=your-app-password"
Environment="Groq__ApiKey=your-api-key"
Environment="AllowedOrigins=https://yourdomain.com"
```

#### Azure App Service
Azure Portal > Configuration > Application Settings bölümünden ekleyin:
- `ConnectionStrings__DefaultConnection`
- `EmailSettings__Mail`
- `EmailSettings__Password`
- `Groq__ApiKey`
- `AllowedOrigins`

### 2. Database Ayarları

1. **Production Database Oluştur**
   ```sql
   -- Production database'i oluştur
   CREATE DATABASE KartistDB_Production;
   ```

2. **Connection String Güncelle**
   - Environment variable olarak ayarla
   - Güvenli bir şifre kullan
   - SSL/TLS zorunlu olsun

3. **Migration Çalıştır** (Eğer EF Core kullanıyorsan)
   ```bash
   dotnet ef database update --environment Production
   ```

### 3. SSL Sertifikası

1. **Let's Encrypt** (Ücretsiz) veya **commercial SSL** kullan
2. HTTPS zorunlu olduğundan emin ol
3. HSTS aktif (zaten kodda var)

### 4. Domain Ayarları

1. **appsettings.Production.json** dosyasında:
   ```json
   "AllowedOrigins": "https://yourdomain.com;https://www.yourdomain.com"
   ```

2. **DNS Ayarları**
   - A record veya CNAME ayarla
   - SSL sertifikası için domain doğrulaması

## 🔧 DEPLOYMENT ADIMLARI

### Adım 1: Build
```bash
# Release modunda build
dotnet publish -c Release -o ./publish

# Veya
dotnet build -c Release
```

### Adım 2: Dosya Kontrolü
- ✅ `appsettings.Production.json` var mı?
- ✅ `appsettings.json` içinde sensitive data yok mu?
- ✅ `.gitignore` doğru yapılandırılmış mı?

### Adım 3: Environment Variables
- ✅ Tüm sensitive data environment variable'da
- ✅ Connection string güvenli
- ✅ API key'ler environment variable'da

### Adım 4: IIS Deployment (Windows)

1. **IIS Manager'da Site Oluştur**
   - Application Pool: .NET 9.0
   - Physical Path: publish klasörü

2. **Web.config** (Gerekirse)
   ```xml
   <?xml version="1.0" encoding="utf-8"?>
   <configuration>
     <system.webServer>
       <handlers>
         <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
       </handlers>
       <aspNetCore processPath="dotnet" 
                   arguments=".\Kartist.dll" 
                   stdoutLogEnabled="false" 
                   stdoutLogFile=".\logs\stdout" />
     </system.webServer>
   </configuration>
   ```

3. **SSL Binding**
   - HTTPS binding ekle
   - SSL sertifikası seç

### Adım 5: Linux Deployment (systemd)

1. **Service Dosyası Oluştur** (`/etc/systemd/system/kartist.service`)
   ```ini
   [Unit]
   Description=Kartist Web Application
   After=network.target

   [Service]
   Type=notify
   WorkingDirectory=/var/www/kartist
   ExecStart=/usr/bin/dotnet /var/www/kartist/Kartist.dll
   Restart=always
   RestartSec=10
   Environment="ASPNETCORE_ENVIRONMENT=Production"
   Environment="ConnectionStrings__DefaultConnection=..."
   Environment="EmailSettings__Mail=..."
   Environment="EmailSettings__Password=..."
   Environment="Groq__ApiKey=..."
   Environment="AllowedOrigins=https://yourdomain.com"
   SyslogIdentifier=kartist
   User=www-data

   [Install]
   WantedBy=multi-user.target
   ```

2. **Nginx Reverse Proxy**
   ```nginx
   server {
       listen 80;
       server_name yourdomain.com;
       return 301 https://$server_name$request_uri;
   }

   server {
       listen 443 ssl http2;
       server_name yourdomain.com;

       ssl_certificate /path/to/cert.pem;
       ssl_certificate_key /path/to/key.pem;

       location / {
           proxy_pass http://localhost:5000;
           proxy_http_version 1.1;
           proxy_set_header Upgrade $http_upgrade;
           proxy_set_header Connection keep-alive;
           proxy_set_header Host $host;
           proxy_set_header X-Real-IP $remote_addr;
           proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
           proxy_set_header X-Forwarded-Proto $scheme;
           proxy_cache_bypass $http_upgrade;
       }
   }
   ```

### Adım 6: Azure App Service Deployment

1. **Azure Portal'da App Service Oluştur**
   - Runtime Stack: .NET 9.0
   - OS: Windows veya Linux

2. **Deployment Center**
   - GitHub Actions
   - Azure DevOps
   - FTP/FTPS
   - Local Git

3. **Configuration**
   - Application Settings (Environment Variables)
   - Connection Strings
   - General Settings (Always On: On)

## ✅ POST-DEPLOYMENT KONTROLLERİ

### Güvenlik Kontrolleri
- [ ] HTTPS zorunlu ve çalışıyor
- [ ] HSTS header aktif
- [ ] Security headers doğru çalışıyor
- [ ] Cookie SecurePolicy = Always
- [ ] CORS doğru yapılandırılmış
- [ ] Rate limiting aktif

### Fonksiyonellik Kontrolleri
- [ ] Ana sayfa açılıyor
- [ ] Giriş/Kayıt çalışıyor
- [ ] Tasarım editörü çalışıyor
- [ ] Admin paneli erişilebilir
- [ ] Email gönderimi çalışıyor
- [ ] API çağrıları çalışıyor

### Performance Kontrolleri
- [ ] Sayfa yükleme süreleri kabul edilebilir
- [ ] Static files cache'leniyor
- [ ] Database sorguları optimize
- [ ] CDN kullanılıyor (opsiyonel)

### Monitoring
- [ ] Logging aktif
- [ ] Error tracking (Sentry, Application Insights vb.)
- [ ] Uptime monitoring
- [ ] Performance monitoring

## 🔐 GÜVENLİK NOTLARI

1. **Asla appsettings.json'a sensitive data yazma**
2. **Environment variable'ları düzenli kontrol et**
3. **SSL sertifikasını düzenli yenile**
4. **Backup stratejisi oluştur**
5. **Dependency'leri düzenli güncelle**

## 📞 SORUN GİDERME

### Yaygın Sorunlar

1. **500 Internal Server Error**
   - Logları kontrol et
   - Environment variable'ları kontrol et
   - Database bağlantısını kontrol et

2. **Cookie çalışmıyor**
   - HTTPS aktif mi?
   - SecurePolicy = Always mı?
   - Domain doğru mu?

3. **CORS hatası**
   - AllowedOrigins doğru mu?
   - Credentials ayarı doğru mu?

## 🎯 SON KONTROL LİSTESİ

- [ ] Environment variable'lar ayarlandı
- [ ] Database bağlantısı test edildi
- [ ] SSL sertifikası aktif
- [ ] Domain ayarları yapıldı
- [ ] appsettings.Production.json hazır
- [ ] .gitignore sensitive files'ı ignore ediyor
- [ ] Build başarılı
- [ ] Deployment başarılı
- [ ] Tüm sayfalar test edildi
- [ ] Güvenlik kontrolleri yapıldı
- [ ] Monitoring kuruldu

---

**Son Güncelleme**: 2025-01-XX
**Versiyon**: 1.0






