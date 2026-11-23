using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace WorksArchive.Services;

/// <summary>
/// Google Drive як сховище файлів (15 ГБ безкоштовно на один Google-акаунт).
/// Підключення: адміністратор один раз входить у Google через адмін-панель, застосунок
/// зберігає refresh-токен (зашифровано) у БД. Доступ обмежено правом drive.file —
/// застосунок бачить лише файли, які сам створив.
/// Структура на Drive: «Архів робіт / Рік / Група / Курсові|Дипломні / ПІБ - файл».
/// </summary>
public class GoogleDriveStorage : IFileStorage
{
    public const string Scope = "https://www.googleapis.com/auth/drive.file";
    public const string RefreshTokenKey = "GoogleDrive.RefreshToken";
    public const string AccountKey = "GoogleDrive.Account";
    private const string FolderMime = "application/vnd.google-apps.folder";
    private const string TokenCacheKey = "gd-access-token";

    private readonly HttpClient _http;
    private readonly GoogleDriveOptions _o;
    private readonly SettingsService _settings;
    private readonly IMemoryCache _cache;
    private readonly IDataProtector _protector;
    private readonly ILogger<GoogleDriveStorage> _log;

    public GoogleDriveStorage(HttpClient http, IOptions<StorageOptions> options, SettingsService settings,
        IMemoryCache cache, IDataProtectionProvider dataProtection, ILogger<GoogleDriveStorage> log)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromMinutes(10);
        _o = options.Value.GoogleDrive;
        _settings = settings;
        _cache = cache;
        _protector = dataProtection.CreateProtector("GoogleDrive.RefreshToken");
        _log = log;
    }

    public string DisplayName => "Google Drive";

    /// <summary>Чи задано ClientId/ClientSecret у конфігурації.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_o.ClientId) && !string.IsNullOrWhiteSpace(_o.ClientSecret);

    public async Task<bool> IsConnectedAsync() => !string.IsNullOrEmpty(await _settings.GetAsync(RefreshTokenKey));
    public Task<string?> ConnectedAccountAsync() => _settings.GetAsync(AccountKey);

    // ---------------- Підключення (OAuth 2.0) ----------------

    public string BuildAuthorizeUrl(string redirectUri, string state) =>
        $"{_o.AuthUrl}?client_id={E(_o.ClientId)}&redirect_uri={E(redirectUri)}&response_type=code" +
        $"&scope={E(Scope)}&access_type=offline&prompt=consent&include_granted_scopes=true&state={E(state)}";

    /// <summary>Обмінює код авторизації на refresh-токен і зберігає його.</summary>
    public async Task ConnectAsync(string code, string redirectUri, CancellationToken ct = default)
    {
        var token = await RequestTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = _o.ClientId,
            ["client_secret"] = _o.ClientSecret
        }, ct);

        if (string.IsNullOrEmpty(token.RefreshToken))
            throw new StorageException("Google не повернув refresh-токен. Спробуйте підключити ще раз.");

        await _settings.SetAsync(RefreshTokenKey, _protector.Protect(token.RefreshToken));
        _cache.Set(TokenCacheKey, token.AccessToken, TimeSpan.FromSeconds(Math.Max(60, token.ExpiresIn - 120)));
        ClearFolderCache();

        string account = "Google-акаунт";
        try
        {
            using var resp = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, _o.ApiUrl + "about?fields=user(emailAddress)"), ct);
            if (resp.IsSuccessStatusCode)
            {
                var about = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
                account = about.GetProperty("user").GetProperty("emailAddress").GetString() ?? account;
            }
        }
        catch (Exception ex) { _log.LogWarning(ex, "Не вдалося отримати email акаунта Google"); }
        await _settings.SetAsync(AccountKey, account);
    }

    public async Task DisconnectAsync()
    {
        await _settings.SetAsync(RefreshTokenKey, "");
        await _settings.SetAsync(AccountKey, "");
        _cache.Remove(TokenCacheKey);
        ClearFolderCache();
    }

    private record TokenResponse(string AccessToken, string? RefreshToken, int ExpiresIn);

    private async Task<TokenResponse> RequestTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var resp = await _http.PostAsync(_o.TokenUrl, new FormUrlEncodedContent(form), ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _log.LogWarning("Google OAuth помилка {Status}: {Body}", (int)resp.StatusCode, body);
            if (body.Contains("invalid_grant"))
                throw new StorageException("Доступ до Google Drive відкликано або він закінчився. Адміністратор має підключити Google Drive знову (Адмін-панель → Сховище).");
            throw new StorageException($"Помилка авторизації Google: {(int)resp.StatusCode}");
        }
        var json = JsonDocument.Parse(body).RootElement;
        return new TokenResponse(
            json.GetProperty("access_token").GetString()!,
            json.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
            json.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600);
    }

    private async Task<string> AccessTokenAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(TokenCacheKey, out string? cached) && cached is not null) return cached;

        var protectedToken = await _settings.GetAsync(RefreshTokenKey);
        if (string.IsNullOrEmpty(protectedToken))
            throw new StorageException("Google Drive не підключено. Адміністратор має підключити його: Адмін-панель → Сховище.");

        var token = await RequestTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = _protector.Unprotect(protectedToken),
            ["client_id"] = _o.ClientId,
            ["client_secret"] = _o.ClientSecret
        }, ct);
        _cache.Set(TokenCacheKey, token.AccessToken, TimeSpan.FromSeconds(Math.Max(60, token.ExpiresIn - 120)));
        return token.AccessToken;
    }

    /// <summary>Надсилає запит з токеном; якщо токен прострочився (401) — оновлює і повторює один раз.</summary>
    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> make, CancellationToken ct,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        for (var attempt = 0; ; attempt++)
        {
            var req = make();
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenAsync(ct));
            var resp = await _http.SendAsync(req, completion, ct);
            if (resp.StatusCode != HttpStatusCode.Unauthorized || attempt > 0) return resp;
            resp.Dispose();
            _cache.Remove(TokenCacheKey);
        }
    }

    // ---------------- Папки ----------------

    private void ClearFolderCache() => _cache.Remove("gd-folders-version-bump");

    private string FolderCacheKey(string parent, string name) =>
        $"gd-folder:{_cache.GetOrCreate("gd-folders-version-bump", _ => Guid.NewGuid().ToString("N"))}:{parent}:{name}";

    private async Task<string> FolderAsync(string name, string parent, CancellationToken ct)
    {
        var key = FolderCacheKey(parent, name);
        if (_cache.TryGetValue(key, out string? id) && id is not null) return id;

        var q = $"mimeType='{FolderMime}' and name='{Q(name)}' and '{Q(parent)}' in parents and trashed=false";
        using (var resp = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get,
                   $"{_o.ApiUrl}files?q={E(q)}&fields=files(id)&spaces=drive&pageSize=1"), ct))
        {
            await EnsureOk(resp, "пошуку папки", ct);
            var files = (await resp.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("files");
            if (files.GetArrayLength() > 0) id = files[0].GetProperty("id").GetString();
        }

        if (id is null)
        {
            using var resp = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, $"{_o.ApiUrl}files?fields=id")
            {
                Content = Json(new { name, mimeType = FolderMime, parents = new[] { parent } })
            }, ct);
            await EnsureOk(resp, "створення папки", ct);
            id = (await resp.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;
        }

        _cache.Set(key, id, TimeSpan.FromHours(6));
        return id;
    }

    // ---------------- IFileStorage ----------------

    public async Task EnsureReadyAsync(CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            _log.LogWarning("Google Drive: не задано Storage:GoogleDrive:ClientId / ClientSecret");
            return;
        }
        if (!await IsConnectedAsync())
        {
            _log.LogWarning("Google Drive ще не підключено — увійдіть як адміністратор: Адмін-панель → Сховище");
            return;
        }
        try { await AccessTokenAsync(ct); }
        catch (Exception ex) { _log.LogWarning("Google Drive: {Message}", ex.Message); }
    }

    public async Task<string> UploadAsync(StoredFileInfo file, Stream content, CancellationToken ct = default)
    {
        var root = await FolderAsync(_o.RootFolderName, "root", ct);
        var year = await FolderAsync(file.Year.ToString(), root, ct);
        var group = await FolderAsync(file.GroupName, year, ct);
        var folder = await FolderAsync(file.Category, group, ct);

        // 1) Починаємо resumable-завантаження (працює для великих файлів).
        Uri session;
        using (var init = await SendAsync(() =>
               {
                   var r = new HttpRequestMessage(HttpMethod.Post, $"{_o.UploadUrl}files?uploadType=resumable&fields=id")
                   {
                       Content = Json(new { name = file.FriendlyName, parents = new[] { folder }, mimeType = file.ContentType })
                   };
                   r.Headers.Add("X-Upload-Content-Type", file.ContentType);
                   r.Headers.Add("X-Upload-Content-Length", file.Size.ToString());
                   return r;
               }, ct))
        {
            await EnsureOk(init, "початку завантаження", ct);
            session = init.Headers.Location ?? throw new StorageException("Google Drive не повернув адресу для завантаження");
        }

        // 2) Передаємо сам файл.
        using var put = new HttpRequestMessage(HttpMethod.Put, session) { Content = new StreamContent(content) };
        put.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(file.ContentType);
        put.Content.Headers.ContentLength = file.Size;
        put.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenAsync(ct));
        using var resp = await _http.SendAsync(put, ct);
        await EnsureOk(resp, "завантаження файлу", ct);
        return (await resp.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
    {
        var resp = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{_o.ApiUrl}files/{E(key)}?alt=media"),
            ct, HttpCompletionOption.ResponseHeadersRead);
        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            resp.Dispose();
            throw new FileNotFoundException("Файл не знайдено на Google Drive", key);
        }
        if (!resp.IsSuccessStatusCode)
        {
            using (resp) await EnsureOk(resp, "читання файлу", ct);
        }
        return await resp.Content.ReadAsStreamAsync(ct);
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        try
        {
            using var resp = await SendAsync(() => new HttpRequestMessage(HttpMethod.Delete, $"{_o.ApiUrl}files/{E(key)}"), ct);
            if (!resp.IsSuccessStatusCode && resp.StatusCode != HttpStatusCode.NotFound)
                _log.LogWarning("Не вдалося видалити файл {Id} з Google Drive: {Status}", key, (int)resp.StatusCode);
        }
        catch (StorageException ex)
        {
            _log.LogWarning("Не вдалося видалити файл {Id} з Google Drive: {Message}", key, ex.Message);
        }
    }

    public async Task<StorageQuota?> GetQuotaAsync(CancellationToken ct = default)
    {
        if (!IsConfigured || !await IsConnectedAsync()) return null;
        try
        {
            using var resp = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, _o.ApiUrl + "about?fields=storageQuota"), ct);
            if (!resp.IsSuccessStatusCode) return null;
            var q = (await resp.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("storageQuota");
            long? Num(string name) => q.TryGetProperty(name, out var v) && long.TryParse(v.GetString(), out var n) ? n : null;
            return new StorageQuota(Num("usage") ?? 0, Num("limit"));
        }
        catch (Exception ex)
        {
            _log.LogWarning("Не вдалося отримати квоту Google Drive: {Message}", ex.Message);
            return null;
        }
    }

    // ---------------- Допоміжне ----------------

    private static string E(string s) => Uri.EscapeDataString(s);

    /// <summary>JSON-тіло з точним Content-Length (без chunked-передачі).</summary>
    private static StringContent Json(object value) =>
        new(JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json");
    private static string Q(string s) => s.Replace("\\", "\\\\").Replace("'", "\\'");

    private async Task EnsureOk(HttpResponseMessage resp, string action, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        var body = await resp.Content.ReadAsStringAsync(ct);
        _log.LogWarning("Google Drive помилка {Action}: {Status} {Body}", action, (int)resp.StatusCode, body);
        if (body.Contains("storageQuotaExceeded"))
            throw new StorageException("На Google Drive закінчилось місце.");
        throw new StorageException($"Помилка Google Drive під час {action}: {(int)resp.StatusCode}");
    }
}
