using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Identity;
using MiniShopee.Models;
using MiniShopee.Services;

namespace MiniShopee.Hubs;

/// <summary>
/// Real-time chat hub: Customer ↔ Staff/Admin
/// Groups:
///   "user_{userId}" → chỉ user đó nhận
///   "staff"         → tất cả Staff + Admin nhận
///
/// Auth: client truyền userId qua query string "uid" khi connect
/// (vì Blazor Server WebSocket không gửi cookie theo SignalR connection)
/// </summary>
public class ChatHub(CommerceService commerce, UserManager<ApplicationUser> userManager) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var uid = GetUserId();
        if (uid is not null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{uid}");

            var user = await userManager.FindByIdAsync(uid);
            if (user != null)
            {
                var roles = await userManager.GetRolesAsync(user);
                if (roles.Contains("Admin") || roles.Contains("Staff"))
                    await Groups.AddToGroupAsync(Context.ConnectionId, "staff");
            }
        }

        await base.OnConnectedAsync();
    }

    /// <summary>Customer gửi tin → lưu DB → báo staff</summary>
    public async Task SendToStaff(string content)
    {
        var uid = GetUserId();
        if (uid is null || string.IsNullOrWhiteSpace(content)) return;

        var msg = await commerce.SendMessageAsync(uid, content.Trim());

        await Clients.Group("staff").SendAsync("NewCustomerMessage",
            new { msg.UserId, msg.Content, SentAt = msg.SentAt.ToString("o") });
    }

    /// <summary>Staff/Admin reply một customer cụ thể</summary>
    public async Task ReplyToUser(string targetUserId, string content)
    {
        var uid = GetUserId();
        if (uid is null) return;

        var user = await userManager.FindByIdAsync(uid);
        if (user is null) return;
        var roles = await userManager.GetRolesAsync(user);
        if (!roles.Contains("Admin") && !roles.Contains("Staff")) return;

        if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(targetUserId)) return;

        var msg = await commerce.SendMessageAsync(targetUserId, content.Trim(), isFromStaff: true);

        // Gửi tới customer
        await Clients.Group($"user_{targetUserId}").SendAsync("ReceiveMessage", Dto(msg));

        // Echo xác nhận cho staff
        await Clients.Caller.SendAsync("ReplyEcho", new
        {
            targetUserId,
            msg.Content,
            SentAt = msg.SentAt.ToString("o")
        });
    }

    public async Task StaffViewing(string targetUserId)
        => await Clients.Group($"user_{targetUserId}").SendAsync("StaffOnline", true);

    public async Task StaffLeft(string targetUserId)
        => await Clients.Group($"user_{targetUserId}").SendAsync("StaffOnline", false);

    /// <summary>
    /// Đọc userId từ query string "uid" — client truyền vào khi connect.
    /// Dùng query string vì:
    /// - WebSocket không hỗ trợ custom header
    /// - Blazor Server dùng WebSocket riêng, cookie không được gửi kèm
    /// </summary>
    private string? GetUserId()
    {
        var ctx = Context.GetHttpContext();
        if (ctx is null) return null;

        // Query string "uid" — cách chính
        var uid = ctx.Request.Query["uid"].ToString();
        if (!string.IsNullOrEmpty(uid)) return uid;

        // Fallback: access_token (SignalR .NET client dùng khi không phải WebSocket)
        var token = ctx.Request.Query["access_token"].ToString();
        if (!string.IsNullOrEmpty(token)) return token;

        return null;
    }

    private static object Dto(MiniShopee.Models.ChatMessage m) => new
    {
        m.Id, m.UserId, m.Content, m.IsFromAI, m.IsFromStaff,
        SentAt = m.SentAt.ToString("o")
    };
}
