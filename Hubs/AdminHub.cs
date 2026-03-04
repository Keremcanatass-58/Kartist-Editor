using Microsoft.AspNetCore.SignalR;

namespace Kartist.Hubs
{
    public class AdminHub : Hub
    {
        public async Task KullaniciBaglan(string email)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, email);
        }
    }
}