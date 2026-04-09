using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kartist.Models.DTOs;

namespace Kartist.Data.Repositories
{
    public class SocialRepository : ISocialRepository
    {
        private readonly string _conn;

        public SocialRepository(IConfiguration config)
        {
            _conn = config.GetConnectionString("DefaultConnection");
        }

        public async Task<IEnumerable<SosyalGonderiDto>> GetFeedAsync(int userId, int offset, int pageSize, string filtre)
        {
            using var db = new SqlConnection(_conn);

            string orderBy = filtre switch
            {
                "populer" => "g.BegeniSayisi DESC, g.OlusturmaTarihi DESC",
                "takip" => "g.OlusturmaTarihi DESC",
                _ => "g.OlusturmaTarihi DESC"
            };

            string whereExtra = filtre == "takip"
                ? "AND (g.KullaniciId IN (SELECT TakipEdilenId FROM Takipciler WHERE TakipEdenId = @uid) OR g.KullaniciId = @uid)"
                : "";

            string sql = $@"
                SELECT g.Id, g.Icerik,
                       CASE WHEN g.GorselUrl IS NULL OR g.GorselUrl = '' THEN 'https://via.placeholder.com/600x800?text=Gönderi' ELSE g.GorselUrl END as GorselUrl,
                       g.BegeniSayisi, g.YorumSayisi, g.OlusturmaTarihi,
                       g.KodSinipet,
                       CASE WHEN g.OnceSonraResim IS NULL OR g.OnceSonraResim = '' THEN 'https://via.placeholder.com/600x800?text=Resim' ELSE g.OnceSonraResim END as OnceSonraResim,
                       g.AiVibe,
                       k.Id as KullaniciId, k.AdSoyad,
                       CASE WHEN k.ProfilResmi IS NULL OR k.ProfilResmi = '' THEN 'https://via.placeholder.com/150x150?text=Profil' ELSE k.ProfilResmi END as ProfilResmi,
                       k.UyelikTipi,
                       CASE WHEN b.Id IS NOT NULL THEN 1 ELSE 0 END as Begenildi,
                       CASE WHEN kay.Id IS NOT NULL THEN 1 ELSE 0 END as Kaydedildi,
                       CASE WHEN g.KullaniciId = @uid THEN 1 ELSE 0 END as IsMyPost
                FROM SosyalGonderiler g
                JOIN Kullanicilar k ON g.KullaniciId = k.Id
                LEFT JOIN SosyalBegeniler b ON b.GonderiId = g.Id AND b.KullaniciId = @uid
                LEFT JOIN Kaydedilenler kay ON kay.GonderiId = g.Id AND kay.KullaniciId = @uid
                WHERE 1=1 {whereExtra}
                ORDER BY {orderBy}
                OFFSET @offset ROWS FETCH NEXT @size ROWS ONLY";

            var posts = (await db.QueryAsync<SosyalGonderiDto>(sql, new { uid = userId, offset, size = pageSize })).ToList();
            foreach (var post in posts)
            {
                post.GorselUrl = NormalizeMediaUrl(post.GorselUrl);
                post.OnceSonraResim = NormalizeMediaUrl(post.OnceSonraResim);
                post.ProfilResmi = NormalizeMediaUrl(post.ProfilResmi);
            }
            return posts;
        }

        public async Task<SosyalGonderiDto> GetPostByIdAsync(int postId)
        {
            using var db = new SqlConnection(_conn);
            string sql = @"
                SELECT g.Id, g.Icerik,
                       g.GorselUrl,
                       g.BegeniSayisi, g.YorumSayisi, g.OlusturmaTarihi,
                       g.KodSinipet,
                       g.OnceSonraResim,
                       g.AiVibe,
                       k.Id as KullaniciId, k.AdSoyad,
                       CASE WHEN k.ProfilResmi IS NULL OR k.ProfilResmi = '' THEN 'https://via.placeholder.com/150x150?text=Profil' ELSE k.ProfilResmi END as ProfilResmi,
                       k.UyelikTipi,
                       0 as Begenildi,
                       0 as Kaydedildi,
                       0 as IsMyPost
                FROM SosyalGonderiler g
                JOIN Kullanicilar k ON g.KullaniciId = k.Id
                WHERE g.Id = @id";

            var post = await db.QueryFirstOrDefaultAsync<SosyalGonderiDto>(sql, new { id = postId });
            if (post != null)
            {
                post.GorselUrl = NormalizeMediaUrl(post.GorselUrl);
                post.OnceSonraResim = NormalizeMediaUrl(post.OnceSonraResim);
                post.ProfilResmi = NormalizeMediaUrl(post.ProfilResmi);
            }
            return post;
        }

