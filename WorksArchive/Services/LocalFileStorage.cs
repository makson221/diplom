using Microsoft.Extensions.Options;

namespace WorksArchive.Services;

/// <summary>Локальне сховище у папці — для розробки та тестування без інтернету.</summary>
public class LocalFileStorage(IOptions<StorageOptions> options, IWebHostEnvironment env) : IFileStorage
{
    public string DisplayName => "Диск сервера";

    private readonly string _root = Path.GetFullPath(Path.Combine(env.ContentRootPath, options.Value.LocalPath));

    private string Resolve(string path)
    {
        var full = Path.GetFullPath(Path.Combine(_root, path));
        if (!full.StartsWith(_root, StringComparison.Ordinal))
            throw new InvalidOperationException("Некоректний шлях");
        return full;
    }

    public Task EnsureReadyAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_root);
        return Task.CompletedTask;
    }

    public async Task<string> UploadAsync(StoredFileInfo file, Stream content, CancellationToken ct = default)
    {
        var full = Resolve(file.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var fs = File.Create(full);
        await content.CopyToAsync(fs, ct);
        return file.Path;
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken ct = default)
    {
        var full = Resolve(path);
        if (!File.Exists(full)) throw new FileNotFoundException("Файл не знайдено", path);
        return Task.FromResult<Stream>(File.OpenRead(full));
    }

    public Task DeleteAsync(string path, CancellationToken ct = default)
    {
        var full = Resolve(path);
        if (File.Exists(full)) File.Delete(full);
        return Task.CompletedTask;
    }
}
