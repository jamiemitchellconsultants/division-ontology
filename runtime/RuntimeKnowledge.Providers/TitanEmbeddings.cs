using System.Text.Json;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using RuntimeKnowledge.Contracts;

namespace RuntimeKnowledge.Providers;

public sealed class TitanEmbeddings(IAmazonBedrockRuntime client) : IEmbeddingProvider, IDisposable
{
    public const string ModelId = "amazon.titan-embed-text-v2:0";

    public async Task<float[]> EmbedAsync(EmbeddingProfile profile, string input, EmbeddingPurpose purpose,
        CancellationToken token)
    {
        if (profile.Provider != "Bedrock" || profile.Model != ModelId || profile.Dimensions != 1024 ||
            profile.TextProfile != "knowledge-v1")
            throw new WorkspaceException("invalidEmbedding", "Titan embedding profile is incompatible with this provider.");
        using var body = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new
        {
            inputText = input, dimensions = profile.Dimensions, normalize = true
        }));
        var response = await client.InvokeModelAsync(new InvokeModelRequest
        {
            ModelId = ModelId, ContentType = "application/json", Accept = "application/json", Body = body
        }, token);
        using var responseBody = response.Body;
        try
        {
            using var document = await JsonDocument.ParseAsync(responseBody, cancellationToken: token);
            var vector = document.RootElement.GetProperty("embedding").Deserialize<float[]>();
            profile.ValidateVector(vector);
            return vector!;
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new WorkspaceException("invalidEmbedding", "Bedrock returned a malformed embedding.", error);
        }
    }

    public void Dispose() => client.Dispose();
}

public static class EmbeddingAdapters
{
    public static IEmbeddingProvider Create(string? provider, string? endpoint, string? region) => provider switch
    {
        "Qwen" when Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) =>
            new QwenEmbeddings(new HttpClient { Timeout = TimeSpan.FromSeconds(45) }, uri),
        "Bedrock" when !string.IsNullOrWhiteSpace(region) =>
            new TitanEmbeddings(new AmazonBedrockRuntimeClient(RegionEndpoint.GetBySystemName(region))),
        _ => throw new WorkspaceException("searchUnavailable", "Configure the selected workspace embedding provider.")
    };
}
