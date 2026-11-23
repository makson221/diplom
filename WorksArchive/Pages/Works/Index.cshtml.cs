using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorksArchive.Data;
using WorksArchive.Services;

namespace WorksArchive.Pages.Works;

/// <summary>Панель викладача: фільтр за групою/роком, завантаження окремих файлів або ZIP-архіву.</summary>
public class IndexModel(AppDbContext db, WorkService works, ILogger<IndexModel> log) : PageModel
{
    private const int PageSize = 50;

    [BindProperty(SupportsGet = true)] public WorkFilter Filter { get; set; } = new();
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNo { get; set; } = 1;

    public List<StudyGroup> Groups { get; private set; } = new();
    public List<int> Years { get; private set; } = new();
    public List<Work> Items { get; private set; } = new();
    public int Total { get; private set; }
    public long TotalSize { get; private set; }
    public int Pages { get; private set; }

    public async Task OnGetAsync()
    {
        Groups = await db.Groups.AsNoTracking().OrderBy(g => g.Name).ToListAsync();
        Years = await db.Works.Select(w => w.Year).Distinct().OrderByDescending(y => y).ToListAsync();

        var q = works.Query(Filter);
        Total = await q.CountAsync();
        TotalSize = Total > 0 ? await q.SumAsync(w => w.SizeBytes) : 0;
        Pages = (int)Math.Ceiling(Total / (double)PageSize);
        PageNo = Math.Clamp(PageNo, 1, Math.Max(Pages, 1));
        Items = await q.Skip((PageNo - 1) * PageSize).Take(PageSize).ToListAsync();
    }

    public async Task<IActionResult> OnGetDownloadAsync(int id)
    {
        var w = await db.Works.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (w is null) return NotFound();
        try
        {
            var stream = await works.OpenReadAsync(w, HttpContext.RequestAborted);
            return File(stream, w.ContentType, w.OriginalFileName);
        }
        catch (FileNotFoundException)
        {
            TempData["Error"] = "Файл не знайдено у сховищі";
            return RedirectToPage();
        }
        catch (Exception ex) when (ex is StorageException or HttpRequestException)
        {
            log.LogError(ex, "Помилка читання файлу роботи #{Id}", id);
            TempData["Error"] = ex.Message;
            return RedirectToPage();
        }
    }

    public async Task<IActionResult> OnGetZipAsync()
    {
        var list = await works.Query(Filter).ToListAsync();
        if (list.Count == 0)
        {
            TempData["Error"] = "Немає робіт для завантаження";
            return RedirectToPage(new { Filter.GroupId, Filter.Year, Filter.Type, Filter.Q });
        }

        var parts = new List<string> { "Роботи" };
        if (Filter.GroupId is > 0) parts.Add(list[0].Group.Name);
        if (Filter.Year is > 0) parts.Add(Filter.Year.Value.ToString());
        if (Filter.Type == WorkType.Coursework) parts.Add("курсові");
        if (Filter.Type == WorkType.Diploma) parts.Add("дипломні");

        try
        {
            var zip = await works.BuildZipAsync(list, HttpContext.RequestAborted);
            return File(zip, "application/zip", string.Join('_', parts) + ".zip");
        }
        catch (Exception ex) when (ex is StorageException or HttpRequestException)
        {
            log.LogError(ex, "Помилка формування ZIP");
            TempData["Error"] = "Не вдалося сформувати архів. Спробуйте пізніше.";
            return RedirectToPage(new { Filter.GroupId, Filter.Year, Filter.Type, Filter.Q });
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var w = await db.Works.FindAsync(id);
        if (w is not null)
        {
            await works.DeleteAsync(w);
            log.LogInformation("{User} видалив роботу #{Id} ({Student})", User.Identity?.Name, w.Id, w.StudentName);
            TempData["Success"] = $"Роботу «{w.StudentName}» видалено";
        }
        return RedirectToPage(new { Filter.GroupId, Filter.Year, Filter.Type, Filter.Q });
    }
}
