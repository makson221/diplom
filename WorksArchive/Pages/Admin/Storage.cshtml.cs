using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using WorksArchive.Services;

namespace WorksArchive.Pages.Admin;

public class StorageModel(IFileStorage storage, WorkService works, IConfiguration config, IOptions<StorageOptions> options) : PageModel
{
    public const string StateKey = "gd-oauth-state";

    public StorageUsage Usage { get; private set; } = null!;
    public GoogleDriveStorage? Drive => storage as GoogleDriveStorage;
    public bool DriveConnected { get; private set; }
    public string? DriveAccount { get; private set; }
    public string DriveRoot => options.Value.GoogleDrive.RootFolderName;
    public string DbProvider => config["Database:Provider"] == "Sqlite" ? "SQLite (локально)" : "PostgreSQL (хмара)";
    public int MaxMb => works.MaxFileSizeMb;
    public string RedirectUri => RedirectUriFor(Request);

    public static string RedirectUriFor(HttpRequest r) => $"{r.Scheme}://{r.Host}/Admin/GoogleDriveCallback";

    public async Task OnGetAsync()
    {
        Usage = await works.GetUsageAsync(HttpContext.RequestAborted);
        if (Drive is not null)
        {
            DriveConnected = await Drive.IsConnectedAsync();
            DriveAccount = await Drive.ConnectedAccountAsync();
        }
    }

    public IActionResult OnGetConnect()
    {
        if (Drive is null || !Drive.IsConfigured) return RedirectToPage();
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        TempData[StateKey] = state;
        return Redirect(Drive.BuildAuthorizeUrl(RedirectUri, state));
    }

    public async Task<IActionResult> OnPostDisconnectAsync()
    {
        if (Drive is not null) await Drive.DisconnectAsync();
        TempData["Success"] = "Google Drive відключено";
        return RedirectToPage();
    }
}
