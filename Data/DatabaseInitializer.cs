using System;
using Microsoft.Data.SqlClient;

namespace Kartist.Data
{
    public static class DatabaseInitializer
    {
        public static void Initialize(string connectionString)
        {
            EnsureSchema(connectionString);
            SeedBeautifulData(connectionString);
        }

        private static void EnsureSchema(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) return;

            const string sql = @"
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Kullanicilar') AND name = 'BasarisizGirisSayisi')
    ALTER TABLE Kullanicilar ADD BasarisizGirisSayisi INT NOT NULL DEFAULT 0;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Kullanicilar') AND name = 'HesapKilitliMi')
    ALTER TABLE Kullanicilar ADD HesapKilitliMi BIT NOT NULL DEFAULT 0;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Kullanicilar') AND name = 'KilitBitisTarihi')
    ALTER TABLE Kullanicilar ADD KilitBitisTarihi DATETIME NULL;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Kullanicilar') AND name = 'IkiFactorAktif')
    ALTER TABLE Kullanicilar ADD IkiFactorAktif BIT NOT NULL DEFAULT 0;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Kullanicilar') AND name = 'Biyografi')
    ALTER TABLE Kullanicilar ADD Biyografi NVARCHAR(500) NULL;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Kullanicilar') AND name = 'SosyalMedya')
    ALTER TABLE Kullanicilar ADD SosyalMedya NVARCHAR(500) NULL;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Kullanicilar') AND name = 'ProfilResmi')
    ALTER TABLE Kullanicilar ADD ProfilResmi NVARCHAR(500) NULL;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Kullanicilar') AND name = 'UyelikBitisTarihi')
    ALTER TABLE Kullanicilar ADD UyelikBitisTarihi DATETIME NULL;



IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('IkiFactorKodlari') AND type = 'U')
BEGIN
    CREATE TABLE IkiFactorKodlari (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        KullaniciEmail NVARCHAR(256) NOT NULL,
        Kod NVARCHAR(6) NOT NULL,
        OlusturmaTarihi DATETIME NOT NULL DEFAULT GETUTCDATE(),
        BitisTarihi DATETIME NOT NULL,
        Kullanildi BIT NOT NULL DEFAULT 0
    );
    CREATE INDEX IX_IkiFactorKodlari_Email ON IkiFactorKodlari(KullaniciEmail);
END

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('GirisLoglari') AND type = 'U')
BEGIN
    CREATE TABLE GirisLoglari (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        KullaniciEmail NVARCHAR(256) NULL,
        IpAdresi NVARCHAR(50) NULL,
        BasariliMi BIT NULL,
        Tarih DATETIME NOT NULL DEFAULT GETUTCDATE(),
        UserAgent NVARCHAR(500) NULL
    );
    CREATE INDEX IX_GirisLoglari_Email ON GirisLoglari(KullaniciEmail);
    CREATE INDEX IX_GirisLoglari_Tarih ON GirisLoglari(Tarih);
END

-- ===== SOSYAL MEDYA TABLOLARI =====

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('SosyalGonderiler') AND type = 'U')
BEGIN
    CREATE TABLE SosyalGonderiler (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        KullaniciId INT NOT NULL,
        Icerik NVARCHAR(2000) NULL,
        GorselUrl NVARCHAR(500) NULL,
        TasarimId INT NULL,
        BegeniSayisi INT NOT NULL DEFAULT 0,
        YorumSayisi INT NOT NULL DEFAULT 0,
        OlusturmaTarihi DATETIME NOT NULL DEFAULT GETUTCDATE(),
        FOREIGN KEY (KullaniciId) REFERENCES Kullanicilar(Id)
    );
    CREATE INDEX IX_SosyalGonderiler_Kullanici ON SosyalGonderiler(KullaniciId);
    CREATE INDEX IX_SosyalGonderiler_Tarih ON SosyalGonderiler(OlusturmaTarihi DESC);
