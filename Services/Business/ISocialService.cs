using System.Collections.Generic;
using System.Threading.Tasks;
using Kartist.Models.DTOs;

namespace Kartist.Services.Business
{
    public interface ISocialService
    {
        Task<object> GetFeedAsync(int userId, int page, string filtre);
        Task<object> DeletePostAsync(int postId, int userId, string webRootPath);
        Task<object> EditPostAsync(int postId, int userId, string icerik);
        Task<(bool liked, int begeniSayisi, int ownerId, string ownerEmail)> ToggleLikeAsync(int postId, int userId);
        Task<object> CreatePostAsync(int userId, string icerik, Microsoft.AspNetCore.Http.IFormFile gorsel, Microsoft.AspNetCore.Http.IFormFile onceSonraGorsel, string kodSinipet, string webRootPath);
        
        Task<object> GetCommentsAsync(int postId);
        Task<object> CreateCommentAsync(int userId, int postId, string content, int? parentId);
        Task<object> DeleteCommentAsync(int commentId, int userId);
    }
}
