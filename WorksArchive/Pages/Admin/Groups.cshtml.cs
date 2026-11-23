using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorksArchive.Data;

namespace WorksArchive.Pages.Admin;

public class GroupsModel(AppDbContext db) : PageModel
{
    public record Row(int Id, string Name, int Count);
    public List<Row> Rows { get; private set; } = new();

    public async Task OnGetAsync() =>
        Rows = await db.Groups.OrderBy(g => g.Name)
            .Select(g => new Row(g.Id, g.Name, g.Works.Count)).ToListAsync();

    private static string? Normalize(string? name)
    {
        var n = name?.Trim();
        return string.IsNullOrEmpty(n) ? null : n.Length > 50 ? n[..50] : n;
    }

    public async Task<IActionResult> OnPostCreateAsync(string? name)
    {
        var n = Normalize(name);
        if (n is null) return RedirectToPage();
        if (await db.Groups.AnyAsync(g => g.Name == n))
            TempData["Error"] = $"Група «{n}» уже існує";
        else
        {
            db.Groups.Add(new StudyGroup { Name = n });
            await db.SaveChangesAsync();
            TempData["Success"] = $"Групу «{n}» додано";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostBulkAsync(string? names)
    {
        var list = (names ?? "").Split('\n').Select(Normalize).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var existing = await db.Groups.Select(g => g.Name).ToListAsync();
        var added = list.Where(n => !existing.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
        db.Groups.AddRange(added.Select(n => new StudyGroup { Name = n }));
        await db.SaveChangesAsync();
        TempData["Success"] = $"Додано груп: {added.Count}";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRenameAsync(int id, string? name)
    {
        var n = Normalize(name);
        var g = await db.Groups.FindAsync(id);
        if (g is null || n is null) return RedirectToPage();
        if (await db.Groups.AnyAsync(x => x.Name == n && x.Id != id))
        {
            TempData["Error"] = $"Група «{n}» уже існує";
            return RedirectToPage();
        }
        g.Name = n;
        await db.SaveChangesAsync();
        TempData["Success"] = "Назву змінено";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var g = await db.Groups.FindAsync(id);
        if (g is null) return RedirectToPage();
        if (await db.Works.AnyAsync(w => w.GroupId == id))
        {
            TempData["Error"] = "У групі є роботи — спочатку видаліть їх";
            return RedirectToPage();
        }
        db.Groups.Remove(g);
        await db.SaveChangesAsync();
        TempData["Success"] = $"Групу «{g.Name}» видалено";
        return RedirectToPage();
    }
}