END

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('SosyalBegeniler') AND type = 'U')
BEGIN
    CREATE TABLE SosyalBegeniler (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        GonderiId INT NOT NULL,
        KullaniciId INT NOT NULL,
        Tarih DATETIME NOT NULL DEFAULT GETUTCDATE(),
        FOREIGN KEY (GonderiId) REFERENCES SosyalGonderiler(Id) ON DELETE CASCADE,
        FOREIGN KEY (KullaniciId) REFERENCES Kullanicilar(Id),
        CONSTRAINT UQ_Begeni UNIQUE (GonderiId, KullaniciId)
    );
END

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('SosyalYorumlar') AND type = 'U')
BEGIN
    CREATE TABLE SosyalYorumlar (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        GonderiId INT NOT NULL,
        KullaniciId INT NOT NULL,
        Icerik NVARCHAR(1000) NOT NULL,
        Tarih DATETIME NOT NULL DEFAULT GETUTCDATE(),
        FOREIGN KEY (GonderiId) REFERENCES SosyalGonderiler(Id) ON DELETE CASCADE,
        FOREIGN KEY (KullaniciId) REFERENCES Kullanicilar(Id)
    );
    CREATE INDEX IX_SosyalYorumlar_Gonderi ON SosyalYorumlar(GonderiId);
END

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('Takipciler') AND type = 'U')
BEGIN
    CREATE TABLE Takipciler (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        TakipEdenId INT NOT NULL,
        TakipEdilenId INT NOT NULL,
        Tarih DATETIME NOT NULL DEFAULT GETUTCDATE(),
        FOREIGN KEY (TakipEdenId) REFERENCES Kullanicilar(Id),
        FOREIGN KEY (TakipEdilenId) REFERENCES Kullanicilar(Id),
        CONSTRAINT UQ_Takip UNIQUE (TakipEdenId, TakipEdilenId)
    );
END

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('Hikayeler') AND type = 'U')
BEGIN
    CREATE TABLE Hikayeler (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        KullaniciId INT NOT NULL,
        GorselUrl NVARCHAR(500) NOT NULL,
        OlusturmaTarihi DATETIME NOT NULL DEFAULT GETUTCDATE(),
        BitisTarihi DATETIME NOT NULL,
        FOREIGN KEY (KullaniciId) REFERENCES Kullanicilar(Id)
    );
    CREATE INDEX IX_Hikayeler_Kullanici ON Hikayeler(KullaniciId);
END

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('DirektMesajlar') AND type = 'U')
BEGIN
    CREATE TABLE DirektMesajlar (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        GonderenId INT NOT NULL,
        AliciId INT NOT NULL,
        Mesaj NVARCHAR(2000) NOT NULL,
        GorselUrl NVARCHAR(500) NULL,
        OkunduMu BIT NOT NULL DEFAULT 0,
        Tarih DATETIME NOT NULL DEFAULT GETUTCDATE(),
        FOREIGN KEY (GonderenId) REFERENCES Kullanicilar(Id),
        FOREIGN KEY (AliciId) REFERENCES Kullanicilar(Id)
    );
    CREATE INDEX IX_DM_Gonderen ON DirektMesajlar(GonderenId);
    CREATE INDEX IX_DM_Alici ON DirektMesajlar(AliciId);
END

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('Bildirimler') AND type = 'U')
BEGIN
    CREATE TABLE Bildirimler (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        KullaniciId INT NOT NULL,
        Tip NVARCHAR(50) NOT NULL,
        Mesaj NVARCHAR(500) NOT NULL,
        BaglantiliId INT NULL,
        GonderenId INT NULL,
        OkunduMu BIT NOT NULL DEFAULT 0,
        Tarih DATETIME NOT NULL DEFAULT GETUTCDATE(),
        FOREIGN KEY (KullaniciId) REFERENCES Kullanicilar(Id)
    );
    CREATE INDEX IX_Bildirimler_Kullanici ON Bildirimler(KullaniciId);
