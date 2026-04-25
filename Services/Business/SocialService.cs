using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kartist.Data.Repositories;
using Kartist.Models.DTOs;

namespace Kartist.Services.Business
{
    public class SocialService : ISocialService
    {
        private readonly ISocialRepository _socialRepository;
        private readonly AiModerationService _aiModerator;

        public SocialService(ISocialRepository socialRepository, AiModerationService aiModerator)
        {
            _socialRepository = socialRepository;
            _aiModerator = aiModerator;
        }

        public async Task<object> GetFeedAsync(int userId, int page, string filtre)
        {
            if (userId <= 0)
            {
                return new { success = false, message = "Geçersiz kullanıcı." };
            }

            try
            {
                int pageSize = 10;
                int offset = page * pageSize;

                var posts = await _socialRepository.GetFeedAsync(userId, offset, pageSize, filtre);

                var postList = posts.ToList();
                bool hasMore = postList.Count == pageSize;

                return new { success = true, posts = postList, hasMore = hasMore };
            }
            catch (Exception ex)
            {
                return new { success = false, message = $"Feed yülenemedi: {ex.Message}" };
            }
        }

        public async Task<object> DeletePostAsync(int postId, int userId, string webRootPath)
        {
            var gonderi = await _socialRepository.GetPostByIdAsync(postId);
            if (gonderi == null) return new { success = false, message = "Gönderi bulunamadı." };
            if (gonderi.KullaniciId != userId) return new { success = false, message = "Bu gönderiyi silme yetkiniz yok." };

            // Dosyaları temizle
            if (!string.IsNullOrEmpty(gonderi.GorselUrl) && gonderi.GorselUrl.StartsWith("/uploads/"))
            {
                try { System.IO.File.Delete(System.IO.Path.Combine(webRootPath, gonderi.GorselUrl.TrimStart('/'))); } catch { }
            }
            if (!string.IsNullOrEmpty(gonderi.OnceSonraResim) && gonderi.OnceSonraResim.StartsWith("/uploads/"))
            {
                try { System.IO.File.Delete(System.IO.Path.Combine(webRootPath, gonderi.OnceSonraResim.TrimStart('/'))); } catch { }
            }

            bool isDeleted = await _socialRepository.DeletePostAsync(postId);
            return new { success = isDeleted };
        }

        public async Task<object> EditPostAsync(int postId, int userId, string icerik)
        {
            var gonderi = await _socialRepository.GetPostByIdAsync(postId);
            if (gonderi == null) return new { success = false, message = "Gönderi bulunamadı." };
            if (gonderi.KullaniciId != userId) return new { success = false, message = "Bu gönderiyi düzenleme yetkiniz yok." };

            if (string.IsNullOrWhiteSpace(icerik))
                return new { success = false, message = "İçerik boş olamaz." };

            var modResult = await _aiModerator.AnalyzeContentAsync(icerik);
            if (modResult.IsToxic)
                return new { success = false, message = modResult.Message };

            icerik = Kartist.Helpers.InputValidator.SanitizeHtml(icerik);

            var hashtags = System.Text.RegularExpressions.Regex.Matches(icerik, @"#(\w+)")
                .Select(m => m.Groups[1].Value.ToLowerInvariant()).Distinct().ToList();

            bool isUpdated = await _socialRepository.UpdatePostContentAsync(postId, icerik, hashtags);
            return new { success = isUpdated };
        }

        public async Task<(bool liked, int begeniSayisi, int ownerId, string ownerEmail)> ToggleLikeAsync(int postId, int userId)
        {
             return await _socialRepository.ToggleLikeAsync(postId, userId);
        }

