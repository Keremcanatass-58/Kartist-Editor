-- Kartist production DB schema diagnosis (READ-ONLY, sadece SELECT)
-- SSMS/Azure Data Studio/Plesk SQL paneli ile kartist_db üzerinde çalıştır
-- Çıktıyı kopyalayıp Claude'a yapıştır.

PRINT '===== 1) Tablolar mevcut mu? =====';
SELECT t.required_table,
       CASE WHEN o.object_id IS NULL THEN 'EKSIK' ELSE 'VAR' END AS durum
FROM (VALUES
    ('Sablonlar'), ('Favoriler'), ('Kullanicilar'), ('KayitliTasarimlar'),
    ('Yarismalar'), ('YarismaKatilimlari'), ('YarismaOylari'),
    ('Duellolar'), ('DuelloOylari'),
    ('CanliYayinlar'),
    ('SosyalGonderiler'), ('SosyalBegeniler'), ('SosyalYorumlar')
) t(required_table)
LEFT JOIN sys.objects o ON o.name = t.required_table AND o.type = 'U'
ORDER BY t.required_table;

PRINT '===== 2) Sablonlar kolonlari (anasayfa SELECT bagimliligi) =====';
SELECT column_name, data_type, is_nullable
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'Sablonlar'
ORDER BY ORDINAL_POSITION;

PRINT '===== 3) Favoriler kolonlari =====';
SELECT column_name, data_type, is_nullable
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'Favoriler'
ORDER BY ORDINAL_POSITION;

PRINT '===== 4) Kullanicilar XP/Seviye kolonlari =====';
SELECT column_name, data_type, is_nullable
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'Kullanicilar'
  AND column_name IN ('Id','Email','Seviye','ToplamXP','ProfilResmi','AdSoyad')
ORDER BY ORDINAL_POSITION;

PRINT '===== 5) Yarismalar kolonlari (Competitions sayfasi) =====';
SELECT column_name, data_type, is_nullable
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'Yarismalar'
ORDER BY ORDINAL_POSITION;

PRINT '===== 6) Duellolar kolonlari (Duels sayfasi) =====';
SELECT column_name, data_type, is_nullable
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'Duellolar'
ORDER BY ORDINAL_POSITION;

PRINT '===== 7) CanliYayinlar kolonlari (Live sayfasi) =====';
SELECT column_name, data_type, is_nullable
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'CanliYayinlar'
ORDER BY ORDINAL_POSITION;

PRINT '===== 8) Anasayfa sorgusu kac kart donduruyor? =====';
BEGIN TRY
    SELECT COUNT(*) AS toplam_sablon FROM Sablonlar;
    SELECT TOP 5 Id, Baslik, Kategori,
           (CASE WHEN COL_LENGTH('Sablonlar','OnayDurumu') IS NULL THEN 'KOLON YOK' ELSE 'kolon var' END) AS OnayDurumu_kolon
    FROM Sablonlar;
END TRY
BEGIN CATCH
    SELECT ERROR_NUMBER() AS hata_no, ERROR_MESSAGE() AS hata_mesaji;
END CATCH;

PRINT '===== 9) DB kullanicisinin DDL yetkisi var mi? =====';
SELECT HAS_PERMS_BY_NAME(NULL, 'DATABASE', 'CREATE TABLE') AS db_create_table_perm,
       HAS_PERMS_BY_NAME(NULL, 'DATABASE', 'ALTER ANY SCHEMA') AS alter_schema_perm,
       SUSER_SNAME() AS bagli_login,
       USER_NAME() AS db_user;