END

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('Hashtagler') AND type = 'U')
BEGIN
    CREATE TABLE Hashtagler (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        Etiket NVARCHAR(100) NOT NULL,
        GonderiId INT NOT NULL,
        FOREIGN KEY (GonderiId) REFERENCES SosyalGonderiler(Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_Hashtagler_Etiket ON Hashtagler(Etiket);
END

-- ===== SPRINT 1: GAMIFICATION TABLOLARI =====

-- Kullanicilar tablosuna yeni sütunlar
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Kullanicilar') AND name = 'Seviye')
    ALTER TABLE Kullanicilar ADD Seviye INT NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Kullanicilar') AND name = 'ToplamXP')
    ALTER TABLE Kullanicilar ADD ToplamXP INT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Kullanicilar') AND name = 'Streak')
    ALTER TABLE Kullanicilar ADD Streak INT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Kullanicilar') AND name = 'KapakResmi')
    ALTER TABLE Kullanicilar ADD KapakResmi NVARCHAR(500) NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Kullanicilar') AND name = 'SonGorulenTarihi')
    ALTER TABLE Kullanicilar ADD SonGorulenTarihi DATETIME NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Kullanicilar') AND name = 'ProfilTema')
    ALTER TABLE Kullanicilar ADD ProfilTema NVARCHAR(20) NULL DEFAULT '#c6ff00';

-- SosyalGonderiler tablosuna yeni sütunlar
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('SosyalGonderiler') AND name = 'GoruntulemeSayisi')
    ALTER TABLE SosyalGonderiler ADD GoruntulemeSayisi INT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('SosyalGonderiler') AND name = 'Sabitlendi')
    ALTER TABLE SosyalGonderiler ADD Sabitlendi BIT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('SosyalGonderiler') AND name = 'RepostSayisi')
    ALTER TABLE SosyalGonderiler ADD RepostSayisi INT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('SosyalGonderiler') AND name = 'AnketMi')
    ALTER TABLE SosyalGonderiler ADD AnketMi BIT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('SosyalGonderiler') AND name = 'OnceSonraResim')
    ALTER TABLE SosyalGonderiler ADD OnceSonraResim NVARCHAR(500) NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('SosyalGonderiler') AND name = 'KodSinipet')
    ALTER TABLE SosyalGonderiler ADD KodSinipet NVARCHAR(MAX) NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('SosyalGonderiler') AND name = 'AiVibe')
    ALTER TABLE SosyalGonderiler ADD AiVibe NVARCHAR(50) NULL;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('SosyalYorumlar') AND name = 'UstYorumId')
    ALTER TABLE SosyalYorumlar ADD UstYorumId INT NULL;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DirektMesajlar') AND name = 'Tip')
    ALTER TABLE DirektMesajlar ADD Tip NVARCHAR(50) DEFAULT 'Normal';

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DirektMesajlar') AND name = 'BaglantiliId')
    ALTER TABLE DirektMesajlar ADD BaglantiliId INT NULL;


IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('KullaniciXP') AND type = 'U')
BEGIN
    CREATE TABLE KullaniciXP (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        KullaniciId INT NOT NULL,
        Miktar INT NOT NULL,
        Kaynak NVARCHAR(50) NOT NULL,
        Aciklama NVARCHAR(200) NULL,
        Tarih DATETIME NOT NULL DEFAULT GETUTCDATE(),
        FOREIGN KEY (KullaniciId) REFERENCES Kullanicilar(Id)
    );
    CREATE INDEX IX_KullaniciXP_Kullanici ON KullaniciXP(KullaniciId);
    CREATE INDEX IX_KullaniciXP_Tarih ON KullaniciXP(Tarih DESC);
