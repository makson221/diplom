using Microsoft.Extensions.Options;

namespace WorksArchive.Services;

/// <summary>
/// Вибір сховища. Адміністратор перемикає «активне» сховище в адмін-панелі — туди йдуть НОВІ роботи.
/// Кожна робота пам'ятає своє сховище (Work.StorageProvider), тому старі файли лишаються доступними.
/// </summary>
public class StorageResolver(IServiceProvider sp, SettingsService settings, IOptions<StorageOptions> options)
{
    public const string ActiveKey = "Storage.Active";
    public const string QuotaKey = "Storage.QuotaGb";

    public const string Local = "Local";
    public const string GoogleDrive = "GoogleDrive";
    public const string S3 = "S3";
    public const string Supabase = "Supabase";

    public static readonly string[] All = [Local, GoogleDrive, S3, Supabase];

    public static string Title(string name) => name switch
    {
        Local => "Диск сервера",
        GoogleDrive => "Google Drive",
        S3 => "S3-сумісне сховище",
        Supabase => "Supabase Storage",
        _ => name
    };

    public static string? Normalize(string? name) =>
        All.FirstOrDefault(n => string.Equals(n, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Чи можна використовувати сховище (S3 і Supabase потребують ключів у конфігурації).</summary>
    public bool IsAvailable(string name)
    {
        var o = options.Value;
        return name switch
        {
            Local => o.AllowLocal,
            GoogleDrive => true,
            S3 => !string.IsNullOrWhiteSpace(o.S3.ServiceUrl) && !string.IsNullOrWhiteSpace(o.S3.AccessKey)
                  && !string.IsNullOrWhiteSpace(o.S3.SecretKey) && !string.IsNullOrWhiteSpace(o.S3.Bucket),
            Supabase => !string.IsNullOrWhiteSpace(o.Supabase.Url) && !string.IsNullOrWhiteSpace(o.Supabase.ServiceKey),
            _ => false
        };
    }

    public async Task<string> ActiveNameAsync()
    {
        var fromSettings = Normalize(await settings.GetAsync(ActiveKey));
        if (fromSettings is not null && IsAvailable(fromSettings)) return fromSettings;
        var fromConfig = Normalize(options.Value.Provider);
        return fromConfig is not null && IsAvailable(fromConfig) ? fromConfig
            : IsAvailable(Local) ? Local : GoogleDrive;
    }

    public async Task<IFileStorage> ActiveAsync() => Get(await ActiveNameAsync());

    public IFileStorage Get(string? name) => Normalize(name) switch
    {
        GoogleDrive => sp.GetRequiredService<GoogleDriveStorage>(),
        S3 => sp.GetRequiredService<S3Storage>(),
        Supabase => sp.GetRequiredService<SupabaseStorage>(),
        _ => sp.GetRequiredService<LocalFileStorage>()
    };

    /// <summary>Ліміт для індикатора заповненості (для сховищ, які не повідомляють квоту самі).</summary>
    public async Task<double> QuotaGbAsync() =>
        double.TryParse(await settings.GetAsync(QuotaKey), System.Globalization.CultureInfo.InvariantCulture, out var gb) && gb > 0
            ? gb : options.Value.QuotaGb;
}
