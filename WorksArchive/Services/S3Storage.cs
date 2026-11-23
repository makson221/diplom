using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace WorksArchive.Services;

/// <summary>
/// S3-сумісне сховище: Backblaze B2 (10 ГБ безкоштовно), Oracle Cloud Object Storage (20 ГБ),
/// Cloudflare R2 (10 ГБ), IDrive e2, MinIO тощо. Бакет має бути приватним.
/// </summary>
public sealed class S3Storage : IFileStorage, IDisposable
{
    private readonly AmazonS3Client _s3;
    private readonly string _bucket;
    private readonly ILogger<S3Storage> _log;

    public string DisplayName { get; }

    public S3Storage(IOptions<StorageOptions> options, ILogger<S3Storage> log)
    {
        var o = options.Value.S3;
        if (string.IsNullOrWhiteSpace(o.ServiceUrl) || string.IsNullOrWhiteSpace(o.AccessKey)
            || string.IsNullOrWhiteSpace(o.SecretKey) || string.IsNullOrWhiteSpace(o.Bucket))
            throw new InvalidOperationException("Не задано Storage:S3:ServiceUrl / AccessKey / SecretKey / Bucket.");

        var config = new AmazonS3Config
        {
            ServiceURL = o.ServiceUrl,
            AuthenticationRegion = o.Region,
            ForcePathStyle = o.ForcePathStyle,
            // Нові контрольні суми AWS підтримуються не всіма S3-сумісними сервісами (B2, Oracle) — вмикаємо лише коли обов'язково.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            Timeout = TimeSpan.FromMinutes(10)
        };
        _s3 = new AmazonS3Client(new BasicAWSCredentials(o.AccessKey, o.SecretKey), config);
        _bucket = o.Bucket;
        _log = log;
        DisplayName = $"S3-сумісне сховище ({new Uri(o.ServiceUrl).Host})";
    }

    public async Task EnsureReadyAsync(CancellationToken ct = default)
    {
        try
        {
            await _s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = _bucket, MaxKeys = 1 }, ct);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            await _s3.PutBucketAsync(new PutBucketRequest { BucketName = _bucket }, ct);
            _log.LogInformation("Створено бакет '{Bucket}'", _bucket);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Не вдалося перевірити бакет '{Bucket}'", _bucket);
        }
    }

    public async Task<string> UploadAsync(StoredFileInfo file, Stream content, CancellationToken ct = default)
    {
        try
        {
            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _bucket,
                Key = file.Path,
                InputStream = content,
                ContentType = file.ContentType,
                AutoCloseStream = false,
                UseChunkEncoding = false // B2/Oracle не підтримують aws-chunked
            }, ct);
            return file.Path;
        }
        catch (AmazonServiceException ex)
        {
            throw new StorageException($"Помилка завантаження у сховище: {ex.StatusCode} {ex.Message}");
        }
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var resp = await _s3.GetObjectAsync(_bucket, key, ct);
            return resp.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException("Файл не знайдено у сховищі", key);
        }
        catch (AmazonServiceException ex)
        {
            throw new StorageException($"Помилка читання зі сховища: {ex.StatusCode} {ex.Message}");
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _s3.DeleteObjectAsync(_bucket, key, ct);
        }
        catch (AmazonServiceException ex)
        {
            _log.LogWarning("Не вдалося видалити '{Key}': {Status}", key, ex.StatusCode);
        }
    }

    public void Dispose() => _s3.Dispose();
}