END

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('Rozetler') AND type = 'U')
BEGIN
    CREATE TABLE Rozetler (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        Kod NVARCHAR(50) NOT NULL UNIQUE,
        Ad NVARCHAR(100) NOT NULL,
        Aciklama NVARCHAR(300) NOT NULL,
        Ikon NVARCHAR(50) NOT NULL,
        Renk NVARCHAR(20) NOT NULL DEFAULT '#c6ff00',
        XPOdulu INT NOT NULL DEFAULT 0,
        Sira INT NOT NULL DEFAULT 0
    );
    -- Seed rozetler
    INSERT INTO Rozetler (Kod, Ad, Aciklama, Ikon, Renk, XPOdulu, Sira) VALUES
    ('ilk_gonderi', 'İlk Adım', 'İlk gönderini paylaştın!', '🚀', '#c6ff00', 50, 1),
    ('10_gonderi', 'İçerik Üretici', '10 gönderi paylaştın!', '✍️', '#ff6b6b', 100, 2),
    ('50_gonderi', 'İçerik Makinesi', '50 gönderi paylaştın!', '🔥', '#ff9500', 250, 3),
    ('ilk_begeni', 'İlk Kalp', 'İlk beğenini aldın!', '❤️', '#ff2d55', 25, 4),
    ('100_begeni', 'Sevilen', '100 beğeni topladın!', '💕', '#ff2d55', 200, 5),
    ('500_begeni', 'Popüler Yüz', '500 beğeni topladın!', '🌟', '#ffd700', 500, 6),
    ('ilk_takipci', 'İlk Fan', 'İlk takipçini kazandın!', '👤', '#4da6ff', 30, 7),
    ('50_takipci', 'Etkileyici', '50 takipçiye ulaştın!', '👥', '#4da6ff', 300, 8),
    ('100_takipci', 'Influencer', '100 takipçiye ulaştın!', '⭐', '#ffd700', 500, 9),
    ('ilk_yorum', 'Sohbetçi', 'İlk yorumunu yaptın!', '💬', '#30d158', 20, 10),
    ('7_streak', 'Kararlı', '7 gün üst üste giriş yaptın!', '🔥', '#ff6b6b', 150, 11),
    ('30_streak', 'Bağımlı', '30 gün üst üste giriş yaptın!', '💎', '#bf5af2', 500, 12),
    ('ilk_hikaye', 'Hikayeci', 'İlk hikayeni paylaştın!', '📸', '#ff9500', 30, 13),
    ('gece_kusu', 'Gece Kuşu', 'Gece 02:00-05:00 arası paylaşım yaptın!', '🦉', '#5e5ce6', 50, 14),
    ('hafta_yildizi', 'Hafta Yıldızı', 'Haftalık liderlik tablosunda 1. oldun!', '🏆', '#ffd700', 1000, 15);
END

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('KullaniciRozetleri') AND type = 'U')
BEGIN
    CREATE TABLE KullaniciRozetleri (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        KullaniciId INT NOT NULL,
        RozetId INT NOT NULL,
        KazanmaTarihi DATETIME NOT NULL DEFAULT GETUTCDATE(),
        FOREIGN KEY (KullaniciId) REFERENCES Kullanicilar(Id),
        FOREIGN KEY (RozetId) REFERENCES Rozetler(Id),
        CONSTRAINT UQ_KullaniciRozet UNIQUE (KullaniciId, RozetId)
    );
END

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('GirisKayitlari') AND type = 'U')
BEGIN
    CREATE TABLE GirisKayitlari (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        KullaniciId INT NOT NULL,
        Tarih DATE NOT NULL DEFAULT CAST(GETUTCDATE() AS DATE),
        FOREIGN KEY (KullaniciId) REFERENCES Kullanicilar(Id),
        CONSTRAINT UQ_GunlukGiris UNIQUE (KullaniciId, Tarih)
    );
END

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('GunlukGorevler') AND type = 'U')
BEGIN
    CREATE TABLE GunlukGorevler (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        KullaniciId INT NOT NULL,
        Tarih DATE NOT NULL DEFAULT CAST(GETUTCDATE() AS DATE),
        GorevTipi NVARCHAR(50) NOT NULL,
        HedefSayi INT NOT NULL DEFAULT 1,
        MevcutSayi INT NOT NULL DEFAULT 0,
        Tamamlandi BIT NOT NULL DEFAULT 0,
        XPOdulu INT NOT NULL DEFAULT 0,
        FOREIGN KEY (KullaniciId) REFERENCES Kullanicilar(Id)
    );
    CREATE INDEX IX_GunlukGorevler_Kullanici ON GunlukGorevler(KullaniciId, Tarih);
