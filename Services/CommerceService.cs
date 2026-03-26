using MiniShopee.Data;
using MiniShopee.Models;
using Microsoft.EntityFrameworkCore;

namespace MiniShopee.Services;

public class CommerceService(AppDbContext db, UserGovernanceService governance)
{
    // Products
    public Task<List<Product>> GetProductsAsync(string? search = null, string? category = null, string? sort = null)
    {
        var q = db.Products.Where(p => p.IsActive).AsQueryable();
        if (!string.IsNullOrEmpty(search))
            q = q.Where(p => p.Name.Contains(search) || p.Description.Contains(search) || p.Category.Contains(search));
        if (!string.IsNullOrEmpty(category))
            q = q.Where(p => p.Category == category);
        q = sort switch
        {
            "price_asc"  => q.OrderBy(p => p.PriceVnd),
            "price_desc" => q.OrderByDescending(p => p.PriceVnd),
            "newest"     => q.OrderByDescending(p => p.CreatedAt),
            _            => q.OrderBy(p => p.Id)
        };
        return q.ToListAsync();
    }

    public Task<List<Product>> GetAllProductsAdminAsync() => db.Products.OrderByDescending(p => p.Id).ToListAsync();
    public Task<List<string>> GetCategoriesAsync() => db.Products.Where(p => p.IsActive).Select(p => p.Category).Distinct().ToListAsync();

    public async Task<Product> UpsertProductAsync(Product product)
    {
        if (product.Id == 0) db.Products.Add(product);
        else db.Products.Update(product);
        await db.SaveChangesAsync();
        return product;
    }

