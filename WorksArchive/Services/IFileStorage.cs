namespace WorksArchive.Services;

/// <summary>Абстракція файлового сховища (Supabase Storage у хмарі або локальна папка для розробки).</summary>
public interface IFileStorage
{
    Task EnsureReadyAsync(CancellationToken ct = default);
    Task UploadAsync(string path, Stream content, string contentType, CancellationToken ct = default);
    Task<Stream> OpenReadAsync(string path, CancellationToken ct = default);
    Task DeleteAsync(string path, CancellationToken ct = default);
}

public class StorageOptions
{
    /// <summary>"Supabase" або "Local".</summary>
    public string Provider { get; set; } = "Supabase";
    public string LocalPath { get; set; } = "App_Data/files";
    public SupabaseOptions Supabase { get; set; } = new();
}

public class SupabaseOptions
{
    /// <summary>Наприклад https://abcdefgh.supabase.co</summary>
    public string Url { get; set; } = "";
    /// <summary>service_role ключ (секретний! тільки на сервері).</summary>
    public string ServiceKey { get; set; } = "";
    public string Bucket { get; set; } = "works";
}
