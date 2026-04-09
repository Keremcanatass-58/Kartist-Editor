# 🚀 Kartist Projesi: Sprint 4 İlerleme Raporu (Hafta 7-8)

**Proje Adı:** Kartist - Tasarım Odaklı Sosyal Ağ & Portfolyo Platformu  
**Dönem:** Bahar Yarıyılı  
**Geliştirici:** Keremcan Ataş  

---

> [!NOTE]
> Bu sprint boyunca, platformun **Sosyal Etkileşim (Sosyal Akış)** altyapısı sıfırdan inşa edilmiş, kullanıcı deneyimini (UX) merkeze alan modern "Neon Glassmorphism" tasarım dili standartlaştırılmıştır. Ayrıca Backend tarafında ciddi güvenlik (Yetkilendirme/Claim) revizyonları yapılarak sistem Canlı CI/CD hattına (kartistt.com.tr) entegre edilmiştir.

## 🎯 Temel Kazanımlar ve Geliştirmeler

### 1. Sosyal Akış (Social Feed) Modülünün İnşası
Sosyal medya modülü tam işlevsel bir hale getirilmiştir. 
- **Veritabanı Entegrasyonu:** `DatabaseInitializer` sınıfı tamamen refactor edilerek sahte (mock) veri otomasyonu hatasız hale getirildi. Kırık resim URL'leri düzeltilerek test veritabanının stabil olması sağlandı.
- **Dinamik Post Yönetimi:** Kullanıcıların kendi gönderilerini güvenli bir şekilde silmesi ve düzenlemesi (Edit/Delete) sağlandı.
- **Server Yönlü Sahiplik Kontrolü:** İstemci tarafı zafiyetlerini (Client-side vulnerabilities) gidermek adına, gönderi sahipliği artık SQL sunucu tarafında `CASE WHEN g.KullaniciId = @uid THEN 1 ELSE 0 END as IsMyPost` flag'i ile kontrol ediliyor. Account controller içindeki Login süreçlerinde `Id` Claim tabanlı doğrulama sisteme entegre edildi.

### 2. Yapay Zeka (AI) Destekli İçerik Moderasyonu
Platformun güvenliğini sağlamak için `AiModerationService` entegre edildi.
- Gönderi metinleri AI tarafından analiz edilerek "Nefret Söylemi" veya "Uygunsuz İçerik" puanlamasına tabi tutulmaktadır.
- Toksik olarak etiketlenen metinler gönderim sırasında yakalanıp işlem engellenerek platform kalitesi korunmaktadır.

### 3. Kullanıcı Deneyimi (UX/UI) Güncellemeleri
> [!TIP]
> Performans iyileştirmesi amacıyla işlemciyi yoran ağır Backdrop filtreleri kaldırılarak, modern, hızlı ve minimal animasyonlara geçilmiştir.

- **X-Ray (Before-After) Önizleme Aracı:** Kullanıcıların tasarımlarının "Öncesi/Sonrası" hallerini paylaşabilmesi için kaydırılabilir (slider) "X-Ray" UI arayüzü entegre edildi. Formata uygun resim önizlemeleri JavaScript ile güçlendirildi.
- **SweetAlert2 Entegrasyonu:** Silme işlemi onayları, hata mesajları ve düzenleme (Edit) panelleri Native `window.prompt` yerine asenkron çalışan şık `SweetAlert2` modalları ile değiştirildi.
- **Responsive Navigasyon:** Ana sayfa (Hero) bölümüne Mobil uyumlu, platformun ruhunu yansıtan "Sosyal Akış" çağrı butonu eklendi.

### 4. CI/CD Pipeline (GitHub Actions)
- Sunucu tarafında (kartistt.com.tr) karşılaşılan `CS0234` namespace hataları çözülerek pipeline başarı durumu %100'e çıkarıldı.
- `NotifyHub` ve `AiModeration` servisleri repoya dahil edilerek DevOps sürecindeki derleme hataları ortadan kaldırıldı ve canlı sunucuya kesintisiz versiyon aktarımı sağlandı.

---

## 🛠 Teknik Mimari & Refactoring Süreci

### [Önceki Yapı] İstemci Bağımlı (Güvensiz) Frontend Check
```javascript
// Eski ve Zafiyetli Yöntem (Kaldırıldı)
const isMyPost = g.KullaniciId === MY_USER_ID; 
// MY_USER_ID istemci tarafında cookie'den alınabiliyordu.
```

### [Yeni Yapı] Sunucu Güvenli Claim Kontrolü
```csharp
// Güncel Backend Uygulaması
string sql = $@"
    SELECT g.Id, g.Icerik, g.GorselUrl, g.OlusturmaTarihi,
           k.Id as KullaniciId, k.AdSoyad, k.ProfilResmi,
           CASE WHEN g.KullaniciId = @uid THEN 1 ELSE 0 END as IsMyPost 
    FROM SosyalGonderiler g
    JOIN Kullanicilar k ON g.KullaniciId = k.Id
    WHERE 1=1 ORDER BY g.OlusturmaTarihi DESC";
```
Bu değişiklik ile yetkilendirme (Authorization) ihlallerinin önüne geçilmiş, MVC mimarisinin prensipleri eksiksiz uygulanmıştır.

---

## 📈 Sonraki Sprint Hedefleri (Sprint 5)
1. **Canlı Bildirimler (Real-Time Notifications):** SignalR (NotificationHub) üzerinden soket bağlantılarının test edilmesi ve like/yorum aksiyonlarının eşzamanlı bildirilmesi.
2. **Mesajlaşma (DM) Paneli:** Kişiden kişiye mesajlaşma altyapısının socket tabanlı yapıya entegre edilmesi.
3. **Kapsamlı Profil İstatistikleri:** Profile giren kullanıcılar için grafik tabanlı görüntülenme/beğeni analizlerinin oluşturulması.

✅ *Kartist projesi hedeflenen takvime uygun, performans ve güvenlik standartları en üst düzeyde olacak şekilde geliştirilmeye devam etmektedir.*
