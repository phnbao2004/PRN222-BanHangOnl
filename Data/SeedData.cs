using MiniShopee.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MiniShopee.Data;

public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        await db.Database.EnsureCreatedAsync();

        foreach (var role in new[] { "Admin", "Staff", "Customer", "VipCustomer" })
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));

        await CreateUser(userManager, "admin@local",    "Admin123$",    "Quản trị viên", new[] { "Admin" },                      "123 Nguyễn Huệ, Q1, HCM",         "0901111111");
        await CreateUser(userManager, "staff@local",    "Staff123$",    "Nhân viên A",   new[] { "Staff" },                      "456 Lê Lợi, Q1, HCM",             "0902222222");
        await CreateUser(userManager, "customer@local", "Customer123$", "Nguyễn Văn A",  new[] { "Customer" },                   "789 Trần Hưng Đạo, Q5, HCM",      "0903333333");
        // VIP có cả 2 role: Customer + VipCustomer — để đủ quyền mua sắm + hưởng ưu đãi VIP
        await CreateUser(userManager, "vip@local",      "Vip1234$",      "Nguyễn VIP",    new[] { "Customer", "VipCustomer" },    "101 Võ Văn Tần, Q3, HCM",        "0904444444", spent: 1_500_000);

        if (!await db.Products.AnyAsync())
        {
            db.Products.AddRange(
                new Product { Name = "Áo thun basic",     Description = "Cotton 100%, co giãn 4 chiều, thoáng mát cả ngày",               Category = "Áo thun",  PriceVnd = 150_000, Stock = 80 },
                new Product { Name = "Áo polo premium",   Description = "Vải cá sấu cao cấp, kháng khuẩn, giữ form tốt",                  Category = "Áo polo",  PriceVnd = 320_000, Stock = 60 },
                new Product { Name = "Áo sơ mi công sở",  Description = "Chống nhăn, dễ phối đồ, phù hợp môi trường văn phòng",           Category = "Áo sơ mi", PriceVnd = 420_000, Stock = 45 },
                new Product { Name = "Áo hoodie unisex",  Description = "Nỉ bông dày, giữ ấm, form rộng thời trang",                       Category = "Áo hoodie",PriceVnd = 490_000, Stock = 40 },
                new Product { Name = "Áo khoác bomber",   Description = "Phong cách streetwear, vải dù nhẹ bền màu",                       Category = "Áo khoác", PriceVnd = 650_000, Stock = 30 },
                new Product { Name = "Áo tank top sport", Description = "Vải thun lạnh, thấm hút mồ hôi, dành cho thể thao",              Category = "Áo thun",  PriceVnd = 180_000, Stock = 70 }
            );
        }

        if (!await db.Vouchers.AnyAsync())
        {
            db.Vouchers.AddRange(
                new Voucher { Code = "WELCOME10",  Description = "Giảm 10% cho đơn hàng đầu tiên",              Type = VoucherType.Percent,  DiscountPercent = 10, MinOrderVnd = 0,        MaxUsesTotal = 200, MaxUsesPerUser = 1, VipOnly = false, CreatedByRole = "Admin", ExpiredAt = DateTime.UtcNow.AddMonths(6) },
                new Voucher { Code = "VIP20",      Description = "Ưu đãi 20% đặc biệt cho thành viên VIP",      Type = VoucherType.Percent,  DiscountPercent = 20, MinOrderVnd = 0,        MaxUsesTotal = 100, MaxUsesPerUser = 2, VipOnly = true,  CreatedByRole = "Admin", ExpiredAt = DateTime.UtcNow.AddMonths(3) },
                new Voucher { Code = "SALE15",     Description = "Khuyến mãi mùa hè giảm 15%",                  Type = VoucherType.Percent,  DiscountPercent = 15, MinOrderVnd = 100_000,  MaxUsesTotal = 150, MaxUsesPerUser = 1, VipOnly = false, CreatedByRole = "Admin", ExpiredAt = DateTime.UtcNow.AddDays(30) },
                new Voucher { Code = "GIAM50K",    Description = "Giảm 50.000đ cho đơn từ 300.000đ",            Type = VoucherType.Fixed,    DiscountAmount = 50_000, MinOrderVnd = 300_000, MaxUsesTotal = 100, MaxUsesPerUser = 1, VipOnly = false, CreatedByRole = "Admin", ExpiredAt = DateTime.UtcNow.AddMonths(2) },
                new Voucher { Code = "FREESHIP",   Description = "Miễn phí vận chuyển cho đơn từ 150.000đ",     Type = VoucherType.Freeship, ShipAmount = 30_000, MinOrderVnd = 150_000,   MaxUsesTotal = 300, MaxUsesPerUser = 3, VipOnly = false, CreatedByRole = "Admin", ExpiredAt = DateTime.UtcNow.AddMonths(1) }
            );
        }

        await db.SaveChangesAsync();
    }

    private static async Task CreateUser(
        UserManager<ApplicationUser> um,
        string email, string pass, string name,
        string[] roles, string address = "", string phone = "",
        double spent = 0)
    {
        if (await um.FindByEmailAsync(email) is not null) return;
        var user = new ApplicationUser
        {
            UserName = email, Email = email, FullName = name,
            TotalSpentVnd = spent, EmailConfirmed = true,
            DefaultAddress = address, DefaultPhone = phone
        };
        var result = await um.CreateAsync(user, pass);
        if (result.Succeeded)
            foreach (var role in roles)
                await um.AddToRoleAsync(user, role);
    }
}
