using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorksArchive.Data;
using WorksArchive.Services;

namespace WorksArchive.Pages.Admin;

public class StorageModel(StorageResolver storages, GoogleDriveStorage drive, SettingsService settings,
    WorkService works, AppDbContext db, IConfiguration config) : PageModel
{
    public const string StateKey = "gd-oauth-state";

    public record ProviderRow(string Name, string Title, bool Available, int Count, long Bytes);

    public StorageUsage Usage { get; private set; } = null!;
    public string Active { get; private set; } = StorageResolver.Local;
    public List<ProviderRow> Providers { get; private set; } = new();
    public double QuotaGb { get; private set; }

    public bool DriveConfigured { get; private set; }
    public bool DriveConnected { get; private set; }
    public string? DriveAccount { get; private set; }
    public string? DriveClientId { get; private set; }

    public string DbProvider => config["Database:Provider"] == "Sqlite" ? "файл на сервері (SQLite)" : "PostgreSQL (хмара)";
    public int MaxMb => works.MaxFileSizeMb;
    public string RedirectUri => RedirectUriFor(Request);

    public static string RedirectUriFor(HttpRequest r) => $"{r.Scheme}://{r.Host}/Admin/GoogleDriveCallback";

    public async Task OnGetAsync()
    {
        Usage = await works.GetUsageAsync(HttpContext.RequestAborted);
        Active = await storages.ActiveNameAsync();
        QuotaGb = await storages.QuotaGbAsync();

        var stats = await db.Works.GroupBy(w => w.StorageProvider)
            .Select(g => new { g.Key, Count = g.Count(), Bytes = g.Sum(w => w.SizeBytes) }).ToListAsync();
        Providers = StorageResolver.All.Select(n =>
        {
            var st = stats.FirstOrDefault(x => x.Key == n);
            return new ProviderRow(n, StorageResolver.Title(n), storages.IsAvailable(n), st?.Count ?? 0, st?.Bytes ?? 0);
        }).Where(p => p.Available || p.Count > 0).ToList();

        DriveConfigured = await drive.IsConfiguredAsync();
        DriveConnected = await drive.IsConnectedAsync();
        DriveAccount = await drive.ConnectedAccountAsync();
        DriveClientId = await drive.SavedClientIdAsync();
    }

    public async Task<IActionResult> OnPostActiveAsync(string provider)
    {
        var name = StorageResolver.Normalize(provider);
        if (name is null || !storages.IsAvailable(name))
        {
            TempData["Error"] = "Це сховище недоступне";
            return RedirectToPage();
        }
        if (name == StorageResolver.GoogleDrive && !await drive.IsConnectedAsync())
        {
            TempData["Error"] = "Спочатку підключіть Google Drive (нижче на цій сторінці)";
            return RedirectToPage();
        }
        await settings.SetAsync(StorageResolver.ActiveKey, name);
        try { await storages.Get(name).EnsureReadyAsync(); } catch { /* повідомлення буде в журналі */ }
        TempData["Success"] = $"Нові роботи тепер зберігаються: {StorageResolver.Title(name)}. Уже завантажені роботи залишились там, де були, і доступні як раніше.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostQuotaAsync(string quotaGb)
    {
        if (double.TryParse(quotaGb?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var gb) && gb is > 0 and < 100000)
        {
            await settings.SetAsync(StorageResolver.QuotaKey, gb.ToString(CultureInfo.InvariantCulture));
            TempData["Success"] = "Обсяг збережено";
        }
        else TempData["Error"] = "Вкажіть обсяг у ГБ, наприклад 5";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDriveClientAsync(string clientId, string? clientSecret)
    {
        if (string.IsNullOrWhiteSpace(clientId) || (!await drive.IsConfiguredAsync() && string.IsNullOrWhiteSpace(clientSecret)))
        {
            TempData["Error"] = "Вкажіть Client ID і Client Secret";
            return RedirectToPage();
        }
        await drive.SaveClientAsync(clientId, clientSecret);
        TempData["Success"] = "Ключі Google збережено. Тепер натисніть «Підключити Google Drive».";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnGetConnectAsync()
    {
        if (!await drive.IsConfiguredAsync()) return RedirectToPage();
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        TempData[StateKey] = state;
        return Redirect(await drive.BuildAuthorizeUrlAsync(RedirectUri, state));
    }

    public async Task<IActionResult> OnPostDisconnectAsync()
    {
        if (await storages.ActiveNameAsync() == StorageResolver.GoogleDrive)
        {
            TempData["Error"] = "Google Drive зараз активне сховище. Спершу перемкніть нові роботи на інше сховище.";
            return RedirectToPage();
        }
        await drive.DisconnectAsync();
        TempData["Success"] = "Google Drive відключено";
        return RedirectToPage();
    }
}
