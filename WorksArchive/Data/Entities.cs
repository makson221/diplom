namespace WorksArchive.Data;

public enum UserRole
{
    Teacher = 0,
    Admin = 1
}

public enum WorkType
{
    Coursework = 0,
    Diploma = 1
}

public static class EnumText
{
    private static readonly TimeZoneInfo Kyiv = FindKyiv();

    private static TimeZoneInfo FindKyiv()
    {
        foreach (var id in new[] { "Europe/Kyiv", "Europe/Kiev", "FLE Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { /* пробуємо наступний */ }
        }
        return TimeZoneInfo.Local;
    }

    /// <summary>UTC-час з БД → київський час для показу (сервер на хостингу працює в UTC).</summary>
    public static string ToKyivText(this DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Kyiv).ToString("dd.MM.yyyy HH:mm");

    public static string ToUa(this WorkType t) => t switch
    {
        WorkType.Coursework => "Курсова робота",
        WorkType.Diploma => "Дипломна робота",
        _ => t.ToString()
    };

    public static string ToUa(this UserRole r) => r switch
    {
        UserRole.Admin => "Адміністратор",
        UserRole.Teacher => "Викладач",
        _ => r.ToString()
    };
}

/// <summary>Користувач системи (викладач або адміністратор).</summary>
public class AppUser
{
    public int Id { get; set; }
    public string Login { get; set; } = "";
    public string FullName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Змінюється при зміні пароля/ролі/блокуванні — старі сесії стають недійсними.</summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Академічна група, наприклад "КН-41".</summary>
public class StudyGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Work> Works { get; set; } = new();
}

/// <summary>Курсова або дипломна робота. Сам файл лежить у хмарному сховищі, тут — метадані.</summary>
public class Work
{
    public int Id { get; set; }
    public string StudentName { get; set; } = "";
    public string Title { get; set; } = "";
    public WorkType Type { get; set; }

    public int GroupId { get; set; }
    public StudyGroup Group { get; set; } = null!;

    /// <summary>Рік здачі.</summary>
    public int Year { get; set; }

    public string OriginalFileName { get; set; } = "";
    public string StoragePath { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Хто завантажив: ПІБ викладача або null, якщо завантажив студент.</summary>
    public string? UploadedBy { get; set; }
}

/// <summary>Налаштування, які адміністратор змінює з панелі (ключ-значення).</summary>
public class AppSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
