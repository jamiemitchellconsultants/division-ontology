using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Impact.Engine;
using RuntimeKnowledge.Store;
using RuntimeKnowledge.Tests.Impact;
using RuntimeKnowledge.Tests.OpenApi;
using Xunit;

namespace RuntimeKnowledge.Tests;

/// <summary>One engine, so the workspace and the command line render the same bytes (B23, Q3), and a saved
/// assessment survives restart, a new snapshot and a rebuild unchanged (Q5).</summary>
[Trait("Category", "Database")]
public sealed class AssessmentIdentityTests
{
    private static readonly string[] Manage = ["ReadKnowledgeGraph", "ManageKnowledgeGraph"];
    private static readonly string Input = Path.Combine(RepositoryPaths.Root, "test", "impact-corpus", "confluence", "inputs", "007", "assessment-input.json");

    private static async Task<(ApiFixture Api, string Snapshot)> OpenAsync()
    {
        string? snapshot = null;
        var api = await ApiFixture.OpenAsync("Keycloak", async scenario =>
        {
            snapshot = scenario.Bundles.AddCorpusBundle("confluence");
            await scenario.Store.StageAsync(snapshot, CancellationToken.None);
            await scenario.Store.ActivateAsync(snapshot, CancellationToken.None);
        });
        return (api, snapshot!);
    }

    private static JsonObject Draft() => JsonNode.Parse(File.ReadAllText(Input))!.AsObject();

    private static async Task<Guid> SaveAsync(ApiFixture api, string snapshot) =>
        (await (await api.ClientWithToken(Manage).PostAsJsonAsync("api/assessments", new AssessmentDraftRequest(snapshot, Draft())))
            .Content.ReadFromJsonAsync<AssessmentSaved>(WorkspaceJson.Options))!.Id;

    private static async Task<(string Frozen, byte[] Markdown)> ReadAsync(ApiFixture api, Guid id)
    {
        var client = api.ClientWithToken(["ReadKnowledgeGraph"]);
        var saved = await client.GetFromJsonAsync<SavedAssessment>($"api/assessments/{id:D}", WorkspaceJson.Options);
        return (saved!.Frozen.ToJsonString(), await client.GetByteArrayAsync($"api/assessments/{id:D}/markdown"));
    }

    [Fact]
    public async Task Workspace_export_equals_the_command_line_render_byte_for_byte()             // B23, Q3
    {
        var staging = Directory.CreateTempSubdirectory("impact-identity-");
        try
        {
            var manifest = CorpusPaths.Steps("confluence")[7]!["manifest"]!.GetValue<string>();
            var (exit, files, error) = CommandRunner.Run(["assessment-render", "--bundle", CorpusPaths.Objects,
                "--manifest", manifest, "--staging", staging.FullName, "--input", Input]);
            Assert.True(exit == 0, error);
            var commandLine = Encoding.UTF8.GetBytes(files.Single(f => f.Name == "assessment.md").Text);

            var (api, snapshot) = await OpenAsync();
            await using var _ = api;
            var id = await SaveAsync(api, snapshot);
            Assert.Equal(commandLine, (await ReadAsync(api, id)).Markdown);

            var reader = api.ClientWithToken(["ReadKnowledgeGraph"]);
            async Task<AssessmentPreview> PreviewAsync() =>
                (await (await reader.PostAsJsonAsync("api/assessments/preview", new AssessmentDraftRequest(snapshot, Draft())))
                    .Content.ReadFromJsonAsync<AssessmentPreview>(WorkspaceJson.Options))!;
            var first = await PreviewAsync();
            var second = await PreviewAsync();
            Assert.Equal(first.Frozen.ToJsonString(), second.Frozen.ToJsonString());
            Assert.Equal(first.Markdown, second.Markdown);
            Assert.Equal(commandLine, Encoding.UTF8.GetBytes(first.Markdown));
        }
        finally { staging.Delete(recursive: true); }
    }

    [Fact]
    public async Task A_saved_assessment_opens_identically_after_restart_activation_and_rebuild()  // Q5
    {
        var (api, snapshot) = await OpenAsync();
        await using var _ = api;
        var id = await SaveAsync(api, snapshot);
        var (frozen, markdown) = await ReadAsync(api, id);

        void AssertUnchanged((string Frozen, byte[] Markdown) read, string when)
        {
            Assert.True(frozen == read.Frozen, $"frozen content changed after {when}");
            Assert.True(markdown.AsSpan().SequenceEqual(read.Markdown), $"Markdown bytes changed after {when}");
        }

        await api.RestartAsync();
        AssertUnchanged(await ReadAsync(api, id), "an API restart");

        await api.Scenario.ActivateAsync("initial");
        AssertUnchanged(await ReadAsync(api, id), "a new snapshot");

        var replay = new ReplayStore(api.Scenario.Db, api.Scenario.Bundles);
        await replay.SetWritesPausedAsync(true, CancellationToken.None);
        var current = await replay.DescribeCurrentAsync(CancellationToken.None);
        var rebuilt = await replay.RebuildAsync(current.ThroughPosition, CancellationToken.None);
        Assert.Equal(current.StateDigest, rebuilt.StateDigest);
        await replay.CutoverAsync(rebuilt.Generation, current.ThroughPosition, CancellationToken.None);
        await replay.SetWritesPausedAsync(false, CancellationToken.None);
        AssertUnchanged(await ReadAsync(api, id), "a rebuild and cutover");
    }
}
