using Microsoft.EntityFrameworkCore;
using WorksArchive.Data;

namespace WorksArchive.Services;

/// <summary>Налаштування, що змінюються з адмін-панелі.</summary>
public class SettingsService(AppDbContext db)
{
    public const string AllowStudentUploadKey = "AllowStudentUpload";
    public const string StudentUploadCodeKey = "StudentUploadCode";

    public async Task<string?> GetAsync(string key) =>
        (await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key))?.Value;

    public async Task SetAsync(string key, string value)
    {
        var s = await db.Settings.FindAsync(key);
        if (s is null) db.Settings.Add(new AppSetting { Key = key, Value = value });
        else s.Value = value;
        await db.SaveChangesAsync();
    }

    /// <summary>Чи можуть студенти самі здавати роботи (за замовчуванням — так).</summary>
    public async Task<bool> AllowStudentUploadAsync() => await GetAsync(AllowStudentUploadKey) != "false";

    /// <summary>Код доступу для студентів (порожньо — без коду).</summary>
    public async Task<string> StudentUploadCodeAsync() => await GetAsync(StudentUploadCodeKey) ?? "";
}
