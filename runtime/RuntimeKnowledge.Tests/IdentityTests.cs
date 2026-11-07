using System.Net;
using Xunit;

namespace RuntimeKnowledge.Tests;

public sealed class IdentityTests
{
    [Theory]
    [InlineData("Keycloak")]
    [InlineData("Entra")]
    public async Task WorkspaceRolesAreRecheckedOnEveryRequest(string provider)
    {
        await using var f = await ApiFixture.OpenAsync(provider);
        using var reader = f.ClientWithToken(["ReadKnowledgeGraph"]);
        using var manager = f.ClientWithToken(["ManageKnowledgeGraph"]);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/api/history")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await f.PostAnnotationAsync(reader)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await f.PostAnnotationAsync(manager)).StatusCode);
        using var expired = f.ClientWithToken(["ManageKnowledgeGraph"], expired: true);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.PostAnnotationAsync(expired)).StatusCode);
        using var unrelated = f.ClientWithToken(["OtherRole"]);
        Assert.Equal(HttpStatusCode.Forbidden, (await unrelated.GetAsync("/api/inventory")).StatusCode);
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
            Assert.Equal(HttpStatusCode.Unauthorized, (await f.PostAnnotationAsync(client)).StatusCode);
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
}
