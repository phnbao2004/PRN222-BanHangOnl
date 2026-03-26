using MiniShopee.Data;
using MiniShopee.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MiniShopee.Services;

public class UserGovernanceService(AppDbContext db, UserManager<ApplicationUser> userManager)
{
    public async Task ApplySpendingAndVipRuleAsync(ApplicationUser user, double orderTotal)
    {
        user.TotalSpentVnd += orderTotal;
        // Khi đủ VIP: thêm VipCustomer, GIỮ Customer để vẫn dùng được tất cả trang mua sắm
        if (user.TotalSpentVnd >= 1_000_000 && !await userManager.IsInRoleAsync(user, "VipCustomer"))
        {
            await userManager.AddToRoleAsync(user, "VipCustomer");
            // Đảm bảo luôn có role Customer
            if (!await userManager.IsInRoleAsync(user, "Customer"))
                await userManager.AddToRoleAsync(user, "Customer");
        }
        await userManager.UpdateAsync(user);
    }

    public async Task<bool> IsVipAsync(ApplicationUser user) =>
        await userManager.IsInRoleAsync(user, "VipCustomer") || user.TotalSpentVnd >= 1_000_000;

    public async Task ReportUserAsync(string userId, string reason)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return;
        user.FraudReports++;
        user.ReputationPoints = Math.Max(0, user.ReputationPoints - 1);
        db.UserReports.Add(new UserReport { UserId = userId, Reason = reason });
        if (user.FraudReports >= 3 || user.ReputationPoints == 0) user.IsBlocked = true;
        await db.SaveChangesAsync();
    }

    public async Task UnblockUserAsync(string userId)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return;
        user.IsBlocked = false;
        user.FraudReports = 0;
        user.ReputationPoints = 10;
        await db.SaveChangesAsync();
    }

    /// <summary>Ghi nhận hành vi xấu (hủy đơn / hoàn hàng) → tự động cảnh báo / khóa</summary>
    public async Task RecordBadBehaviorAsync(string userId, string behaviorType)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null || user.IsBlocked) return;

        var isCancelled = behaviorType == "Cancelled";
        if (isCancelled) user.CancelCount++; else user.ReturnCount++;

        // Tính điểm uy tín
        // Hủy đơn: -1 điểm mỗi lần; hoàn hàng: -2 điểm (boom hàng nặng hơn)
        int penalty = isCancelled ? 1 : 2;
        user.ReputationPoints = Math.Max(0, user.ReputationPoints - penalty);

        // Tỉ lệ hủy/hoàn (tính trên tổng đơn đã có)
        var totalOrders = await db.Orders.CountAsync(o => o.UserId == userId);
        var badOrders   = user.CancelCount + user.ReturnCount;
        double badRate  = totalOrders > 0 ? (double)badOrders / totalOrders : 0;

        // Xác định warning level mới
        string? reason = null;

        if (user.ReturnCount >= 5 || (user.ReturnCount >= 3 && badRate >= 0.5))
        {
            // Nhiều hoàn hàng / tỉ lệ cao → khóa
            user.WarningLevel = 3;
            user.IsBlocked = true;
            reason = $"Tự động khóa: {user.ReturnCount} lần hoàn hàng (tỉ lệ {badRate:P0})";
        }
        else if (user.CancelCount >= 10 || badRate >= 0.6)
        {
            // Hủy hàng loạt → khóa
            user.WarningLevel = 3;
            user.IsBlocked = true;
            reason = $"Tự động khóa: {user.CancelCount} lần hủy đơn (tỉ lệ {badRate:P0})";
        }
        else if (user.ReputationPoints <= 3 || badRate >= 0.4 || user.ReturnCount >= 2)
        {
            user.WarningLevel = Math.Max(user.WarningLevel, 2);
            reason = $"Cảnh báo nghiêm trọng: uy tín {user.ReputationPoints}/10, hủy {user.CancelCount}, hoàn {user.ReturnCount}";
        }
        else if (user.ReputationPoints <= 7 || user.CancelCount >= 3 || user.ReturnCount >= 1)
        {
            user.WarningLevel = Math.Max(user.WarningLevel, 1);
            reason = $"Cảnh báo: hủy {user.CancelCount} đơn, hoàn {user.ReturnCount} đơn";
        }

        if (reason != null)
        {
            user.LastWarningReason = reason;
            user.LastWarningAt = DateTime.UtcNow;
            db.UserReports.Add(new UserReport { UserId = userId, Reason = reason });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Admin xóa cảnh báo / tha lỗi cho user</summary>
    public async Task ResetWarningAsync(string userId, int pointsRestore = 2)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return;
        user.WarningLevel = Math.Max(0, user.WarningLevel - 1);
        user.ReputationPoints = Math.Min(10, user.ReputationPoints + pointsRestore);
        user.LastWarningReason = null;
        user.LastWarningAt = null;
        await db.SaveChangesAsync();
    }

    public async Task AssignStaffAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return;
        if (!await userManager.IsInRoleAsync(user, "Staff"))
            await userManager.AddToRoleAsync(user, "Staff");
    }
}
