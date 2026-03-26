namespace MiniShopee.Models;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "Áo";
    public string ImageUrl { get; set; } = "";
    public double PriceVnd { get; set; }
    public int Stock { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    // Sizes available: comma-separated e.g. "S,M,L,XL"
    public string AvailableSizes { get; set; } = "S,M,L,XL";
}

/// <summary>
/// Percent  = giảm theo %  (DiscountPercent)
/// Fixed    = giảm tiền cố định từ MinOrderVnd trở lên (DiscountAmount)
/// Freeship = miễn phí ship từ MinOrderVnd trở lên
/// </summary>
public enum VoucherType { Percent, Fixed, Freeship }

public class Voucher
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public VoucherType Type { get; set; } = VoucherType.Percent;
    public double DiscountPercent { get; set; }   // dùng khi Type=Percent
    public double DiscountAmount { get; set; }    // dùng khi Type=Fixed
    public double ShipAmount { get; set; } = 30_000; // giá ship được miễn khi Type=Freeship
    public double MinOrderVnd { get; set; }       // đơn tối thiểu để áp dụng
    public bool VipOnly { get; set; }
    public string CreatedByRole { get; set; } = string.Empty;
    public DateTime ExpiredAt { get; set; }
    public bool IsActive { get; set; } = true;
    public int MaxUsesTotal { get; set; } = 0;   // 0 = không giới hạn tổng
    public int MaxUsesPerUser { get; set; } = 1; // mỗi user dùng tối đa bao nhiêu lần
    public int UsedCount { get; set; } = 0;      // tổng số lần đã dùng
    public ICollection<VoucherUsage> Usages { get; set; } = new List<VoucherUsage>();
}

/// <summary>Ghi lại mỗi lần user dùng voucher</summary>
public class VoucherUsage
{
    public int Id { get; set; }
    public int VoucherId { get; set; }
    public Voucher? Voucher { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateTime UsedAt { get; set; } = DateTime.UtcNow;
}

public class Order
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Pending";
    public double TotalVnd { get; set; }
    public string? AppliedVoucherCode { get; set; }
    public string? AppliedFreeshipCode { get; set; }
    public double ShipFeeVnd { get; set; }
    public string ShippingAddress { get; set; } = string.Empty;
    public string ReceiverName { get; set; } = string.Empty;
    public string ReceiverPhone { get; set; } = string.Empty;
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public int Quantity { get; set; }
    public double UnitPriceVnd { get; set; }
}

public class ChatMessage
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }
    public string Content { get; set; } = string.Empty;
    public bool IsFromAI { get; set; }
    public bool IsFromStaff { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}

public class ProductReview
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }
    public int Stars { get; set; } // 1–5
    public string Comment { get; set; } = string.Empty;
    public bool IsVerifiedPurchase { get; set; }
    public bool IsReported { get; set; }
    public bool IsHidden { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class UserReport
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public record ShopReport(
    double TotalRevenue,
    int OrderCount,
    int VipCount,
    int BlockedCount,
    int ProductCount,
    int ActiveVoucherCount
);
