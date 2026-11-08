using System.Text.Json;
using Microsoft.JSInterop;
using RuntimeKnowledge.Contracts;

namespace RuntimeKnowledge.Web.Services;

public sealed class DraftState(IJSRuntime js)
{
    private const string Storage = "runtime-knowledge.workspace.drafts";

    public async Task<ContributionDraft?> Get(string key) =>
        (await Read()).GetValueOrDefault(key);

    public async Task Save(string key, ContributionDraft draft)
    {
        var drafts = await Read();
        drafts[key] = draft;
        await Write(drafts);
    }

    public async Task Remove(string key)
    {
        var drafts = await Read();
        drafts.Remove(key);
        await Write(drafts);
    }

    public ValueTask Clear() => js.InvokeVoidAsync("sessionStorage.removeItem", Storage);

    private ValueTask Write(Dictionary<string, ContributionDraft> drafts) =>
        js.InvokeVoidAsync("sessionStorage.setItem", Storage, JsonSerializer.Serialize(drafts, WorkspaceJson.Options));

    private async Task<Dictionary<string, ContributionDraft>> Read()
    {
        var value = await js.InvokeAsync<string?>("sessionStorage.getItem", Storage);
        return value is null ? [] : JsonSerializer.Deserialize<Dictionary<string, ContributionDraft>>(value, WorkspaceJson.Options) ?? [];
    }
}

public sealed record ContributionDraft(int Version, string Category, string Text, PublishedKey? From,
    PublishedKey? To, PublishedKey? Type, string Label, string Explanation, PublishedKey? Codification = null);