    public async Task<bool> DeleteProductAsync(int id)
    {
        var p = await db.Products.FindAsync(id);
        if (p is null) return false;
        db.Products.Remove(p);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ToggleProductActiveAsync(int id)
    {
        var p = await db.Products.FindAsync(id);
        if (p is null) return false;
        p.IsActive = !p.IsActive;
        await db.SaveChangesAsync();
        return true;
    }

    // Vouchers
    public async Task<List<Voucher>> GetVouchersAsync(bool isVip, string? userId = null)
    {
        var all = await db.Vouchers
            .Include(v => v.Usages)
            .Where(v => v.IsActive && v.ExpiredAt > DateTime.UtcNow && (!v.VipOnly || isVip))
            .OrderBy(v => v.ExpiredAt)
            .ToListAsync();

        if (userId == null) return all;

        // Lọc bỏ voucher hết lượt dùng của user hoặc hết tổng lượt
        return all.Where(v =>
        {
            if (v.MaxUsesTotal > 0 && v.UsedCount >= v.MaxUsesTotal) return false;
            if (v.MaxUsesPerUser > 0)
            {
                var userUses = v.Usages.Count(u => u.UserId == userId);
                if (userUses >= v.MaxUsesPerUser) return false;
            }
            return true;
        }).ToList();
    }

    public Task<List<Voucher>> GetAllVouchersAsync() =>
        db.Vouchers.Include(v => v.Usages).OrderByDescending(v => v.ExpiredAt).ToListAsync();

    public async Task<Voucher> CreateVoucherAsync(Voucher v)
    {
        v.Code = v.Code.ToUpperInvariant();
        db.Vouchers.Add(v);
        await db.SaveChangesAsync();
        return v;
    }

    public async Task<bool> DeleteVoucherAsync(int id)
    {
        var v = await db.Vouchers.FindAsync(id);
        if (v is null) return false;
        db.Vouchers.Remove(v);
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Tính số tiền giảm + freeship từ voucher. Trả về (discountAmt, freeshipAmt, error)</summary>
    public async Task<(double discount, double freeship, string error)> CalcVoucherAsync(
        string code, string userId, double orderTotal, bool isVip)
    {
        var v = await db.Vouchers.Include(v => v.Usages)
            .FirstOrDefaultAsync(x => x.Code == code.ToUpperInvariant() && x.IsActive && x.ExpiredAt > DateTime.UtcNow);
        if (v is null) return (0, 0, "Mã voucher không hợp lệ hoặc đã hết hạn.");
        if (v.VipOnly && !isVip) return (0, 0, "Voucher này chỉ dành cho thành viên VIP.");
        if (v.MinOrderVnd > 0 && orderTotal < v.MinOrderVnd)
            return (0, 0, $"Đơn hàng tối thiểu {v.MinOrderVnd:N0}đ để dùng voucher này.");
        if (v.MaxUsesTotal > 0 && v.UsedCount >= v.MaxUsesTotal)
            return (0, 0, "Voucher đã hết lượt sử dụng.");
        if (v.MaxUsesPerUser > 0)
        {
            var used = v.Usages.Count(u => u.UserId == userId);
            if (used >= v.MaxUsesPerUser) return (0, 0, $"Bạn đã dùng hết {v.MaxUsesPerUser} lượt của voucher này.");
        }
        return v.Type switch
        {
            VoucherType.Percent  => (Math.Round(orderTotal * v.DiscountPercent / 100), 0, ""),
            VoucherType.Fixed    => (Math.Min(v.DiscountAmount, orderTotal), 0, ""),
            VoucherType.Freeship => (0, v.ShipAmount, ""),
            _ => (0, 0, "")
        };
    }

    private async Task RecordVoucherUsageAsync(string code, string userId)
    {
        var v = await db.Vouchers.FirstOrDefaultAsync(x => x.Code == code);
        if (v is null) return;
        v.UsedCount++;
        db.VoucherUsages.Add(new VoucherUsage { VoucherId = v.Id, UserId = userId });
        await db.SaveChangesAsync();
    }

    // ─── Phí vận chuyển ────────────────────────────────────────────────────────
    // < 200k  → 35.000đ  |  200k–500k → 25.000đ  |  > 500k → 20.000đ
    public static double CalcShipFee(double subtotal) => subtotal switch
    {
        < 200_000  => 35_000,
        < 500_000  => 25_000,
        _          => 20_000,
    };

    // Orders
    /// <param name="voucherCode">Voucher giảm giá (Percent hoặc Fixed)</param>
    /// <param name="freeshipCode">Voucher freeship riêng biệt</param>
    public async Task<(Order? order, string error)> PlaceOrderAsync(
        ApplicationUser user, Dictionary<int, int> cart,
        string shippingAddress, string receiverName, string receiverPhone,
        string? voucherCode, string? freeshipCode = null)
    {
        if (string.IsNullOrWhiteSpace(shippingAddress)) return (null, "Vui lòng nhập địa chỉ giao hàng.");
        if (string.IsNullOrWhiteSpace(receiverName)) return (null, "Vui lòng nhập tên người nhận.");
        if (string.IsNullOrWhiteSpace(receiverPhone)) return (null, "Vui lòng nhập số điện thoại.");

        var products = await db.Products.Where(p => cart.Keys.Contains(p.Id)).ToListAsync();
        var order = new Order
        {
            UserId = user.Id, Status = "Pending",
            ShippingAddress = shippingAddress,
            ReceiverName = receiverName,
            ReceiverPhone = receiverPhone,
            AppliedVoucherCode  = string.IsNullOrWhiteSpace(voucherCode)  ? null : voucherCode.ToUpperInvariant(),
            AppliedFreeshipCode = string.IsNullOrWhiteSpace(freeshipCode) ? null : freeshipCode.ToUpperInvariant(),
        };

        foreach (var product in products)
        {
            var qty = cart[product.Id];
            if (qty <= 0 || product.Stock < qty) continue;
            product.Stock -= qty;
            order.Items.Add(new OrderItem { ProductId = product.Id, Quantity = qty, UnitPriceVnd = product.PriceVnd });
            order.TotalVnd += qty * product.PriceVnd;
        }

        if (order.Items.Count == 0) return (null, "Không có sản phẩm hợp lệ trong giỏ.");

        var isVip = await governance.IsVipAsync(user);

        // Phí ship ban đầu
        var shipFee = CalcShipFee(order.TotalVnd);

        // Áp voucher giảm giá (Percent / Fixed) — không áp Freeship ở đây
        if (!string.IsNullOrWhiteSpace(voucherCode))
        {
            var (discount, _, err) = await CalcVoucherAsync(voucherCode, user.Id, order.TotalVnd, isVip);
            if (string.IsNullOrEmpty(err))
            {
                order.TotalVnd = Math.Max(0, order.TotalVnd - discount);
                await RecordVoucherUsageAsync(voucherCode.ToUpperInvariant(), user.Id);
            }
        }

        // Áp voucher freeship riêng
        if (!string.IsNullOrWhiteSpace(freeshipCode))
        {
            var (_, freeship, err2) = await CalcVoucherAsync(freeshipCode, user.Id, order.TotalVnd, isVip);
            if (string.IsNullOrEmpty(err2) && freeship > 0)
            {
                shipFee = Math.Max(0, shipFee - freeship);
                await RecordVoucherUsageAsync(freeshipCode.ToUpperInvariant(), user.Id);
            }
        }

        order.ShipFeeVnd = shipFee;
        order.TotalVnd  += shipFee;

        db.Orders.Add(order);
        await db.SaveChangesAsync();
        await governance.ApplySpendingAndVipRuleAsync(user, order.TotalVnd);
        return (order, "");
    }

    public async Task<bool> CancelOrderAsync(int orderId, string userId)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId);
        if (order is null || order.Status != "Pending") return false;
        order.Status = "Cancelled";
        var items = await db.OrderItems.Include(i => i.Product).Where(i => i.OrderId == orderId).ToListAsync();
        foreach (var item in items)
            if (item.Product != null) item.Product.Stock += item.Quantity;
        await db.SaveChangesAsync();
        // Hủy khi Pending = chưa xác nhận → KHÔNG trừ uy tín
        return true;
    }

