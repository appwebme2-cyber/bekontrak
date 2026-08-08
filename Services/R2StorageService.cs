using Amazon.S3;
using Amazon.S3.Model;

namespace RefineryContractAPI.Services;

public class R2StorageService
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;

    public R2StorageService(IConfiguration config)
    {
        var r2 = config.GetSection("R2");
        _bucket = r2["BucketName"] ?? "";

        var s3Config = new AmazonS3Config
        {
            ServiceURL = r2["EndpointUrl"],
            ForcePathStyle = true,
        };

        _s3 = new AmazonS3Client(r2["AccessKeyId"], r2["SecretAccessKey"], s3Config);
    }

    public async Task<string> UploadAsync(Stream stream, string key, string contentType)
    {
        var req = new PutObjectRequest
        {
            BucketName = _bucket,
            Key        = key,
            InputStream = stream,
            ContentType = contentType,
        };
        await _s3.PutObjectAsync(req);
        return key;
    }

    public async Task DeleteAsync(string key)
    {
        try { await _s3.DeleteObjectAsync(_bucket, key); }
        catch { /* ignore if key doesn't exist */ }
    }

    public async Task<(Stream stream, string contentType, long contentLength)> GetAsync(string key)
    {
        var resp = await _s3.GetObjectAsync(_bucket, key);
        return (resp.ResponseStream, resp.Headers.ContentType, resp.ContentLength);
    }
}
