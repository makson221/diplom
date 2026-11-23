using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WorksArchive.Services;

namespace WorksArchive.Data;

/// <summary>Чи потрібно ще створити першого адміністратора.</summary>
public class SetupState
{
    public volatile bool NeedsSetup;
}

public static class DbInitializer
{
    /// <summary>Створює таблиці (якщо їх ще немає) і, якщо задано Admin:Password, першого адміністратора.</summary>
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var config = sp.GetRequiredService<IConfiguration>();
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DbInitializer");

        await db.Database.EnsureCreatedAsync();

        var password = config["Admin:Password"];
        if (!await db.Users.AnyAsync() && !string.IsNullOrWhiteSpace(password))
        {
            // Адміністратор із конфігурації (зручно для хостингів зі змінними середовища, напр. Render).
            var login = config["Admin:Login"] ?? "admin";
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

        // Якщо користувачів немає — сайт покаже майстер першого запуску (/Setup).
        sp.GetRequiredService<SetupState>().NeedsSetup = !await db.Users.AnyAsync();
        if (sp.GetRequiredService<SetupState>().NeedsSetup)
            log.LogWarning("Немає жодного користувача — відкрийте сайт і створіть адміністратора на сторінці /Setup");

        var storages = sp.GetRequiredService<StorageResolver>();
        await (await storages.ActiveAsync()).EnsureReadyAsync();
    }
}
