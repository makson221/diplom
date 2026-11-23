using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace WorksArchive.Services;

/// <summary>
/// Робота з Supabase Storage через REST API.
/// Бакет приватний: файли доступні лише через цей сервер (з service_role ключем),
/// тобто тільки авторизованим викладачам.
/// </summary>
public class SupabaseStorage : IFileStorage
{
    private readonly HttpClient _http;
    private readonly string _bucket;
    private readonly ILogger<SupabaseStorage> _log;

    public SupabaseStorage(HttpClient http, IOptions<StorageOptions> options, ILogger<SupabaseStorage> log)
    {
        var o = options.Value.Supabase;
        if (string.IsNullOrWhiteSpace(o.Url) || string.IsNullOrWhiteSpace(o.ServiceKey))
            throw new InvalidOperationException("Не задано Storage:Supabase:Url або Storage:Supabase:ServiceKey.");

        _http = http;
        _http.BaseAddress = new Uri(o.Url.TrimEnd('/') + "/storage/v1/");
        _http.DefaultRequestHeaders.Add("apikey", o.ServiceKey);
        // Старі ключі service_role — це JWT ("eyJ..."), їх передаємо і як Bearer.
        // Нові секретні ключі (sb_secret_...) передаються лише в заголовку apikey.
        if (o.ServiceKey.StartsWith("eyJ", StringComparison.Ordinal))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", o.ServiceKey);
        _http.Timeout = TimeSpan.FromMinutes(10);
        _bucket = o.Bucket;
        _log = log;
    }

    public async Task EnsureReadyAsync(CancellationToken ct = default)
    {
        var check = await _http.GetAsync($"bucket/{_bucket}", ct);
        if (check.IsSuccessStatusCode) return;

        var create = await _http.PostAsJsonAsync("bucket", new { id = _bucket, name = _bucket, @public = false }, ct);
        if (create.IsSuccessStatusCode)
            _log.LogInformation("Створено приватний бакет '{Bucket}' у Supabase Storage", _bucket);
        else
            _log.LogWarning("Не вдалося перевірити/створити бакет '{Bucket}': {Status} {Body}",
                _bucket, (int)create.StatusCode, await create.Content.ReadAsStringAsync(ct));
    }

    public async Task UploadAsync(string path, Stream content, string contentType, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"object/{_bucket}/{path}");
        req.Content = new StreamContent(content);
        req.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        req.Headers.Add("x-upsert", "true");

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            throw new StorageException($"Помилка завантаження у сховище: {(int)resp.StatusCode} {await resp.Content.ReadAsStringAsync(ct)}");
    }

    public async Task<Stream> OpenReadAsync(string path, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"object/{_bucket}/{path}", HttpCompletionOption.ResponseHeadersRead, ct);
        if (resp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            resp.Dispose();
            throw new FileNotFoundException("Файл не знайдено у сховищі", path);
        }
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            resp.Dispose();
            throw new StorageException($"Помилка читання зі сховища: {(int)resp.StatusCode} {body}");
        }
        return await resp.Content.ReadAsStreamAsync(ct);
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, $"object/{_bucket}")
        {
            Content = JsonContent.Create(new { prefixes = new[] { path } })
        };
        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            _log.LogWarning("Не вдалося видалити '{Path}' зі сховища: {Status}", path, (int)resp.StatusCode);
    }
}

public class StorageException(string message) : Exception(message);