END

-- ===== SPRINT 2: ZENGİN ETKİLEŞİM TABLOLARI =====

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('GonderiReaksiyonlar') AND type = 'U')
BEGIN
    CREATE TABLE GonderiReaksiyonlar (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        GonderiId INT NOT NULL,
        KullaniciId INT NOT NULL,
        Reaksiyon NVARCHAR(10) NOT NULL,
        Tarih DATETIME NOT NULL DEFAULT GETUTCDATE(),
        FOREIGN KEY (GonderiId) REFERENCES SosyalGonderiler(Id) ON DELETE CASCADE,
        FOREIGN KEY (KullaniciId) REFERENCES Kullanicilar(Id),
        CONSTRAINT UQ_Reaksiyon UNIQUE (GonderiId, KullaniciId)
    );
END

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('Kaydedilenler') AND type = 'U')
BEGIN
    CREATE TABLE Kaydedilenler (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        KullaniciId INT NOT NULL,
        GonderiId INT NOT NULL,
        Tarih DATETIME NOT NULL DEFAULT GETUTCDATE(),
        FOREIGN KEY (KullaniciId) REFERENCES Kullanicilar(Id),
        FOREIGN KEY (GonderiId) REFERENCES SosyalGonderiler(Id) ON DELETE CASCADE,
        CONSTRAINT UQ_Kaydet UNIQUE (KullaniciId, GonderiId)
    );
END

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('Repostlar') AND type = 'U')
BEGIN
    CREATE TABLE Repostlar (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        KullaniciId INT NOT NULL,
        OrijinalGonderiId INT NOT NULL,
        Yorum NVARCHAR(500) NULL,
        Tarih DATETIME NOT NULL DEFAULT GETUTCDATE(),
        FOREIGN KEY (KullaniciId) REFERENCES Kullanicilar(Id),
        FOREIGN KEY (OrijinalGonderiId) REFERENCES SosyalGonderiler(Id) ON DELETE CASCADE
    );
END

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('AnketSecenekler') AND type = 'U')
BEGIN
    CREATE TABLE AnketSecenekler (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        GonderiId INT NOT NULL,
        Metin NVARCHAR(200) NOT NULL,
        OySayisi INT NOT NULL DEFAULT 0,
        Sira INT NOT NULL DEFAULT 0,
        FOREIGN KEY (GonderiId) REFERENCES SosyalGonderiler(Id) ON DELETE CASCADE
    );
END

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('AnketOylari') AND type = 'U')
BEGIN
    CREATE TABLE AnketOylari (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        SecenekId INT NOT NULL,
        KullaniciId INT NOT NULL,
        Tarih DATETIME NOT NULL DEFAULT GETUTCDATE(),
        FOREIGN KEY (SecenekId) REFERENCES AnketSecenekler(Id) ON DELETE CASCADE,
        FOREIGN KEY (KullaniciId) REFERENCES Kullanicilar(Id),
        CONSTRAINT UQ_AnketOy UNIQUE (SecenekId, KullaniciId)
    );
END

-- ===== CANLI YAYIN TABLOLARI =====

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('CanliYayinlar') AND type = 'U')
BEGIN
    CREATE TABLE CanliYayinlar (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        YayinciId INT NOT NULL,
        Baslik NVARCHAR(200) NOT NULL,
        Etiketler NVARCHAR(500) NULL,
        BaslangicTarihi DATETIME NOT NULL DEFAULT GETUTCDATE(),
        BitisTarihi DATETIME NULL,
        Aktif BIT NOT NULL DEFAULT 1,
        IzleyiciSayisi INT NOT NULL DEFAULT 0,
        FOREIGN KEY (YayinciId) REFERENCES Kullanicilar(Id)
    );
    CREATE INDEX IX_CanliYayinlar_Aktif ON CanliYayinlar(Aktif) INCLUDE (YayinciId, Baslik);
