using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WorksArchive.Services;

namespace WorksArchive.Pages.Admin;

/// <summary>Сюди Google повертає адміністратора після дозволу доступу до Drive.</summary>
public class GoogleDriveCallbackModel(GoogleDriveStorage drive, ILogger<GoogleDriveCallbackModel> log) : PageModel
{
    public async Task<IActionResult> OnGetAsync(string? code, string? state, string? error)
    {
        var expected = TempData[StorageModel.StateKey] as string;

        if (!string.IsNullOrEmpty(error))
            TempData["Error"] = error == "access_denied" ? "Доступ до Google Drive не надано" : $"Google повернув помилку: {error}";
        else if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state) || state != expected)
            TempData["Error"] = "Некоректна відповідь від Google. Спробуйте ще раз.";
        else
        {
            try
            {
                await drive.ConnectAsync(code, StorageModel.RedirectUriFor(Request), HttpContext.RequestAborted);
                TempData["Success"] = $"Google Drive підключено: {await drive.ConnectedAccountAsync()}";
            }
            catch (Exception ex) when (ex is StorageException or HttpRequestException)
            {
                log.LogError(ex, "Помилка підключення Google Drive");
                TempData["Error"] = ex.Message;
            }
        }
        return RedirectToPage("/Admin/Storage");
    }
}
