-- Kartist Hafta 1-2 Veritabani Degisiklikleri
-- Bu script'i SQL Server Management Studio'da calistirin

-- 1. Kullanicilar tablosuna yeni alanlar ekle
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Kullanicilar') AND name = 'BasarisizGirisSayisi')
    ALTER TABLE Kullanicilar ADD BasarisizGirisSayisi INT DEFAULT 0;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Kullanicilar') AND name = 'HesapKilitliMi')
    ALTER TABLE Kullanicilar ADD HesapKilitliMi BIT DEFAULT 0;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Kullanicilar') AND name = 'KilitBitisTarihi')
    ALTER TABLE Kullanicilar ADD KilitBitisTarihi DATETIME NULL;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Kullanicilar') AND name = 'IkiFactorAktif')
    ALTER TABLE Kullanicilar ADD IkiFactorAktif BIT DEFAULT 0;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Kullanicilar') AND name = 'Biyografi')
    ALTER TABLE Kullanicilar ADD Biyografi NVARCHAR(500) NULL;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Kullanicilar') AND name = 'SosyalMedya')
    ALTER TABLE Kullanicilar ADD SosyalMedya NVARCHAR(500) NULL;

-- 2. 2FA kodlari tablosu
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('IkiFactorKodlari') AND type = 'U')
BEGIN
    CREATE TABLE IkiFactorKodlari (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        KullaniciEmail NVARCHAR(256) NOT NULL,
        Kod NVARCHAR(6) NOT NULL,
        OlusturmaTarihi DATETIME DEFAULT GETUTCDATE(),
        BitisTarihi DATETIME NOT NULL,
        Kullanildi BIT DEFAULT 0
    );
    CREATE INDEX IX_IkiFactorKodlari_Email ON IkiFactorKodlari(KullaniciEmail);
END

-- 3. Giris loglari tablosu
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('GirisLoglari') AND type = 'U')
BEGIN
    CREATE TABLE GirisLoglari (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        KullaniciEmail NVARCHAR(256),
        IpAdresi NVARCHAR(50),
        BasariliMi BIT,
        Tarih DATETIME DEFAULT GETUTCDATE(),
        UserAgent NVARCHAR(500)
    );
    CREATE INDEX IX_GirisLoglari_Email ON GirisLoglari(KullaniciEmail);
    CREATE INDEX IX_GirisLoglari_Tarih ON GirisLoglari(Tarih);
END

PRINT 'Veritabani guncelleme tamamlandi!';
