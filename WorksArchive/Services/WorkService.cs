using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WorksArchive.Data;

namespace WorksArchive.Services;

public class UploadOptions
{
    public int MaxFileSizeMb { get; set; } = 50;
}

/// <summary>Поля форми завантаження роботи (спільні для студента і викладача).</summary>
public class WorkInput
{
    [Display(Name = "Група")]
    [Range(1, int.MaxValue, ErrorMessage = "Оберіть групу")]
    public int GroupId { get; set; }

    [Display(Name = "Рік здачі")]
    [Range(2000, 2100, ErrorMessage = "Некоректний рік")]
    public int Year { get; set; } = DateTime.Now.Year;

    [Display(Name = "Тип роботи")]
    public WorkType Type { get; set; } = WorkType.Coursework;

    [Display(Name = "ПІБ студента")]
    [Required(ErrorMessage = "Вкажіть ПІБ студента")]
    [StringLength(150)]
    public string StudentName { get; set; } = "";

    [Display(Name = "Тема роботи")]
    [Required(ErrorMessage = "Вкажіть тему роботи")]
    [StringLength(300)]
    public string Title { get; set; } = "";
}

/// <summary>Фільтр списку робіт: за групою, роком, типом і пошуком.</summary>
public class WorkFilter
{
    public int? GroupId { get; set; }
    public int? Year { get; set; }
    public WorkType? Type { get; set; }
    public string? Q { get; set; }
}

public class WorkService(AppDbContext db, IFileStorage storage, IOptions<UploadOptions> options)
{
    public static readonly string[] AllowedExtensions =
        [".pdf", ".doc", ".docx", ".odt", ".rtf", ".txt", ".ppt", ".pptx", ".xls", ".xlsx", ".zip", ".rar", ".7z"];

    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    public int MaxFileSizeMb => options.Value.MaxFileSizeMb;
    public string AcceptAttribute => string.Join(',', AllowedExtensions);

    /// <summary>Повертає текст помилки або null, якщо файл підходить.</summary>
    public string? ValidateFile(IFormFile? file)
    {
        if (file is null || file.Length == 0) return "Оберіть файл";
        if (file.Length > MaxFileSizeMb * 1024L * 1024L) return $"Файл завеликий (максимум {MaxFileSizeMb} МБ)";
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            return "Недопустимий тип файлу. Дозволено: " + string.Join(", ", AllowedExtensions);
        return null;
    }

    public async Task<Work> SaveAsync(WorkInput input, IFormFile file, string? uploadedBy, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        // Шлях у сховищі — лише латиниця/цифри, оригінальна назва зберігається в БД.
        var path = $"{input.Year}/{input.GroupId}/{Guid.NewGuid():N}{ext}";
        if (!ContentTypes.TryGetContentType(file.FileName, out var contentType))
            contentType = "application/octet-stream";

        await using (var stream = file.OpenReadStream())
            await storage.UploadAsync(path, stream, contentType, ct);

        var work = new Work
        {
            GroupId = input.GroupId,
            Year = input.Year,
            Type = input.Type,
            StudentName = input.StudentName.Trim(),
            Title = input.Title.Trim(),
            OriginalFileName = Path.GetFileName(file.FileName),
            StoragePath = path,
            ContentType = contentType,
            SizeBytes = file.Length,
            UploadedBy = uploadedBy
        };
        db.Works.Add(work);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            await storage.DeleteAsync(path, CancellationToken.None);
            throw;
        }
        return work;
    }

    public async Task DeleteAsync(Work work, CancellationToken ct = default)
    {
        db.Works.Remove(work);
        await db.SaveChangesAsync(ct);
        await storage.DeleteAsync(work.StoragePath, ct);
    }

    public IQueryable<Work> Query(WorkFilter f)
    {
        var q = db.Works.Include(w => w.Group).AsNoTracking().AsQueryable();
        if (f.GroupId is > 0) q = q.Where(w => w.GroupId == f.GroupId);
        if (f.Year is > 0) q = q.Where(w => w.Year == f.Year);
        if (f.Type.HasValue) q = q.Where(w => w.Type == f.Type);
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var s = f.Q.Trim().ToLower();
            q = q.Where(w => w.StudentName.ToLower().Contains(s) || w.Title.ToLower().Contains(s));
        }
        return q.OrderByDescending(w => w.Year).ThenBy(w => w.Group.Name).ThenBy(w => w.StudentName);
    }

    /// <summary>
    /// Пакує обрані роботи у ZIP у тимчасовий файл (видаляється автоматично після віддачі).
    /// Структура архіву: Група/Рік/Тип/ПІБ - файл.
    /// </summary>
    public async Task<FileStream> BuildZipAsync(IEnumerable<Work> works, CancellationToken ct = default)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"works_{Guid.NewGuid():N}.zip");
        var fs = new FileStream(tmp, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var w in works)
                {
                    var typeDir = w.Type == WorkType.Diploma ? "Дипломні" : "Курсові";
                    var baseName = Safe($"{w.StudentName} - {w.OriginalFileName}");
                    var name = $"{Safe(w.Group.Name)}/{w.Year}/{typeDir}/{baseName}";
                    for (var i = 2; !used.Add(name); i++)
                        name = $"{Safe(w.Group.Name)}/{w.Year}/{typeDir}/({i}) {baseName}";

                    var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
                    await using var target = await entry.OpenAsync(ct);
                    try
                    {
                        await using var source = await storage.OpenReadAsync(w.StoragePath, ct);
                        await source.CopyToAsync(target, ct);
                    }
                    catch (FileNotFoundException)
                    {
                        // Файл зник зі сховища — пишемо замітку замість падіння всього архіву.
                        await using var sw = new StreamWriter(target);
                        await sw.WriteAsync("Файл не знайдено у сховищі.");
                    }
                }
            }
            fs.Position = 0;
            return fs;
        }
        catch
        {
            await fs.DisposeAsync();
            throw;
        }
    }

    private static string Safe(string s)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var clean = new string(s.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return clean.Length == 0 ? "_" : clean;
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} Б",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} КБ",
        _ => $"{bytes / 1024.0 / 1024.0:0.#} МБ"
    };
}
