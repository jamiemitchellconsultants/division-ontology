using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Amazon;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using RuntimeKnowledge.Api;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Providers;
using RuntimeKnowledge.Worker;
using Xunit;

namespace RuntimeKnowledge.Tests;

public sealed class AwsSimulationTests
{
    private const string Issuer = "https://login.fixture.test/tenant/v2.0";
    private const string Audience = "api://workspace-fixture";
    private static readonly TestContext Context = new();

    [Fact]
    public async Task EntraGatewayAndScheduledHandlersShareTheWorkspace()
    {
        await using var scenario = await Scenario.OpenAsync();
        var manifest = scenario.Bundles.AddSnapshot("initial");
        using var s3 = new FixtureS3(scenario.Bundles);
        using var bedrock = new EmbeddingAdapterTests.CaptureBedrock();
        using var titan = new TitanEmbeddings(bedrock);
        var profile = new EmbeddingProfile("Bedrock", TitanEmbeddings.ModelId, "v2", 1024, "knowledge-v1");
        LambdaEntry Worker() => new(scenario.Db, new BundleObjects(s3, "fixture-bundles"), titan, profile);

        Assert.Equal("waitingForProfile", (await Worker().ImportHandler(new ImportRequest(manifest), Context)).State);
        await Worker().IndexHandler(Context);
        Assert.Equal("staged", (await Worker().ImportHandler(new ImportRequest(manifest), Context)).State);
        Assert.Equal("waitingForIndex", (await Worker().ImportHandler(new ImportRequest(manifest), Context)).State);
        Assert.True((await Worker().IndexHandler(Context)).Indexed > 0);
        var activated = await Worker().ImportHandler(new ImportRequest(manifest), Context);
        Assert.Equal("active", activated.State);
        Assert.Equal(1, activated.Position);
        Assert.Equal(activated, await Worker().ImportHandler(new ImportRequest(manifest), Context));
        Assert.Equal(1, s3.ReadKeys.Count(key => key == $"manifests/{manifest}.json"));
        Assert.Equal(TitanEmbeddings.ModelId, bedrock.ModelId);

        using var rsa = RSA.Create(2048);
        var signingKey = new RsaSecurityKey(rsa) { KeyId = "workspace-lambda-fixture" };
        var metadata = new OpenIdConnectConfiguration { Issuer = Issuer };
        metadata.SigningKeys.Add(signingKey);
        var api = new LambdaHosting(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Workspace"] = scenario.ConnectionString,
                ["Workspace:Identity:Provider"] = "Entra",
                ["Workspace:Identity:Issuer"] = Issuer,
                ["Workspace:Identity:Audience"] = Audience
            }));
            builder.ConfigureServices(services => services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme, options =>
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata)));
        });

        var reader = Signed("ReadKnowledgeGraph", signingKey);
        var manager = Signed("ManageKnowledgeGraph", signingKey);
        const string path = "/api/contributions/relationships";
        var change = new CreateRelationship(scenario.A, scenario.B, RelationshipMeaning.Custom("supplies", "Lambda fixture"));
        var anonymous = await api.FunctionHandlerAsync(Event("POST", path, change), Context);
        Assert.Equal(401, anonymous.StatusCode);
        Assert.Contains("\"authenticationRequired\"", anonymous.Body);
        var readerOnly = await api.FunctionHandlerAsync(Event("POST", path, change, reader), Context);
        Assert.Equal(403, readerOnly.StatusCode);
        Assert.Contains("\"roleRequired\"", readerOnly.Body);
        var created = await api.FunctionHandlerAsync(Event("POST", path, change, manager), Context);
        Assert.Equal(201, created.StatusCode);
        var relationship = JsonSerializer.Deserialize<ContributionResult>(created.Body, WorkspaceJson.Options)!;
        Assert.Equal(2, relationship.Position);

        var traversed = await api.FunctionHandlerAsync(Event("POST", "/api/traverse", new TraversalRequest(scenario.A), reader), Context);
        Assert.Equal(200, traversed.StatusCode);
        var paths = JsonSerializer.Deserialize<TraversalResult>(traversed.Body, WorkspaceJson.Options)!;
        Assert.Equal(manifest, paths.SnapshotId);
        Assert.Contains(paths.Paths.SelectMany(item => item.Steps), step => step.RuntimeId == relationship.Id);
    }

    [Fact, Trait("Category", "OpenApi")]
    public async Task LambdaHostGivesRefusalsTheirBodies()
    {
        var api = DocsFreeLambda();
        var anonymous = await api.FunctionHandlerAsync(Request("GET", "/api/inventory"), Context);
        Assert.Equal(401, anonymous.StatusCode);
        Assert.Equal("Bearer", Header(anonymous, "www-authenticate"));
        Assert.Equal("authenticationRequired", JsonDocument.Parse(anonymous.Body).RootElement.GetProperty("code").GetString());
        var plain = await api.FunctionHandlerAsync(Request("POST", "/api/search", "text/plain", "x"), Context);
        Assert.Equal(415, plain.StatusCode);
        Assert.True(string.IsNullOrEmpty(plain.Body));
        var unknown = await api.FunctionHandlerAsync(Request("GET", "/api/no-such-operation"), Context);
        Assert.Equal(404, unknown.StatusCode);
        Assert.True(string.IsNullOrEmpty(unknown.Body));
    }

    /// <summary>A Lambda host with no database, for requests that never reach the store.</summary>
    private static LambdaHosting DocsFreeLambda(IReadOnlyDictionary<string, string?>? extra = null)
    {
        var metadata = new OpenIdConnectConfiguration { Issuer = Issuer };
        var settings = new Dictionary<string, string?>
        {
            ["Workspace:Identity:Provider"] = "Entra",
            ["Workspace:Identity:Issuer"] = Issuer,
            ["Workspace:Identity:Audience"] = Audience
        };
        foreach (var (key, value) in extra ?? new Dictionary<string, string?>()) settings[key] = value;
        return new LambdaHosting(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
            builder.ConfigureServices(services => services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme, options =>
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata)));
        });
    }

    private static string? Header(APIGatewayHttpApiV2ProxyResponse response, string name) =>
        response.Headers?.FirstOrDefault(header => string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    [Fact]
    public async Task BundleAdapterUsesTheExpectedS3WireKeysAndImmutablePut()
    {
        using var transport = new BundleTransport();
        using var s3 = new AmazonS3Client(new AnonymousAWSCredentials(), new AmazonS3Config
        {
            ServiceURL = "https://s3.fixture.test",
            AuthenticationRegion = "eu-west-2",
            ForcePathStyle = true,
            HttpClientFactory = new FixtureHttpFactory(transport)
        });
        var bytes = new byte[] { 1, 2, 3 };
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var key = $"blobs/{digest}";
        var objects = new BundleObjects(s3, "fixture-bundles");
        using var input = new MemoryStream(bytes);
        await objects.PutImmutableAsync(key, input, CancellationToken.None);
        using var downloaded = await objects.OpenAsync(key, CancellationToken.None);
        using var result = new MemoryStream();
        await downloaded.CopyToAsync(result);
        Assert.Equal(bytes, result.ToArray());
        Assert.Equal([new(HttpMethod.Put, $"/fixture-bundles/{key}"), new(HttpMethod.Get, $"/fixture-bundles/{key}")],
            transport.Requests);
    }

    private sealed class FixtureHttpFactory(HttpMessageHandler handler) : HttpClientFactory
    {
        public override HttpClient CreateHttpClient(IClientConfig config) => new(handler, disposeHandler: false);
    }

    private sealed class BundleTransport : HttpMessageHandler
    {
        private byte[]? _stored;
        public List<KeyValuePair<HttpMethod, string>> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("s3.fixture.test", request.RequestUri!.Host);
            Requests.Add(new(request.Method, request.RequestUri.AbsolutePath));
            if (request.Method == HttpMethod.Put)
            {
                Assert.Equal("*", Assert.Single(request.Headers.GetValues("If-None-Match")));
                _stored = await request.Content!.ReadAsByteArrayAsync(token);
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"fixture\"");
                return response;
            }
            Assert.Equal(HttpMethod.Get, request.Method);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_stored!) };
        }
    }

    private static string Signed(string role, RsaSecurityKey key) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(Issuer, Audience,
            [new Claim("sub", "workspace-fixture-user"), new Claim("roles", role)],
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256)));

    private static APIGatewayHttpApiV2ProxyRequest Event(string method, string path, object body, string? bearer = null)
    {
        var headers = new Dictionary<string, string> { ["host"] = "workspace.fixture.test", ["content-type"] = "application/json" };
        if (bearer is not null) headers["authorization"] = $"Bearer {bearer}";
        return new APIGatewayHttpApiV2ProxyRequest
        {
            Version = "2.0",
            RawPath = path,
            RawQueryString = "",
            Headers = headers,
            RequestContext = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext
            {
                Stage = "$default",
                Http = new APIGatewayHttpApiV2ProxyRequest.HttpDescription { Method = method, Path = path, Protocol = "HTTP/1.1", SourceIp = "127.0.0.1" }
            },
            Body = JsonSerializer.Serialize(body, WorkspaceJson.Options)
        };
    }

    private static APIGatewayHttpApiV2ProxyRequest Request(string method, string path, string? contentType = null, string? body = null)
    {
        var headers = new Dictionary<string, string> { ["host"] = "workspace.fixture.test" };
        if (contentType is not null) headers["content-type"] = contentType;
        return new APIGatewayHttpApiV2ProxyRequest
        {
            Version = "2.0",
            RawPath = path,
            RawQueryString = "",
            Headers = headers,
            RequestContext = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext
            {
                Stage = "$default",
                Http = new APIGatewayHttpApiV2ProxyRequest.HttpDescription { Method = method, Path = path, Protocol = "HTTP/1.1", SourceIp = "127.0.0.1" }
            },
            Body = body
        };
    }

    private sealed class FixtureS3(MemoryBundles objects)
        : AmazonS3Client(new AnonymousAWSCredentials(), RegionEndpoint.EUWest2)
    {
        public List<string> ReadKeys { get; } = [];

        public override async Task<GetObjectResponse> GetObjectAsync(string bucket, string key, CancellationToken token)
        {
            Assert.Equal("fixture-bundles", bucket);
            ReadKeys.Add(key);
            var stream = await objects.OpenAsync(key, token);
            return new GetObjectResponse { ResponseStream = stream, ContentLength = stream.Length };
        }
    }

    private sealed class TestContext : ILambdaContext
    {
        public string AwsRequestId => "fixture-request";
        public IClientContext ClientContext => null!;
        public string FunctionName => "workspace-test";
        public string FunctionVersion => "$LATEST";
        public ICognitoIdentity Identity => null!;
        public string InvokedFunctionArn => "arn:aws:lambda:eu-west-2:123456789012:function:workspace-test";
        public ILambdaLogger Logger { get; } = new TestLogger();
        public string LogGroupName => "/aws/lambda/workspace-test";
        public string LogStreamName => "fixture";
        public int MemoryLimitInMB => 512;
        public TimeSpan RemainingTime => TimeSpan.FromMinutes(1);
    }

    private sealed class TestLogger : ILambdaLogger
    {
        public void Log(string message) { }
        public void LogLine(string message) { }
    }
}
