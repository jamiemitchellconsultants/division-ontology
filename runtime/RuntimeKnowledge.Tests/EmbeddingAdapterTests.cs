using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Providers;
using Xunit;

namespace RuntimeKnowledge.Tests;

public sealed class EmbeddingAdapterTests
{
    [Fact]
    public async Task QwenUsesTheConfiguredModelAndRejectsWrongDimensions()
    {
        using var handler = new CaptureHttp();
        using var http = new HttpClient(handler);
        var adapter = new QwenEmbeddings(http, new Uri("http://fixture.test/v1/embeddings"));
        var profile = new EmbeddingProfile("Qwen", "qwen3-embedding:0.6b", "pinned", 1024, "knowledge-v1");
        var vector = await adapter.EmbedAsync(profile, "Where are invoices reconciled?", EmbeddingPurpose.Query,
            CancellationToken.None);
        Assert.Equal(1024, vector.Length);
        Assert.Equal(profile.Model, handler.Body.GetProperty("model").GetString());
        Assert.Contains("Query: Where are invoices reconciled?", handler.Body.GetProperty("input").GetString());
        handler.Dimensions = 2;
        var error = await Assert.ThrowsAsync<WorkspaceException>(() =>
            adapter.EmbedAsync(profile, "document", EmbeddingPurpose.Document, CancellationToken.None));
        Assert.Equal("invalidEmbedding", error.Code);
    }

    [Fact]
    public async Task TitanSendsV2ModelAndNormalized1024DimensionRequest()
    {
        using var bedrock = new CaptureBedrock();
        using var adapter = new TitanEmbeddings(bedrock);
        var profile = new EmbeddingProfile("Bedrock", TitanEmbeddings.ModelId, "v2", 1024, "knowledge-v1");
        var vector = await adapter.EmbedAsync(profile, "Order evidence", EmbeddingPurpose.Document, CancellationToken.None);
        Assert.Equal(1024, vector.Length);
        Assert.Equal("amazon.titan-embed-text-v2:0", bedrock.ModelId);
        Assert.Equal("Order evidence", bedrock.Body.GetProperty("inputText").GetString());
        Assert.Equal(1024, bedrock.Body.GetProperty("dimensions").GetInt32());
        Assert.True(bedrock.Body.GetProperty("normalize").GetBoolean());
    }

    internal sealed class CaptureBedrock() : AmazonBedrockRuntimeClient(new AnonymousAWSCredentials(), RegionEndpoint.EUWest2)
    {
        public JsonElement Body { get; private set; }
        public string? ModelId { get; private set; }

        public override async Task<InvokeModelResponse> InvokeModelAsync(InvokeModelRequest request, CancellationToken token)
        {
            ModelId = request.ModelId;
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: token);
            Body = document.RootElement.Clone();
            var vector = new float[1024];
            vector[0] = 1;
            return new InvokeModelResponse
            {
                Body = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new { embedding = vector }))
            };
        }
    }

    private sealed class CaptureHttp : HttpMessageHandler
    {
        public JsonElement Body { get; private set; }
        public int Dimensions { get; set; } = 1024;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Body = document.RootElement.Clone();
            var vector = new float[Dimensions];
            vector[0] = 1;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { data = new[] { new { embedding = vector } } })
            };
        }
    }
}
