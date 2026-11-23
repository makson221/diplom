using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorksArchive.Data;

namespace WorksArchive.Pages.Admin;

public class IndexModel(AppDbContext db) : PageModel
{
    public record GroupYearRow(int GroupId, string Group, int Year, int Count);

    public int TotalWorks { get; private set; }
    public long TotalSize { get; private set; }
    public int Courseworks { get; private set; }
    public int Diplomas { get; private set; }
    public int GroupCount { get; private set; }
    public int UserCount { get; private set; }
    public List<Work> Recent { get; private set; } = new();
    public List<GroupYearRow> ByGroupYear { get; private set; } = new();

    public async Task OnGetAsync()
    {
        TotalWorks = await db.Works.CountAsync();
        TotalSize = TotalWorks > 0 ? await db.Works.SumAsync(w => w.SizeBytes) : 0;
        Diplomas = await db.Works.CountAsync(w => w.Type == WorkType.Diploma);
        Courseworks = TotalWorks - Diplomas;
        GroupCount = await db.Groups.CountAsync();
        UserCount = await db.Users.CountAsync();
        Recent = await db.Works.Include(w => w.Group).AsNoTracking()
            .OrderByDescending(w => w.UploadedAt).Take(10).ToListAsync();

        var rows = await db.Works
            .GroupBy(w => new { w.GroupId, w.Group.Name, w.Year })
            .Select(g => new { g.Key.GroupId, g.Key.Name, g.Key.Year, Count = g.Count() })
            .ToListAsync();
        ByGroupYear = rows.OrderByDescending(r => r.Year).ThenBy(r => r.Name)
            .Select(r => new GroupYearRow(r.GroupId, r.Name, r.Year, r.Count)).ToList();
    }
}
