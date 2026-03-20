using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using System;

namespace Kartist.Hubs
{
    public class AdminHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            var http = Context.GetHttpContext();
            var isAdmin = http?.Session?.GetString("AdminOturumu") == "Aktif";

            if (isAdmin)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, "admins");
            }

            await base.OnConnectedAsync();
        }

        public async Task KullaniciBaglan(string email)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, email);
        }

        public async Task JoinUser(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return;
            await Groups.AddToGroupAsync(Context.ConnectionId, email);
        }

        public async Task JoinAdmin()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "admins");
        }

        public async Task SendUserMessage(string email, string message)
        {
            if (string.IsNullOrWhiteSpace(email)) return;
            var cleanMessage = message?.Trim();
            if (string.IsNullOrWhiteSpace(cleanMessage)) return;

            var payload = new { from = "user", email, message = cleanMessage, at = DateTime.UtcNow };
            await Clients.Group("admins").SendAsync("SupportMessage", payload);
            await Clients.Group(email).SendAsync("SupportMessage", payload);
        }

        public async Task SendAdminMessage(string email, string message)
        {
            if (string.IsNullOrWhiteSpace(email)) return;
            var cleanMessage = message?.Trim();
            if (string.IsNullOrWhiteSpace(cleanMessage)) return;

            var payload = new { from = "admin", email, message = cleanMessage, at = DateTime.UtcNow };
            await Clients.Group("admins").SendAsync("SupportMessage", payload);
            await Clients.Group(email).SendAsync("SupportMessage", payload);
        }
    }
}
