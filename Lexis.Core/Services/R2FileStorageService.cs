using Amazon.S3;
using Amazon.S3.Model; 
using Lexis.Core.Helpers;
using Lexis.Core.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Lexis.Core.Services;

public sealed class R2FileStorageService : IFileStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly CloudflareR2Options _options;

    public R2FileStorageService(
        IAmazonS3 s3Client,
        IOptions<CloudflareR2Options> options)
    {
        _s3Client = s3Client;
        _options = options.Value;
    }

    public async Task<string> SaveFileAsync(IFormFile file, int userId)
    {
        var extension = Path.GetExtension(file.FileName);
        var objectKey =
            $"users/{userId}/documents/{Guid.NewGuid():N}{extension}";

        await using var stream = file.OpenReadStream();

        var request = new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
            InputStream = stream,
            ContentType = file.ContentType,
            DisablePayloadSigning = true,
            DisableDefaultChecksumValidation = true
        };

        await _s3Client.PutObjectAsync(request);

        return objectKey;
    }

    public async Task<Stream> GetFileAsync(string filePath)
    {
        using var response = await _s3Client.GetObjectAsync(
            _options.BucketName,
            filePath);

        var memoryStream = new MemoryStream();

        await response.ResponseStream.CopyToAsync(memoryStream);

        memoryStream.Position = 0;

        return memoryStream;
    }

    public async Task DeleteFileAsync(string filePath)
    {
        await _s3Client.DeleteObjectAsync(
            _options.BucketName,
            filePath);
    }
}
