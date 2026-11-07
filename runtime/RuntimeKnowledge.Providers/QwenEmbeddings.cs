using System.Net.Http.Json;
using System.Text.Json;
using RuntimeKnowledge.Contracts;

namespace RuntimeKnowledge.Providers;

public sealed class QwenEmbeddings(HttpClient client, Uri endpoint) : IEmbeddingProvider, IDisposable
{
    public async Task<float[]> EmbedAsync(EmbeddingProfile profile, string input, EmbeddingPurpose purpose,
        CancellationToken token)
    {
        if (profile.Provider != "Qwen" || profile.TextProfile != "knowledge-v1")
            throw new WorkspaceException("invalidEmbedding", "Qwen embedding profile is incompatible with this provider.");
        var text = purpose == EmbeddingPurpose.Query
            ? $"Instruct: Given a business question, retrieve relevant knowledge records.\nQuery: {input}"
            : input;
        try
        {
            using var response = await client.PostAsJsonAsync(endpoint, new { model = profile.Model, input = text }, token);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token));
            var vector = document.RootElement.GetProperty("data")[0].GetProperty("embedding")
                .Deserialize<float[]>();
            profile.ValidateVector(vector);
            return vector!;
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new WorkspaceException("invalidEmbedding", "The Qwen endpoint returned a malformed embedding.", error);
        }
    }

    public void Dispose() => client.Dispose();
}
