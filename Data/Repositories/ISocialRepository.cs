using System.Collections.Generic;
using System.Threading.Tasks;
using Kartist.Models.DTOs;

namespace Kartist.Data.Repositories
{
    public interface ISocialRepository
    {
        Task<IEnumerable<SosyalGonderiDto>> GetFeedAsync(int userId, int offset, int pageSize, string filtre);
        Task<SosyalGonderiDto> GetPostByIdAsync(int postId);
        Task<bool> DeletePostAsync(int postId);
        Task<bool> UpdatePostContentAsync(int postId, string formattedContent, List<string> hashtags);
        Task<(bool liked, int begeniSayisi, int ownerId, string ownerEmail)> ToggleLikeAsync(int postId, int userId);
        Task<int> CreatePostAsync(int userId, string icerik, string gorselUrl, string onceSonraUrl, string kodSinipet, string vibe, List<string> hashtags);
        
        Task<object> GetCommentsAsync(int postId);
        Task<int> CreateCommentAsync(int userId, int postId, string content, int? parentId);
        Task<bool> DeleteCommentAsync(int commentId, int userId);
        
        Task<(bool isFollowing, string hedefAd)> ToggleFollowAsync(int followerId, int targetId);
    }
}
