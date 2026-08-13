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
            AuthenticationRegion = "auto",
            DisablePayloadSigning = true,
        };

        _s3 = new AmazonS3Client(
            new Amazon.Runtime.BasicAWSCredentials(r2["AccessKeyId"], r2["SecretAccessKey"]),
            s3Config);
    }

    private static readonly HttpClient _httpClient = new();

    public async Task<string> UploadAsync(Stream stream, string key, string contentType)
    {
        // Use pre-signed URL to avoid STREAMING-AWS4-HMAC-SHA256-PAYLOAD
        // which Cloudflare R2 does not support
        var preSignRequest = new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key        = key,
            Verb       = HttpVerb.PUT,
            Expires    = DateTime.UtcNow.AddMinutes(10),
        };

        var presignedUrl = _s3.GetPreSignedURL(preSignRequest);

        using var content = new StreamContent(stream);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        var response = await _httpClient.PutAsync(presignedUrl, content);
        response.EnsureSuccessStatusCode();

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
