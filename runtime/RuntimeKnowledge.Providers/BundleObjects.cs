using System.Net;
using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;
using RuntimeKnowledge.Contracts;

namespace RuntimeKnowledge.Providers;

public sealed class BundleObjects(IAmazonS3 client, string bucket) : IBundleObjects
{
    private const long MaxBytes = 64L * 1024 * 1024;

    public async Task<Stream> OpenAsync(string objectKey, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetObjectAsync(bucket, objectKey, cancellationToken);
            if (response.ContentLength is < 0 or > MaxBytes)
                throw new WorkspaceException("invalidBundle", "Bundle object exceeds the supported size.");
            return await ReadBoundedAsync(response.ResponseStream, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException("The referenced bundle object does not exist.", objectKey, ex);
        }
    }

    public async Task PutImmutableAsync(string objectKey, Stream content, CancellationToken cancellationToken)
    {
        using var bytes = await ReadBoundedAsync(content, cancellationToken);
        var expected = Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray()));
        if (objectKey != $"blobs/{expected}" && objectKey != $"manifests/{expected}.json")
            throw new WorkspaceException("invalidBundle", "Object key must match its content digest.");

        try
        {
            await client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = bucket,
                Key = objectKey,
                InputStream = bytes,
                AutoCloseStream = false,
                IfNoneMatch = "*"
            }, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            await using var current = await OpenAsync(objectKey, cancellationToken);
            var actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(current, cancellationToken));
            if (actual != expected)
                throw new WorkspaceException("invalidBundle", "Immutable object already contains different bytes.");
        }
    }

    private static async Task<MemoryStream> ReadBoundedAsync(Stream stream, CancellationToken cancellationToken)
    {
        var result = new MemoryStream();
        try
        {
            var buffer = new byte[65536];
            while (true)
            {
                var count = await stream.ReadAsync(buffer, cancellationToken);
                if (count == 0) break;
                if (result.Length + count > MaxBytes)
                    throw new WorkspaceException("invalidBundle", "Bundle object exceeds the supported size.");
                result.Write(buffer, 0, count);
            }
            result.Position = 0;
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }
}
