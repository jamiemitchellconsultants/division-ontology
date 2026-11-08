using Amazon.Lambda.AspNetCoreServer;
using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace RuntimeKnowledge.Api;

public sealed class LambdaHosting : APIGatewayHttpApiV2ProxyFunction
{
    private readonly Action<IWebHostBuilder>? _configure;

    public LambdaHosting() : this(null) { }

    public LambdaHosting(Action<IWebHostBuilder>? configure) : base(StartupMode.FirstRequest) =>
        _configure = configure;

    protected override void Init(IWebHostBuilder builder)
    {
        builder.ConfigureServices(ApiProgram.ConfigureServices);
        builder.Configure(ApiProgram.Configure);
        _configure?.Invoke(builder);
    }
}