END

-- ===== SPRINT 3: HİKAYE HIGHLIGHTS =====

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('HikayeHighlightlar') AND type = 'U')
BEGIN
    CREATE TABLE HikayeHighlightlar (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        KullaniciId INT NOT NULL,
        Baslik NVARCHAR(100) NOT NULL,
        KapakUrl NVARCHAR(500) NULL,
        OlusturmaTarihi DATETIME NOT NULL DEFAULT GETUTCDATE(),
        FOREIGN KEY (KullaniciId) REFERENCES Kullanicilar(Id)
    );
END

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('HighlightHikayeleri') AND type = 'U')
BEGIN
    CREATE TABLE HighlightHikayeleri (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        HighlightId INT NOT NULL,
        GorselUrl NVARCHAR(500) NOT NULL,
        Sira INT NOT NULL DEFAULT 0,
        FOREIGN KEY (HighlightId) REFERENCES HikayeHighlightlar(Id) ON DELETE CASCADE
    );
END
";

            try
            {
                using var connection = new SqlConnection(connectionString);
                connection.Open();
                using var command = new SqlCommand(sql, connection);
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DB schema check failed: {ex.Message}");
            }
        }

        private static void SeedBeautifulData(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) return;

            const string wipeSql = @"
IF OBJECT_ID('Hashtagler', 'U') IS NOT NULL DELETE FROM Hashtagler;
IF OBJECT_ID('SosyalYorumlar', 'U') IS NOT NULL DELETE FROM SosyalYorumlar;
IF OBJECT_ID('Kaydedilenler', 'U') IS NOT NULL DELETE FROM Kaydedilenler;
IF OBJECT_ID('GonderiReaksiyonlar', 'U') IS NOT NULL DELETE FROM GonderiReaksiyonlar;
IF OBJECT_ID('Repostlar', 'U') IS NOT NULL DELETE FROM Repostlar;
IF OBJECT_ID('AnketOylari', 'U') IS NOT NULL DELETE FROM AnketOylari;
IF OBJECT_ID('AnketSecenekler', 'U') IS NOT NULL DELETE FROM AnketSecenekler;
IF OBJECT_ID('SosyalBegeniler', 'U') IS NOT NULL DELETE FROM SosyalBegeniler;
IF OBJECT_ID('SosyalGonderiler', 'U') IS NOT NULL DELETE FROM SosyalGonderiler;

IF OBJECT_ID('SosyalGonderiler', 'U') IS NOT NULL DBCC CHECKIDENT ('SosyalGonderiler', RESEED, 0);
IF OBJECT_ID('SosyalYorumlar', 'U') IS NOT NULL DBCC CHECKIDENT ('SosyalYorumlar', RESEED, 0);
IF OBJECT_ID('Hashtagler', 'U') IS NOT NULL DBCC CHECKIDENT ('Hashtagler', RESEED, 0);

DELETE FROM Kullanicilar WHERE Email IN ('aix@kartist.com', 'luna@kartist.com', 'cypher@kartist.com');
";

            const string createBots = @"
IF NOT EXISTS (SELECT 1 FROM Kullanicilar WHERE Email = 'aix@kartist.com')
    INSERT INTO Kullanicilar (AdSoyad, Email, Sifre, ProfilResmi, Seviye, UyelikTipi, Biyografi, KapakResmi) 
    VALUES ('AiX Designer', 'aix@kartist.com', 'seeded', 'https://picsum.photos/seed/aix/200/200', 25, 'Pro', 'Neo-brutalism and dynamic interactions. Crafting the web of tomorrow.', 'https://picsum.photos/seed/aixcover/1500/500');