    public Task<List<Order>> GetOrdersByUserAsync(string userId) =>
        db.Orders.Include(o => o.Items).ThenInclude(i => i.Product)
                 .Where(o => o.UserId == userId)
                 .OrderByDescending(o => o.CreatedAt).ToListAsync();

    public Task<List<Order>> GetAllOrdersAsync() =>
        db.Orders.Include(o => o.User).Include(o => o.Items).ThenInclude(i => i.Product)
                 .OrderByDescending(o => o.CreatedAt).ToListAsync();

    public async Task<bool> UpdateOrderStatusAsync(int orderId, string status)
    {
        var order = await db.Orders.FindAsync(orderId);
        if (order is null) return false;

        var oldStatus = order.Status;
        order.Status = status;

        // Khi chuyển sang Returned: hoàn kho sản phẩm + ghi nhận hành vi xấu
        if (status == "Returned" && oldStatus != "Returned")
        {
            var items = await db.OrderItems.Include(i => i.Product)
                .Where(i => i.OrderId == orderId).ToListAsync();
            foreach (var item in items)
                if (item.Product != null)
                    item.Product.Stock += item.Quantity;
            // Hoàn hàng / không nhận hàng → trừ uy tín
            if (order.UserId != null)
                await governance.RecordBadBehaviorAsync(order.UserId, "Returned");
        }
        // Nếu hủy Returned (chuyển sang status khác): trừ lại kho
        else if (oldStatus == "Returned" && status != "Returned")
        {
            var items = await db.OrderItems.Include(i => i.Product)
                .Where(i => i.OrderId == orderId).ToListAsync();
            foreach (var item in items)
                if (item.Product != null)
                    item.Product.Stock = Math.Max(0, item.Product.Stock - item.Quantity);
        }

        await db.SaveChangesAsync();
        return true;
    }

    public async Task<int> GetReturnedOrderCountAsync()
    {
        var orders = await db.Orders.ToListAsync();
        return orders.Count(o => o.Status == "Returned");
    }

    public async Task<double> GetReturnedRevenueAsync()
    {
        var orders = await db.Orders.Where(o => o.Status == "Returned").ToListAsync();
        return orders.Sum(o => o.TotalVnd);
    }

    // Chat
    public Task<List<ChatMessage>> GetChatMessagesAsync(string userId) =>
        db.ChatMessages.Where(m => m.UserId == userId)
                       .OrderBy(m => m.SentAt).ToListAsync();

    // Lấy danh sách user đã có tin nhắn (dùng cho staff inbox)
    public async Task<List<ChatInboxItem>> GetChatInboxAsync()
    {
        // Tải về client rồi group - SQLite EF Core không hỗ trợ GroupBy + First() server-side
        var allMessages = await db.ChatMessages
            .Include(m => m.User)
            .OrderByDescending(m => m.SentAt)
            .ToListAsync();

        return allMessages
            .GroupBy(m => m.UserId)
            .Select(g =>
            {
                var last = g.First(); // đã OrderByDescending ở trên
                return new ChatInboxItem(
                    g.Key,
                    last.User?.FullName ?? "?",
                    last.User?.Email ?? "",
                    last.Content,
                    last.SentAt,
                    last.IsFromStaff || last.IsFromAI
                );
            })
            .OrderByDescending(x => x.SentAt)
            .ToList();
    }

    public async Task<ChatMessage> SendMessageAsync(string userId, string content, bool isFromStaff = false, bool isFromAI = false)
    {
        var msg = new ChatMessage { UserId = userId, Content = content, IsFromStaff = isFromStaff, IsFromAI = isFromAI };
        db.ChatMessages.Add(msg);
        await db.SaveChangesAsync();
        return msg;
    }

