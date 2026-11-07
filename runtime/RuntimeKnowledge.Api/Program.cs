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
        builder.Services.AddWorkspaceIdentity();
        builder.Services.AddProblemDetails();
        builder.Services.AddSingleton<NpgsqlDataSource>(services =>
        {
            var configuration = services.GetRequiredService<IConfiguration>();
            var database = configuration.GetConnectionString("Workspace");
            if (string.IsNullOrWhiteSpace(database))
                throw new InvalidOperationException("Configure the workspace database connection.");
            return NpgsqlDataSource.Create(database);
        });
        builder.Services.AddSingleton<Db>();
        builder.Services.AddSingleton<GraphQueries>();
        builder.Services.AddSingleton<ContributionStore>();
        builder.Services.AddSingleton<IEmbeddingProvider>(services =>
        {
            var config = services.GetRequiredService<IConfiguration>().GetSection("Workspace:Embeddings");
            return EmbeddingAdapters.Create(config["Provider"], config["Endpoint"], config["Region"]);
        });
        builder.Services.AddSingleton<SearchStore>(services =>
        {
            var settings = services.GetRequiredService<IConfiguration>();
            return new SearchStore(services.GetRequiredService<Db>(), services.GetRequiredService<IEmbeddingProvider>(),
                settings.GetValue("Workspace:Embeddings:MinimumSimilarity", 0.35));
        });

        var app = builder.Build();
        app.UseExceptionHandler(exceptionApp => exceptionApp.Run(ProblemResponses.WriteAsync));
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/health", async (Db db, CancellationToken token) =>
        {
            await using var connection = await db.OpenAsync(token);
            return Results.Ok(new { status = "ready" });
        }).AllowAnonymous();
        app.MapWorkspaceRoutes();
        return app;
    }
}