IF NOT EXISTS (SELECT 1 FROM Kullanicilar WHERE Email = 'luna@kartist.com')
    INSERT INTO Kullanicilar (AdSoyad, Email, Sifre, ProfilResmi, Seviye, UyelikTipi, Biyografi, KapakResmi) 
    VALUES ('Luna Creative', 'luna@kartist.com', 'seeded', 'https://picsum.photos/seed/luna/200/200', 42, 'Free', 'Exploring digital landscapes. Digital art enthusiast.', 'https://picsum.photos/seed/lunacover/1500/500');

IF NOT EXISTS (SELECT 1 FROM Kullanicilar WHERE Email = 'cypher@kartist.com')
    INSERT INTO Kullanicilar (AdSoyad, Email, Sifre, ProfilResmi, Seviye, UyelikTipi, Biyografi, KapakResmi) 
    VALUES ('Cypher Dev', 'cypher@kartist.com', 'seeded', 'https://picsum.photos/seed/cypher/200/200', 18, 'Pro', 'Code is poetry. C#, TS, and creative coding.', 'https://picsum.photos/seed/cyphercover/1500/500');
";

            const string createPosts = @"
DECLARE @Id1 INT = (SELECT TOP 1 Id FROM Kullanicilar WHERE Email = 'aix@kartist.com');
DECLARE @Id2 INT = (SELECT TOP 1 Id FROM Kullanicilar WHERE Email = 'luna@kartist.com');
DECLARE @Id3 INT = (SELECT TOP 1 Id FROM Kullanicilar WHERE Email = 'cypher@kartist.com');

INSERT INTO SosyalGonderiler (KullaniciId, Icerik, GorselUrl, BegeniSayisi, YorumSayisi, GoruntulemeSayisi, AiVibe, OlusturmaTarihi)
VALUES 
(@Id1, 'Minimalist arayüzlerin güzelliği hiçbir zaman eskimiyor. Son konsept tasarımım! 🚀', 'https://picsum.photos/seed/vibe1/800/800', 1204, 85, 4500, 'Tasarım Odaklı', DATEADD(hour, -2, GETUTCDATE())),
(@Id2, 'Gece vaktinde 3D renderlar ile harikalar yaratmak! Blender üzerinde 14 saat harcadım ama kesinlikle değdi. Yorumlarınızı bekliyorum!', 'https://picsum.photos/seed/vibe2/800/800', 3450, 210, 12000, 'Görsel Şölen', DATEADD(hour, -5, GETUTCDATE())),
(@Id1, 'Yeni UI Kit paketimi yakında yayınlıyorum. Karanlık tema severler için özel renk paletleri hazırladım. Takipte kalın. 🎨', 'https://picsum.photos/seed/vibe3/800/800', 890, 45, 3000, 'Estetik', DATEADD(hour, -24, GETUTCDATE()));

INSERT INTO SosyalGonderiler (KullaniciId, Icerik, KodSinipet, BegeniSayisi, YorumSayisi, GoruntulemeSayisi, AiVibe, OlusturmaTarihi)
VALUES
(@Id3, 'Javascript ile Array işlemlerini hızlandırmak için ufak bir trik! Performansı %40 oranında artırıyor.', 'const fastMap = (arr, fn) => {
    const len = arr.length;
    const res = new Array(len);
    for(let i = 0; i < len; i++) {
        res[i] = fn(arr[i]);
    }
    return res;
};', 512, 34, 1800, 'Bilgi Paylaşımı', DATEADD(hour, -12, GETUTCDATE()));
";

            try
            {
                using var connection = new SqlConnection(connectionString);
                connection.Open();

                using var wipeCmd = new SqlCommand(wipeSql, connection);
                wipeCmd.ExecuteNonQuery();

                using var botsCmd = new SqlCommand(createBots, connection);
                botsCmd.ExecuteNonQuery();

                using var postsCmd = new SqlCommand(createPosts, connection);
                postsCmd.ExecuteNonQuery();

                Console.WriteLine("Beautiful Seed Data initialized successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Seeding failed: {ex.Message}");
            }
        }
    }
}
