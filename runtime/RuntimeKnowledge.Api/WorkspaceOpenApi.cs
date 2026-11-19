namespace RuntimeKnowledge.Api;

internal static class WorkspaceOpenApi
{
    public const string DocumentName = "v1";

    public static void AddWorkspaceOpenApi(this IServiceCollection services) => services.AddOpenApi(DocumentName);
}
