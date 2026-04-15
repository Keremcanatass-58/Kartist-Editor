using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace Kartist.Hubs
{
    public class NotificationHub : Hub
    {
        // Simple mapping from UserId to ConnectionId
        public static ConcurrentDictionary<string, string> UserConnections = new();

        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.Identity?.Name; // In our app, it's email
            if (!string.IsNullOrEmpty(userId))
            {
                UserConnections[userId] = Context.ConnectionId;
            }
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            var userId = Context.User?.Identity?.Name;
            if (!string.IsNullOrEmpty(userId))
            {
                UserConnections.TryRemove(userId, out _);
            }
            await base.OnDisconnectedAsync(exception);
        }

        // ===== LIVE CHAT KARTIST =====
        public async Task JoinLiveRoom(string roomId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "LiveRoom_" + roomId);
        }

        public async Task SendLiveMessage(string roomId, string userHandle, string userAvatar, string message)
        {
            // Send the message to everyone in this room
            await Clients.Group("LiveRoom_" + roomId).SendAsync("ReceiveLiveMessage", userHandle, userAvatar, message);
        }
    }
}
