namespace RuntimeKnowledge.Tests.OpenApi;

internal static class RepositoryPaths
{
    public static string Root { get; } = FindRoot();
    public static string Spec { get; } = Path.Combine(Root, "runtime", "openapi", "workspace-api.json");

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "runtime", "RuntimeKnowledge.slnx")))
                return directory.FullName;
        throw new InvalidOperationException("Could not find the repository root above " + AppContext.BaseDirectory);
    }
}
