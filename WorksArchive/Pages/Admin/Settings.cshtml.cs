using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WorksArchive.Services;

namespace WorksArchive.Pages.Admin;

public class SettingsModel(SettingsService settings, WorkService works, IConfiguration config) : PageModel
{
    [BindProperty] public bool AllowStudentUpload { get; set; }
    [BindProperty] public string? StudentUploadCode { get; set; }

    public string DbProvider => config["Database:Provider"] == "Sqlite" ? "SQLite (локально)" : "PostgreSQL (Supabase)";
    public string StorageProvider => config["Storage:Provider"] == "Local" ? "Локальна папка" : "Supabase Storage (хмара)";
    public int MaxMb => works.MaxFileSizeMb;

    public async Task OnGetAsync()
    {
        AllowStudentUpload = await settings.AllowStudentUploadAsync();
        StudentUploadCode = await settings.StudentUploadCodeAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await settings.SetAsync(SettingsService.AllowStudentUploadKey, AllowStudentUpload ? "true" : "false");
        var code = (StudentUploadCode ?? "").Trim();
        await settings.SetAsync(SettingsService.StudentUploadCodeKey, code.Length > 64 ? code[..64] : code);
        TempData["Success"] = "Налаштування збережено";
        return RedirectToPage();
    }
}