        private string NormalizeMediaUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;
            url = url.Trim().Replace("\\", "/");
            if (url.StartsWith("~/")) url = "/" + url.Substring(2);
            if (url.StartsWith("http://") || url.StartsWith("https://")) return url;
            if (url.StartsWith("/")) return url;
            if (url.StartsWith("uploads/")) return "/" + url;
            if (url.Contains(".") && (url.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || url.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || url.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || url.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) || url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) || url.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)))
            {
                return "/uploads/social/" + url;
            }
            return "/" + url;
        }

        public async Task<bool> DeletePostAsync(int postId)
        {
            using var db = new SqlConnection(_conn);
            await db.ExecuteAsync("DELETE FROM Hashtagler WHERE GonderiId = @gid", new { gid = postId });
            int rows = await db.ExecuteAsync("DELETE FROM SosyalGonderiler WHERE Id = @gid", new { gid = postId });
            return rows > 0;
        }

        public async Task<bool> UpdatePostContentAsync(int postId, string formattedContent, List<string> hashtags)
        {
            using var db = new SqlConnection(_conn);
            
            await db.ExecuteAsync("DELETE FROM Hashtagler WHERE GonderiId = @gid", new { gid = postId });
            foreach (var tag in hashtags)
            {
                await db.ExecuteAsync("INSERT INTO Hashtagler (Etiket, GonderiId) VALUES (@tag, @gid)", new { tag, gid = postId });
            }

            int rows = await db.ExecuteAsync("UPDATE SosyalGonderiler SET Icerik = @icerik WHERE Id = @gid", new { icerik = formattedContent, gid = postId });
            return rows > 0;
        }

        public async Task<(bool liked, int begeniSayisi, int ownerId, string ownerEmail)> ToggleLikeAsync(int postId, int userId)
        {
            using var db = new SqlConnection(_conn);
            
            int varMi = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SosyalBegeniler WHERE GonderiId = @gid AND KullaniciId = @uid", new { gid = postId, uid = userId });

            bool isLiked = false;
            
            if (varMi > 0)
            {
                await db.ExecuteAsync("DELETE FROM SosyalBegeniler WHERE GonderiId = @gid AND KullaniciId = @uid", new { gid = postId, uid = userId });
                await db.ExecuteAsync("UPDATE SosyalGonderiler SET BegeniSayisi = BegeniSayisi - 1 WHERE Id = @id AND BegeniSayisi > 0", new { id = postId });
                isLiked = false;
            }
            else
            {
                await db.ExecuteAsync("INSERT INTO SosyalBegeniler (GonderiId, KullaniciId) VALUES (@gid, @uid)", new { gid = postId, uid = userId });
                await db.ExecuteAsync("UPDATE SosyalGonderiler SET BegeniSayisi = BegeniSayisi + 1 WHERE Id = @id", new { id = postId });
                isLiked = true;
            }

            int begeniSayisi = await db.ExecuteScalarAsync<int>("SELECT BegeniSayisi FROM SosyalGonderiler WHERE Id = @id", new { id = postId });
            
            var owner = await db.QueryFirstOrDefaultAsync(
                "SELECT KullaniciId, (SELECT Email FROM Kullanicilar k WHERE k.Id = g.KullaniciId) as Email FROM SosyalGonderiler g WHERE Id = @id", 
                new { id = postId });

            return (isLiked, begeniSayisi, owner?.KullaniciId ?? 0, owner?.Email);
        }

        public async Task<int> CreatePostAsync(int userId, string icerik, string gorselUrl, string onceSonraUrl, string kodSinipet, string vibe, List<string> hashtags)
        {
            using var db = new SqlConnection(_conn);
            int gonderiId = await db.ExecuteScalarAsync<int>(
                @"INSERT INTO SosyalGonderiler (KullaniciId, Icerik, GorselUrl, OnceSonraResim, KodSinipet, AiVibe) OUTPUT INSERTED.Id VALUES (@uid, @icerik, @gorsel, @xray, @kod, @vibe)",
                new { uid = userId, icerik, gorsel = gorselUrl, xray = onceSonraUrl, kod = kodSinipet, vibe });

            foreach (var tag in hashtags)
            {
                await db.ExecuteAsync("INSERT INTO Hashtagler (Etiket, GonderiId) VALUES (@tag, @gid)", new { tag, gid = gonderiId });
            }

            return gonderiId;
        }

        public async Task<object> GetCommentsAsync(int postId)
        {
            using var db = new SqlConnection(_conn);
            var yorumlar = await db.QueryAsync(@"SELECT y.Id, y.Icerik, y.Tarih, y.UstYorumId, k.Id as KullaniciId, k.AdSoyad, k.ProfilResmi
                                      FROM SosyalYorumlar y JOIN Kullanicilar k ON y.KullaniciId = k.Id
                                      WHERE y.GonderiId = @gid ORDER BY y.Tarih ASC", new { gid = postId });
            return yorumlar;
        }

        public async Task<int> CreateCommentAsync(int userId, int postId, string content, int? parentId)
        {
            using var db = new SqlConnection(_conn);
            int yorumId = await db.ExecuteScalarAsync<int>(
                "INSERT INTO SosyalYorumlar (GonderiId, KullaniciId, Icerik, UstYorumId) OUTPUT INSERTED.Id VALUES (@gid, @uid, @icerik, @ust)",
                new { gid = postId, uid = userId, icerik = content, ust = parentId });

            await db.ExecuteAsync("UPDATE SosyalGonderiler SET YorumSayisi = YorumSayisi + 1 WHERE Id = @gid", new { gid = postId });
            return yorumId;
        }

        public async Task<bool> DeleteCommentAsync(int commentId, int userId)
        {
            using var db = new SqlConnection(_conn);
            var yorum = await db.QueryFirstOrDefaultAsync("SELECT GonderiId, KullaniciId FROM SosyalYorumlar WHERE Id = @id", new { id = commentId });
            
            if (yorum == null || (int)yorum.KullaniciId != userId) return false;

            await db.ExecuteAsync("DELETE FROM SosyalYorumlar WHERE Id = @id", new { id = commentId });
            await db.ExecuteAsync("UPDATE SosyalGonderiler SET YorumSayisi = YorumSayisi - 1 WHERE Id = @gid AND YorumSayisi > 0", new { gid = (int)yorum.GonderiId });
            return true;
        }

        public async Task<(bool isFollowing, string hedefAd)> ToggleFollowAsync(int followerId, int targetId)
        {
            using var db = new SqlConnection(_conn);
            int varMi = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Takipciler WHERE TakipEdenId = @ben AND TakipEdilenId = @o",
                new { ben = followerId, o = targetId });

            if (varMi > 0)
            {
                await db.ExecuteAsync("DELETE FROM Takipciler WHERE TakipEdenId = @ben AND TakipEdilenId = @o", new { ben = followerId, o = targetId });
                return (false, null);
            }
            else
            {
                await db.ExecuteAsync("INSERT INTO Takipciler (TakipEdenId, TakipEdilenId) VALUES (@ben, @o)", new { ben = followerId, o = targetId });
                string hedefAd = await db.ExecuteScalarAsync<string>("SELECT AdSoyad FROM Kullanicilar WHERE Id = @id", new { id = targetId });
                return (true, hedefAd);
            }
        }
    }
}
