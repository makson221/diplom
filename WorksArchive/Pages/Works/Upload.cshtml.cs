using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorksArchive.Data;
using WorksArchive.Services;

namespace WorksArchive.Pages.Works;

/// <summary>Викладач сам завантажує роботу в архів.</summary>
public class UploadModel(AppDbContext db, WorkService works, ILogger<UploadModel> log) : PageModel
{
    [BindProperty] public WorkInput Input { get; set; } = new();
    [BindProperty] public IFormFile? WorkFile { get; set; }

    public List<StudyGroup> Groups { get; private set; } = new();
    public int MaxMb => works.MaxFileSizeMb;
    public string Accept => works.AcceptAttribute;

    public async Task OnGetAsync() =>
        Groups = await db.Groups.AsNoTracking().OrderBy(g => g.Name).ToListAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        await OnGetAsync();
        if (Input.GroupId > 0 && !Groups.Any(g => g.Id == Input.GroupId))
            ModelState.AddModelError("Input.GroupId", "Оберіть групу");
        if (works.ValidateFile(WorkFile) is { } fileError)
            ModelState.AddModelError(nameof(WorkFile), fileError);
        if (!ModelState.IsValid) return Page();

        try
        {
            await works.SaveAsync(Input, WorkFile!, User.Identity?.Name, HttpContext.RequestAborted);
        }
        catch (Exception ex) when (ex is StorageException or HttpRequestException or DbUpdateException)
        {
            log.LogError(ex, "Помилка збереження роботи");
            ModelState.AddModelError("", "Не вдалося зберегти файл: " + ex.Message);
            return Page();
        }

        TempData["Success"] = $"Роботу «{Input.StudentName}» додано";
        return RedirectToPage("/Works/Index", new { Input.GroupId, Input.Year });
    }
}
