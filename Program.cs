using MiniShopee.Components;
using MiniShopee.Data;
using MiniShopee.Hubs;
using MiniShopee.Models;
using MiniShopee.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=minishopee.db"));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services
    .AddIdentityCore<ApplicationUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.SignIn.RequireConfirmedAccount = false;
        o.Password.RequireDigit = true;
        o.Password.RequireUppercase = true;
        o.Password.RequireLowercase = true;
        o.Password.RequireNonAlphanumeric = true;
        o.Password.RequiredLength = 8;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();

builder.Services.AddAuthentication(o =>
{
    o.DefaultScheme = IdentityConstants.ApplicationScheme;
    o.DefaultSignInScheme = IdentityConstants.ExternalScheme;
}).AddIdentityCookies();

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// ── SignalR ──────────────────────────────────────────────────────────
builder.Services.AddSignalR(o =>
{
    o.EnableDetailedErrors = builder.Environment.IsDevelopment();
    o.MaximumReceiveMessageSize = 64 * 1024;
});

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddScoped<CommerceService>();
builder.Services.AddScoped<UserGovernanceService>();
builder.Services.AddScoped<CartState>();

var app = builder.Build();

// Đảm bảo DB được tạo với schema mới nhất
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment()) app.UseMigrationsEndPoint();
else { app.UseExceptionHandler("/Error", createScopeForErrors: true); app.UseHsts(); }

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

await SeedData.InitializeAsync(app.Services);

// ── Auth endpoints ───────────────────────────────────────────────────
app.MapPost("/auth/login", async (LoginRequest req, SignInManager<ApplicationUser> sm, UserManager<ApplicationUser> um) =>
{
    var user = await um.FindByEmailAsync(req.Email);
    if (user is null || user.IsBlocked) return Results.BadRequest("Tài khoản không tồn tại hoặc đã bị khóa.");
    var r = await sm.PasswordSignInAsync(user, req.Password, true, false);
    return r.Succeeded ? Results.Ok() : Results.BadRequest("Sai thông tin đăng nhập.");
});

app.MapPost("/auth/logout", async (SignInManager<ApplicationUser> sm) => { await sm.SignOutAsync(); return Results.Ok(); });

app.MapPost("/auth/login-form", async (HttpContext ctx, SignInManager<ApplicationUser> sm, UserManager<ApplicationUser> um) =>
{
    var f = await ctx.Request.ReadFormAsync();
    var user = await um.FindByEmailAsync(f["email"].ToString().Trim());
    if (user is null || user.IsBlocked) return Results.Redirect("/login?error=blocked");
    var r = await sm.PasswordSignInAsync(user, f["password"].ToString(), true, false);
    return r.Succeeded ? Results.Redirect("/") : Results.Redirect("/login?error=bad_credentials");
});

app.MapPost("/auth/register-form", async (HttpContext ctx, UserManager<ApplicationUser> um, SignInManager<ApplicationUser> sm) =>
{
    var f = await ctx.Request.ReadFormAsync();
    var fullName = f["fullName"].ToString().Trim();
    var email    = f["email"].ToString().Trim();
    var password = f["password"].ToString();
    var confirm  = f["confirmPassword"].ToString();

    if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        return Results.Redirect("/register?error=missing_fields");
    if (password != confirm) return Results.Redirect("/register?error=password_mismatch");
    if (await um.FindByEmailAsync(email) is not null) return Results.Redirect("/register?error=email_exists");

    var user = new ApplicationUser { UserName = email, Email = email, FullName = fullName, EmailConfirmed = true, AvatarUrl = "" };
    var cr = await um.CreateAsync(user, password);
    if (!cr.Succeeded) return Results.Redirect("/register?error=invalid_password");
    await um.AddToRoleAsync(user, "Customer");
    await sm.SignInAsync(user, true);
    return Results.Redirect("/");
});

app.MapPost("/auth/logout-form", async (SignInManager<ApplicationUser> sm) =>
    { await sm.SignOutAsync(); return Results.Redirect("/login"); });

// ── SignalR auth token endpoint ─────────────────────────────────────
// Trả về userId để client dùng làm access token cho hub connection
app.MapGet("/hubs/chat-user", (HttpContext ctx) =>
{
    var uid = ctx.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    return uid is not null ? Results.Ok(uid) : Results.Unauthorized();
}).RequireAuthorization();

// ── SignalR Hub ──────────────────────────────────────────────────────
app.MapHub<ChatHub>("/hubs/chat");

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

public record LoginRequest(string Email, string Password);
