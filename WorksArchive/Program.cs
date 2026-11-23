using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using WorksArchive.Data;
using WorksArchive.Services;

var builder = WebApplication.CreateBuilder(args);

// Необов'язковий локальний файл із секретами (не комітиться в git).
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// ---------- База даних (Supabase PostgreSQL у хмарі, або SQLite локально) ----------
var dbProvider = builder.Configuration["Database:Provider"] ?? "Postgres";
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Не задано ConnectionStrings:Default");
Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "App_Data"));
builder.Services.AddDbContext<AppDbContext>(o =>
{
    if (dbProvider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase)) o.UseSqlite(connectionString);
    else o.UseNpgsql(connectionString);
});

// ---------- Файлове сховище (Supabase Storage або локальна папка) ----------
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.Configure<UploadOptions>(builder.Configuration.GetSection("Upload"));
var storageProvider = (builder.Configuration["Storage:Provider"] ?? "GoogleDrive").ToLowerInvariant();
switch (storageProvider)
{
    case "local":
        builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
        break;
    case "s3": // Backblaze B2, Oracle Cloud, Cloudflare R2, IDrive e2, MinIO...
        builder.Services.AddSingleton<S3Storage>();
        builder.Services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<S3Storage>());
        break;
    case "supabase":
        builder.Services.AddHttpClient<SupabaseStorage>();
        builder.Services.AddTransient<IFileStorage>(sp => sp.GetRequiredService<SupabaseStorage>());
        break;
    case "googledrive":
        builder.Services.AddHttpClient<GoogleDriveStorage>();
        builder.Services.AddScoped<IFileStorage>(sp => sp.GetRequiredService<GoogleDriveStorage>());
        break;
    default:
        throw new InvalidOperationException($"Невідомий Storage:Provider '{storageProvider}'. Допустимо: GoogleDrive, S3, Supabase, Local.");
}

builder.Services.AddScoped<WorkService>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<SettingsService>();

builder.Services.AddDataProtection()
    .SetApplicationName("WorksArchive")
    .PersistKeysToDbContext<AppDbContext>();

// ---------- Ліміти розміру файлів ----------
var maxMb = builder.Configuration.GetValue("Upload:MaxFileSizeMb", 50);
var maxBody = (maxMb + 2) * 1024L * 1024L;
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = maxBody);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = maxBody);

// ---------- Авторизація: вхід через cookie, ролі Admin / Teacher ----------
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        o.LogoutPath = "/Account/Logout";
        o.AccessDeniedPath = "/Account/AccessDenied";
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.Events.OnValidatePrincipal = async ctx =>
        {
            // Перевіряємо, що користувача не заблоковано/не змінено пароль з моменту входу.
            var id = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var stamp = ctx.Principal?.FindFirstValue("stamp");
            var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var ok = int.TryParse(id, out var userId) && await db.Users.AnyAsync(u =>
                u.Id == userId && u.IsActive && u.SecurityStamp == stamp);
            if (!ok)
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Teacher", p => p.RequireRole(nameof(UserRole.Teacher), nameof(UserRole.Admin)))
    .AddPolicy("Admin", p => p.RequireRole(nameof(UserRole.Admin)));

builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/Works", "Teacher");
    o.Conventions.AuthorizeFolder("/Admin", "Admin");
    o.Conventions.AuthorizePage("/Account/ChangePassword");
});

// Захист від підбору пароля: не більше 10 спроб входу на хвилину з однієї IP.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", ctx => HttpMethods.IsPost(ctx.Request.Method)
        ? RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) })
        : RateLimitPartition.GetNoLimiter("get"));
    o.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
        await ctx.HttpContext.Response.WriteAsync("Забагато спроб входу. Зачекайте хвилину і спробуйте знову.", ct);
    };
});

// Для хостингу за проксі (Render, Railway тощо).
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

await DbInitializer.InitializeAsync(app.Services);

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Українські формати чисел і дат ("2 457", "23.11.2026").
app.UseRequestLocalization(o =>
{
    o.SetDefaultCulture("uk-UA");
    o.RequestCultureProviders.Clear();
});

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

app.Run();
