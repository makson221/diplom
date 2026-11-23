using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorksArchive.Data;
using WorksArchive.Services;

namespace WorksArchive.Pages;

/// <summary>Сторінка для студентів: здати роботу без реєстрації (список робіт їм недоступний).</summary>
public class UploadModel(AppDbContext db, WorkService works, SettingsService settings, ILogger<UploadModel> log) : PageModel
{
    [BindProperty] public WorkInput Input { get; set; } = new();
    [BindProperty] public IFormFile? WorkFile { get; set; }
    [BindProperty, Display(Name = "Код доступу")] public string? Code { get; set; }

    public List<StudyGroup> Groups { get; private set; } = new();
    public bool Allowed { get; private set; }
    public bool CodeRequired { get; private set; }
    public bool Done { get; private set; }
    public int MaxMb => works.MaxFileSizeMb;
    public string Accept => works.AcceptAttribute;

    private async Task LoadAsync()
    {
        Allowed = await settings.AllowStudentUploadAsync();
        CodeRequired = (await settings.StudentUploadCodeAsync()).Length > 0;
        Groups = await db.Groups.AsNoTracking().OrderBy(g => g.Name).ToListAsync();
    }

    public async Task OnGetAsync(bool done = false)
    {
        await LoadAsync();
        Done = done;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        if (!Allowed) return Page();

        var code = await settings.StudentUploadCodeAsync();
        if (code.Length > 0 && !string.Equals(Code?.Trim(), code, StringComparison.Ordinal))
            ModelState.AddModelError(nameof(Code), "Невірний код доступу");

        if (Input.GroupId > 0 && !Groups.Any(g => g.Id == Input.GroupId))
            ModelState.AddModelError("Input.GroupId", "Оберіть групу");

        if (works.ValidateFile(WorkFile) is { } fileError)
            ModelState.AddModelError(nameof(WorkFile), fileError);

        if (!ModelState.IsValid) return Page();

        try
        {
            await works.SaveAsync(Input, WorkFile!, uploadedBy: null, HttpContext.RequestAborted);
        }
        catch (Exception ex) when (ex is StorageException or HttpRequestException or DbUpdateException)
        {
            log.LogError(ex, "Помилка збереження роботи студента");
            ModelState.AddModelError("", "Не вдалося зберегти файл. Спробуйте пізніше.");
            return Page();
        }
        return RedirectToPage(new { done = true });
    }
}