        public async Task<object> CreatePostAsync(int userId, string icerik, Microsoft.AspNetCore.Http.IFormFile gorsel, Microsoft.AspNetCore.Http.IFormFile onceSonraGorsel, string kodSinipet, string webRootPath)
        {
            string gorselUrl = null;
            if (gorsel != null && gorsel.Length > 0)
            {
                if (!Kartist.Helpers.FileUploadValidator.TryValidateImage(gorsel, 10 * 1024 * 1024, out var ext, out var err))
                    return new { success = false, message = err };

                var dosyaAdi = $"post_{System.Guid.NewGuid():N}{ext}";
                var klasor = System.IO.Path.Combine(webRootPath, "uploads/social");
                if (!System.IO.Directory.Exists(klasor)) System.IO.Directory.CreateDirectory(klasor);
                var yol = System.IO.Path.Combine(klasor, dosyaAdi);
                using var stream = new System.IO.FileStream(yol, System.IO.FileMode.Create);
                await gorsel.CopyToAsync(stream);
                gorselUrl = "/uploads/social/" + dosyaAdi;
            }

            string onceSonraUrl = null;
            if (onceSonraGorsel != null && onceSonraGorsel.Length > 0)
            {
                if (!Kartist.Helpers.FileUploadValidator.TryValidateImage(onceSonraGorsel, 10 * 1024 * 1024, out var ext, out var err))
                    return new { success = false, message = err };

                var dosyaAdi = $"xray_{System.Guid.NewGuid():N}{ext}";
                var klasor = System.IO.Path.Combine(webRootPath, "uploads/social");
                if (!System.IO.Directory.Exists(klasor)) System.IO.Directory.CreateDirectory(klasor);
                var yol = System.IO.Path.Combine(klasor, dosyaAdi);
                using var stream = new System.IO.FileStream(yol, System.IO.FileMode.Create);
                await onceSonraGorsel.CopyToAsync(stream);
                onceSonraUrl = "/uploads/social/" + dosyaAdi;
            }

            if (string.IsNullOrWhiteSpace(icerik) && string.IsNullOrWhiteSpace(gorselUrl) && string.IsNullOrWhiteSpace(kodSinipet))
                return new { success = false, message = "Lütfen bir içerik girin." };

            var modResult = await _aiModerator.AnalyzeContentAsync(icerik);
            if (modResult.IsToxic)
            {
                if (gorselUrl != null)
                {
                    try { System.IO.File.Delete(System.IO.Path.Combine(webRootPath, gorselUrl.TrimStart('/'))); } catch { }
                }
                return new { success = false, message = modResult.Message };
            }

            icerik = Kartist.Helpers.InputValidator.SanitizeHtml(icerik ?? "");

            var hashtags = System.Text.RegularExpressions.Regex.Matches(icerik, @"#(\w+)")
                .Select(m => m.Groups[1].Value.ToLowerInvariant()).Distinct().ToList();

            string vibe = "🔥";
            var lowIc = icerik.ToLower();
            if (lowIc.Contains("kod") || lowIc.Contains("css") || lowIc.Contains("c#") || !string.IsNullOrEmpty(kodSinipet)) vibe = "💻";
            else if (lowIc.Contains("tasarım") || lowIc.Contains("design") || lowIc.Contains("renk") || lowIc.Contains("çizim")) vibe = "🎨";
            else if (lowIc.Contains("fikir") || lowIc.Contains("proje") || lowIc.Contains("idea")) vibe = "💡";
            else if (lowIc.Contains("başarı") || lowIc.Contains("özellik")) vibe = "🚀";

            int gonderiId = await _socialRepository.CreatePostAsync(userId, icerik, gorselUrl, onceSonraUrl, kodSinipet, vibe, hashtags);

            return new { success = true, id = gonderiId, xp = 50 };
        }

        public async Task<object> GetCommentsAsync(int postId)
        {
            var yorumlar = await _socialRepository.GetCommentsAsync(postId);
            return new { success = true, yorumlar };
        }

        public async Task<object> CreateCommentAsync(int userId, int postId, string content, int? parentId)
        {
            if (string.IsNullOrWhiteSpace(content)) return new { success = false, message = "Yorum boş olamaz." };

            var modResult = await _aiModerator.AnalyzeContentAsync(content);
            if (modResult.IsToxic) return new { success = false, message = modResult.Message };

            content = Kartist.Helpers.InputValidator.SanitizeHtml(content);
            if (content.Length > 1000) content = content[..1000];

            int yorumId = await _socialRepository.CreateCommentAsync(userId, postId, content, parentId);
            return new { success = true, id = yorumId };
        }

        public async Task<object> DeleteCommentAsync(int commentId, int userId)
        {
            bool isDeleted = await _socialRepository.DeleteCommentAsync(commentId, userId);
            return new { success = isDeleted };
        }
    }
}
