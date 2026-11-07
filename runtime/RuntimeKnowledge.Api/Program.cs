using Npgsql;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Providers;
using RuntimeKnowledge.Store;

namespace RuntimeKnowledge.Api;

public partial class ApiProgram
{
    public static void Main(string[] args) => Build(args).Run();

    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        ConfigureServices(builder.Services);
        var app = builder.Build();
        Configure(app);
        return app;
    }

    internal static void ConfigureServices(IServiceCollection services)
    {
        services.AddWorkspaceIdentity();
        services.AddProblemDetails();
        services.AddSingleton<NpgsqlDataSource>(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var database = DatabaseSecrets.ResolveAsync(configuration.GetConnectionString("Workspace"),
                configuration["Workspace:Database:SecretArn"], configuration["Workspace:Database:Host"],
                configuration["Workspace:Database:MaximumPoolSize"], CancellationToken.None).GetAwaiter().GetResult();
            return NpgsqlDataSource.Create(database);
        });
        services.AddSingleton<Db>();
        services.AddSingleton<GraphQueries>();
        services.AddSingleton<ContributionStore>();
        services.AddSingleton<IEmbeddingProvider>(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>().GetSection("Workspace:Embeddings");
            return EmbeddingAdapters.Create(config["Provider"], config["Endpoint"], config["Region"]);
        });
        services.AddSingleton<SearchStore>(provider =>
        {
            var settings = provider.GetRequiredService<IConfiguration>();
            return new SearchStore(provider.GetRequiredService<Db>(), provider.GetRequiredService<IEmbeddingProvider>(),
                settings.GetValue("Workspace:Embeddings:MinimumSimilarity", 0.35));
        });
    }

    internal static void Configure(IApplicationBuilder app)
    {
        app.UseExceptionHandler(exceptionApp => exceptionApp.Run(ProblemResponses.WriteAsync));
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapGet("/health", async (Db db, CancellationToken token) =>
            {
                await using var connection = await db.OpenAsync(token);
                return Results.Ok(new { status = "ready" });
            }).AllowAnonymous();
            endpoints.MapWorkspaceRoutes();
        });
    }
}
