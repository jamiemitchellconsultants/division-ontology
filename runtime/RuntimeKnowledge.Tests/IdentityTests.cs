using System.Net;
using System.Net.Http.Json;
using RuntimeKnowledge.Contracts;
using Xunit;

namespace RuntimeKnowledge.Tests;

public sealed class IdentityTests
{
    [Fact]
    public async Task AnotherManagerCanEditAndRemoveWithTheirOwnAttribution()
    {
        await using var f = await ApiFixture.OpenAsync("Keycloak");
        using var author = f.ClientWithToken(["ManageKnowledgeGraph"], subject: "original-manager");
        using var editor = f.ClientWithToken(["ManageKnowledgeGraph"], subject: "second-manager");
        using var reader = f.ClientWithToken(["ReadKnowledgeGraph"], subject: "fixture-reader");
        using var created = await f.PostAnnotationAsync(author);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var note = (await created.Content.ReadFromJsonAsync<ContributionResult>())!;
        var path = $"/api/contributions/{note.Id:D}";
        var edit = new EditContributionRequest(note.Version, "businessExplanation", "Clarified by the second manager.");
        using var deniedEdit = await reader.PutAsJsonAsync(path, edit);
        using var deniedRemoval = await reader.PostAsJsonAsync(path + "/remove", new RemoveContributionRequest(note.Version));
        Assert.Equal(HttpStatusCode.Forbidden, deniedEdit.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deniedRemoval.StatusCode);

        using var edited = await editor.PutAsJsonAsync(path, edit);
        edited.EnsureSuccessStatusCode();
        var revision = (await edited.Content.ReadFromJsonAsync<ContributionResult>())!;
        using var removed = await editor.PostAsJsonAsync(path + "/remove", new RemoveContributionRequest(revision.Version));
        removed.EnsureSuccessStatusCode();

        var history = (await reader.GetFromJsonAsync<HistoryPage>($"/api/history?contributionId={note.Id:D}"))!;
        Assert.Equal(new[] { "original-manager", "second-manager", "second-manager" },
            history.Entries.OrderBy(entry => entry.Position).Select(entry => entry.Actor.Subject));
        var removal = Assert.Single(history.Entries, entry => entry.After.Removed);
        Assert.Equal(note.Id, removal.ContributionId);
        Assert.Equal("original-manager", removal.After.Author.Subject);
        Assert.Equal(edit.Text, removal.Before!.Annotation!.Text);
    }

    [Theory]
    [InlineData("Keycloak")]
    [InlineData("Entra")]
    public async Task WorkspaceRolesAreRecheckedOnEveryRequest(string provider)
    {
        await using var f = await ApiFixture.OpenAsync(provider);
        using var reader = f.ClientWithToken(["ReadKnowledgeGraph"]);
        using var manager = f.ClientWithToken(["ManageKnowledgeGraph"]);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/api/history")).StatusCode);
        await AssertRefusal(await f.PostAnnotationAsync(reader), HttpStatusCode.Forbidden, "roleRequired");
        Assert.Equal(HttpStatusCode.Created, (await f.PostAnnotationAsync(manager)).StatusCode);
        using var expired = f.ClientWithToken(["ManageKnowledgeGraph"], expired: true);
        await AssertRefusal(await f.PostAnnotationAsync(expired), HttpStatusCode.Unauthorized, "authenticationRequired");
        using var unrelated = f.ClientWithToken(["OtherRole"]);
        await AssertRefusal(await unrelated.GetAsync("/api/inventory"), HttpStatusCode.Forbidden, "roleRequired");
        Assert.Equal(1, await f.AnnotationCountAsync());
    }

    [Theory]
    [InlineData("Keycloak")]
    [InlineData("Entra")]
    public async Task InvalidTokensAreDeniedBeforeWrites(string provider)
    {
        await using var f = await ApiFixture.OpenAsync(provider);
        using var noToken = f.CreateClient();
        using var wrongIssuer = f.ClientWithToken(["ManageKnowledgeGraph"], issuer: "https://wrong.test");
        using var wrongAudience = f.ClientWithToken(["ManageKnowledgeGraph"], audience: "other-api");
        using var badSignature = f.ClientWithToken(["ManageKnowledgeGraph"], wrongSignature: true);
        foreach (var client in new[] { noToken, wrongIssuer, wrongAudience, badSignature })
            await AssertRefusal(await f.PostAnnotationAsync(client), HttpStatusCode.Unauthorized, "authenticationRequired");
        Assert.Equal(0, await f.AnnotationCountAsync());
    }

    [Fact]
    public async Task KeycloakRequiresTheConfiguredClientAndTrustedRoleClaims()
    {
        await using var f = await ApiFixture.OpenAsync("Keycloak");
        using var otherClient = f.ClientWithToken(["ManageKnowledgeGraph"], keycloakClient: "elsewhere");
        using var forgedNormalizedClaim = f.ClientWithToken([], injectCapability: true);
        using var both = f.ClientWithToken(["ManageKnowledgeGraph", "ReadKnowledgeGraph"]);
        Assert.Equal(HttpStatusCode.Forbidden, (await f.PostAnnotationAsync(otherClient)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await f.PostAnnotationAsync(forgedNormalizedClaim)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await f.PostAnnotationAsync(both)).StatusCode);
        Assert.Equal(1, await f.AnnotationCountAsync());
    }

    private static async Task AssertRefusal(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }
}
