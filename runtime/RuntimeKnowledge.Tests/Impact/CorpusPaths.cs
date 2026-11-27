using RuntimeKnowledge.Tests.OpenApi;
using System.Text.Json.Nodes;

namespace RuntimeKnowledge.Tests.Impact;

internal static class CorpusPaths
{
    public static string Corpus { get; } = Path.Combine(RepositoryPaths.Root, "test", "impact-corpus");
    public static string Objects { get; } = Path.Combine(Corpus, "objects");
    public static JsonArray Steps(string scenario) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(Corpus, scenario, "steps.json")))!.AsArray();
    public static string Expected(string scenario, int step, string file) =>
        Path.Combine(Corpus, scenario, "expected", step.ToString("000"), file);
    public static string FirstManifest(string scenario, string command) =>
        Steps(scenario).First(s => s!["argv"]![0]!.GetValue<string>() == command && s["manifest"] is not null)!["manifest"]!.GetValue<string>();
}
