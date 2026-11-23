namespace WorksArchive.Services;

/// <summary>Дані про файл, який зберігається.</summary>
/// <param name="Path">Технічний шлях (лише латиниця): рік/група/guid.ext</param>
/// <param name="FriendlyName">Зрозуміла назва файлу (ПІБ + оригінальна назва) — для Google Drive.</param>
/// <param name="Year">Рік здачі — для папок у Google Drive.</param>
/// <param name="GroupName">Назва групи — для папок у Google Drive.</param>
/// <param name="Category">"Курсові" або "Дипломні" — для папок у Google Drive.</param>
public record StoredFileInfo(string Path, string FriendlyName, string ContentType, long Size, int Year, string GroupName, string Category);

/// <summary>Скільки місця зайнято / доступно у сховищі (якщо сховище це повідомляє).</summary>
public record StorageQuota(long UsedBytes, long? LimitBytes);

/// <summary>Абстракція файлового сховища: Google Drive, S3-сумісне (Backblaze B2, Oracle, R2…), Supabase або локальна папка.</summary>
public interface IFileStorage
{
    /// <summary>Назва для адмін-панелі.</summary>
    string DisplayName { get; }

    Task EnsureReadyAsync(CancellationToken ct = default);

    /// <summary>Зберігає файл і повертає ключ, за яким його потім читати/видаляти.</summary>
    Task<string> UploadAsync(StoredFileInfo file, Stream content, CancellationToken ct = default);

    Task<Stream> OpenReadAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>Реальна квота від сховища (Google Drive) або null, якщо сховище її не повідомляє.</summary>
    Task<StorageQuota?> GetQuotaAsync(CancellationToken ct = default) => Task.FromResult<StorageQuota?>(null);
}

public class StorageOptions
{
    /// <summary>"GoogleDrive", "S3", "Supabase" або "Local".</summary>
    public string Provider { get; set; } = "GoogleDrive";

    /// <summary>Обсяг сховища в ГБ для індикатора заповненості (для S3/Supabase/Local, які квоту не повідомляють).</summary>
    public double QuotaGb { get; set; } = 10;

    public string LocalPath { get; set; } = "App_Data/files";
    public SupabaseOptions Supabase { get; set; } = new();
    public S3Options S3 { get; set; } = new();
    public GoogleDriveOptions GoogleDrive { get; set; } = new();
}

public class SupabaseOptions
{
    /// <summary>Наприклад https://abcdefgh.supabase.co</summary>
    public string Url { get; set; } = "";
    /// <summary>service_role ключ (секретний! тільки на сервері).</summary>
    public string ServiceKey { get; set; } = "";
    public string Bucket { get; set; } = "works";
}

public class S3Options
{
    /// <summary>Endpoint, напр. https://s3.eu-central-003.backblazeb2.com</summary>
    public string ServiceUrl { get; set; } = "";
    public string Region { get; set; } = "auto";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string Bucket { get; set; } = "";
    /// <summary>Шлях виду endpoint/bucket/key (потрібно для Oracle, MinIO). Для B2/R2 підходить і true.</summary>
    public bool ForcePathStyle { get; set; } = true;
}

public class GoogleDriveOptions
{
    /// <summary>OAuth Client ID з Google Cloud Console (тип "Web application").</summary>
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    /// <summary>Назва кореневої папки на Google Drive.</summary>
    public string RootFolderName { get; set; } = "Архів робіт";

    // Адреси API — змінюються лише для тестів.
    public string AuthUrl { get; set; } = "https://accounts.google.com/o/oauth2/v2/auth";
    public string TokenUrl { get; set; } = "https://oauth2.googleapis.com/token";
    public string ApiUrl { get; set; } = "https://www.googleapis.com/drive/v3/";
    public string UploadUrl { get; set; } = "https://www.googleapis.com/upload/drive/v3/";
}

public class StorageException(string message) : Exception(message);
