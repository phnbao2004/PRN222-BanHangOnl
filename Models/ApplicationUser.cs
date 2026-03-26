using Microsoft.AspNetCore.Identity;

namespace MiniShopee.Models;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = "";
    public string DefaultAddress { get; set; } = string.Empty;
    public string DefaultPhone { get; set; } = string.Empty;
    public double TotalSpentVnd { get; set; }
    public int ReputationPoints { get; set; } = 10;
    public bool IsBlocked { get; set; }
    public int FraudReports { get; set; }
    // Hành vi xấu
    public int CancelCount { get; set; }   // số lần tự hủy đơn
    public int ReturnCount { get; set; }   // số lần hoàn hàng
    // 0=bình thường 1=cảnh báo lần 1  2=cảnh báo lần 2  3=khóa tự động
    public int WarningLevel { get; set; }
    public string? LastWarningReason { get; set; }
    public DateTime? LastWarningAt { get; set; }
}
