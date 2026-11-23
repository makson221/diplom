using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WorksArchive.Services;

namespace WorksArchive.Data;

public static class DbInitializer
{
    /// <summary>Створює таблиці (якщо їх ще немає) і першого адміністратора.</summary>
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var config = sp.GetRequiredService<IConfiguration>();
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DbInitializer");

        await db.Database.EnsureCreatedAsync();

        if (!await db.Users.AnyAsync())
        {
            var login = config["Admin:Login"] ?? "admin";
            var password = config["Admin:Password"];
            if (string.IsNullOrWhiteSpace(password))
            {
                password = "Admin123!";
                log.LogWarning("Admin:Password не задано — створено адміністратора '{Login}' з паролем за замовчуванням. ЗМІНІТЬ ЙОГО після входу!", login);
            }

            var admin = new AppUser
            {
                Login = login,
                FullName = config["Admin:FullName"] ?? "Адміністратор",
                Role = UserRole.Admin
            };
            admin.PasswordHash = new PasswordHasher<AppUser>().HashPassword(admin, password);
            db.Users.Add(admin);
            await db.SaveChangesAsync();
            log.LogInformation("Створено першого адміністратора '{Login}'", login);
        }

        await sp.GetRequiredService<IFileStorage>().EnsureReadyAsync();
    }
}