    // Report
    public async Task<ShopReport> BuildReportAsync()
    {
        // Tải về client rồi tính - tránh mọi vấn đề SQLite EF Core với aggregate
        var orders   = await db.Orders.Select(o => new { o.Status, o.TotalVnd }).ToListAsync();
        var users    = await db.Users.Select(u => new { u.TotalSpentVnd, u.IsBlocked }).ToListAsync();
        var products = await db.Products.Select(p => new { p.IsActive }).ToListAsync();
        var vouchers = await db.Vouchers.Select(v => new { v.IsActive, v.ExpiredAt }).ToListAsync();

        var revenue      = orders.Where(o => o.Status != "Cancelled").Sum(o => o.TotalVnd);
        var orderCount   = orders.Count;
        var vipCount     = users.Count(u => u.TotalSpentVnd >= 1_000_000 && !u.IsBlocked);
        var blockedCount = users.Count(u => u.IsBlocked);
        var productCount = products.Count(p => p.IsActive);
        var voucherCount = vouchers.Count(v => v.IsActive && v.ExpiredAt > DateTime.UtcNow);

        return new ShopReport(revenue, orderCount, vipCount, blockedCount, productCount, voucherCount);
    }

    public async Task<Dictionary<int, int>> GetUserVoucherUsagesAsync(string userId)
    {
        var usages = await db.VoucherUsages.Where(u => u.UserId == userId).ToListAsync();
        return usages.GroupBy(u => u.VoucherId).ToDictionary(g => g.Key, g => g.Count());
    }

    // Reviews
    public Task<List<ProductReview>> GetReviewsAsync(int productId, bool includeHidden = false) =>
        db.ProductReviews
          .Include(r => r.User)
          .Where(r => r.ProductId == productId && (includeHidden || !r.IsHidden))
          .OrderByDescending(r => r.CreatedAt)
          .ToListAsync();

    public async Task<bool> HasPurchasedAsync(int productId, string userId)
    {
        var items = await db.OrderItems
            .Include(i => i.Order)
            .Where(i => i.ProductId == productId && i.Order != null && i.Order.UserId == userId && i.Order.Status == "Delivered")
            .AnyAsync();
        return items;
    }

    public async Task<ProductReview?> GetUserReviewAsync(int productId, string userId) =>
        await db.ProductReviews.FirstOrDefaultAsync(r => r.ProductId == productId && r.UserId == userId);

    public async Task<ProductReview> UpsertReviewAsync(int productId, string userId, int stars, string comment)
    {
        var existing = await GetUserReviewAsync(productId, userId);
        var verified = await HasPurchasedAsync(productId, userId);
        if (existing != null)
        {
            existing.Stars = stars; existing.Comment = comment; existing.IsVerifiedPurchase = verified;
            await db.SaveChangesAsync(); return existing;
        }
        var r = new ProductReview { ProductId = productId, UserId = userId, Stars = stars, Comment = comment, IsVerifiedPurchase = verified };
        db.ProductReviews.Add(r);
        await db.SaveChangesAsync();
        return r;
    }

    public async Task DeleteReviewAsync(int id)
    {
        var r = await db.ProductReviews.FindAsync(id);
        if (r != null) { db.ProductReviews.Remove(r); await db.SaveChangesAsync(); }
    }

    public async Task ReportReviewAsync(int id)
    {
        var r = await db.ProductReviews.FindAsync(id);
        if (r != null) { r.IsReported = true; await db.SaveChangesAsync(); }
    }

    public async Task HideReviewAsync(int id)
    {
        var r = await db.ProductReviews.FindAsync(id);
        if (r != null) { r.IsHidden = true; await db.SaveChangesAsync(); }
    }

    public Task<List<ProductReview>> GetAllReportedReviewsAsync() =>
        db.ProductReviews.Include(r => r.User).Include(r => r.Product)
          .Where(r => r.IsReported).OrderByDescending(r => r.CreatedAt).ToListAsync();

    // Product detail
    public Task<Product?> GetProductByIdAsync(int id)
        => db.Products.FirstOrDefaultAsync(p => p.Id == id);

    public async Task<int> GetProductSoldCountAsync(int id)
    {
        var items = await db.OrderItems
            .Where(i => i.ProductId == id)
            .ToListAsync();
        return items.Sum(i => i.Quantity);
    }

    public async Task<List<Product>> GetRelatedProductsAsync(string category, int excludeId, int take = 5)
        => await db.Products
            .Where(p => p.IsActive && p.Category == category && p.Id != excludeId)
            .Take(take)
            .ToListAsync();
}

public record ChatInboxItem(string UserId, string FullName, string Email, string LastMsg, DateTime SentAt, bool IsReplied);
