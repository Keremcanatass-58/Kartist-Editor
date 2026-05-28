using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;
using Dapper;

namespace Kartist.Hubs
{
    public class NotificationHub : Hub
    {
        private readonly string _conn;
        public NotificationHub(IConfiguration config)
        {
            _conn = config.GetConnectionString("DefaultConnection");
        }

        // Yayin DB satirini kapat (hayalet yayin onleme). Yayinci sekmeyi kapatip/baglanti
        // koptugunda CanliYayinlar.Aktif=1 kaliyordu -> listede hayalet yayin. Hata olursa sessizce gec.
        private void MarkStreamEndedInDb(string streamId)
        {
            try
            {
                if (int.TryParse(streamId, out var id) && !string.IsNullOrEmpty(_conn))
                {
                    using var db = new SqlConnection(_conn);
                    db.Execute("UPDATE CanliYayinlar SET Aktif = 0, BitisTarihi = GETUTCDATE() WHERE Id = @id AND Aktif = 1", new { id });
                }
            }
            catch { }
        }

        public static ConcurrentDictionary<string, string> UserConnections = new();

        // streamId → broadcaster connectionId
        public static ConcurrentDictionary<string, string> StreamBroadcasters = new();
        // streamId → set of viewer connectionIds
        public static ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> StreamViewers = new();

        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.Identity?.Name;
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

            var connId = Context.ConnectionId;

            // If a broadcaster disconnects, notify all viewers
            foreach (var kvp in StreamBroadcasters)
            {
                if (kvp.Value == connId)
                {
                    StreamBroadcasters.TryRemove(kvp.Key, out _);
                    MarkStreamEndedInDb(kvp.Key);
                    await Clients.Group("LiveRoom_" + kvp.Key).SendAsync("StreamEnded");
                    if (StreamViewers.TryRemove(kvp.Key, out _)) { }
                    break;
                }
            }

            // If a viewer disconnects, notify the broadcaster
            foreach (var kvp in StreamViewers)
            {
                if (kvp.Value.TryRemove(connId, out _))
                {
                    if (StreamBroadcasters.TryGetValue(kvp.Key, out var broadcasterId))
                    {
                        await Clients.Client(broadcasterId).SendAsync("ViewerLeft", connId);
                    }
                    await Clients.Group("LiveRoom_" + kvp.Key).SendAsync("ViewerCountUpdate", kvp.Value.Count);
                    break;
                }
            }

            await base.OnDisconnectedAsync(exception);
        }

        // ===== LIVE CHAT =====
        public async Task JoinLiveRoom(string roomId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "LiveRoom_" + roomId);
        }

        public async Task SendLiveMessage(string roomId, string userHandle, string userAvatar, string message)
        {
            await Clients.Group("LiveRoom_" + roomId).SendAsync("ReceiveLiveMessage", userHandle, userAvatar, message);
        }

        // ===== WEBRTC SIGNALING =====

        public async Task StartBroadcast(string streamId)
        {
            StreamBroadcasters[streamId] = Context.ConnectionId;
            StreamViewers[streamId] = new ConcurrentDictionary<string, byte>();
            await Groups.AddToGroupAsync(Context.ConnectionId, "LiveRoom_" + streamId);
            await Clients.Group("LiveRoom_" + streamId).SendAsync("BroadcastStarted", streamId);
        }

        public async Task JoinStream(string streamId)
        {
            var connId = Context.ConnectionId;
            await Groups.AddToGroupAsync(connId, "LiveRoom_" + streamId);

            if (!StreamBroadcasters.TryGetValue(streamId, out var broadcasterId))
            {
                await Clients.Caller.SendAsync("StreamEnded");
                return;
            }

            if (StreamViewers.TryGetValue(streamId, out var viewers))
            {
                viewers[connId] = 0;
                await Clients.Group("LiveRoom_" + streamId).SendAsync("ViewerCountUpdate", viewers.Count);
            }

            // Tell the broadcaster a new viewer wants to connect
            await Clients.Client(broadcasterId).SendAsync("ViewerJoined", connId);
        }

        public async Task SendWebRTCOffer(string targetConnectionId, string sdpOffer)
        {
            await Clients.Client(targetConnectionId).SendAsync("ReceiveOffer", Context.ConnectionId, sdpOffer);
        }

        public async Task SendWebRTCAnswer(string targetConnectionId, string sdpAnswer)
        {
            await Clients.Client(targetConnectionId).SendAsync("ReceiveAnswer", Context.ConnectionId, sdpAnswer);
        }

        public async Task SendICECandidate(string targetConnectionId, string candidate)
        {
            await Clients.Client(targetConnectionId).SendAsync("ReceiveICECandidate", Context.ConnectionId, candidate);
        }

        public async Task EndBroadcast(string streamId)
        {
            StreamBroadcasters.TryRemove(streamId, out _);
            StreamViewers.TryRemove(streamId, out _);
            MarkStreamEndedInDb(streamId);
            await Clients.Group("LiveRoom_" + streamId).SendAsync("StreamEnded");
        }
    }
}
