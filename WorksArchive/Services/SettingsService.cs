using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WorksArchive.Data;

namespace WorksArchive.Services;

/// <summary>Налаштування, що змінюються з адмін-панелі (кешуються в пам'яті, бо тема читається на кожній сторінці).</summary>
public class SettingsService(AppDbContext db, IMemoryCache cache)
{
    public const string AllowStudentUploadKey = "AllowStudentUpload";
    public const string StudentUploadCodeKey = "StudentUploadCode";
    public const string DefaultAccentKey = "DefaultAccent";
    public const string DefaultModeKey = "DefaultMode";

    private const string CacheKey = "app-settings";

    private async Task<Dictionary<string, string>> AllAsync() =>
        (await cache.GetOrCreateAsync(CacheKey, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value);
        }))!;

    public async Task<string?> GetAsync(string key) =>
        (await AllAsync()).GetValueOrDefault(key);

    public async Task SetAsync(string key, string value)
    {
        var s = await db.Settings.FindAsync(key);
        if (s is null) db.Settings.Add(new AppSetting { Key = key, Value = value });
        else s.Value = value;
        await db.SaveChangesAsync();
        cache.Remove(CacheKey);
    }

    /// <summary>Чи можуть студенти самі здавати роботи (за замовчуванням — так).</summary>
    public async Task<bool> AllowStudentUploadAsync() => await GetAsync(AllowStudentUploadKey) != "false";

    /// <summary>Код доступу для студентів (порожньо — без коду).</summary>
    public async Task<string> StudentUploadCodeAsync() => await GetAsync(StudentUploadCodeKey) ?? "";

    /// <summary>Тема за замовчуванням для всіх, хто ще не обрав свою.</summary>
    public async Task<(ThemeAccent Accent, string Mode)> DefaultThemeAsync() =>
        (Themes.FindAccent(await GetAsync(DefaultAccentKey)), Themes.NormalizeMode(await GetAsync(DefaultModeKey)));
}
