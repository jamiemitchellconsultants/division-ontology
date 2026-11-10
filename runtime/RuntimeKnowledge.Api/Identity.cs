using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using RuntimeKnowledge.Contracts;

namespace RuntimeKnowledge.Api;

internal static class Identity
{
    internal const string ReadPolicy = "WorkspaceRead";
    internal const string ManagePolicy = "WorkspaceManage";
    private const string ReadRole = "ReadKnowledgeGraph";
    private const string ManageRole = "ManageKnowledgeGraph";
    private const string Capability = "runtime-knowledge:capability";
    private const string ActorIssuer = "runtime-knowledge:issuer";
    private const string ActorSubject = "runtime-knowledge:subject";

    public static void AddWorkspaceIdentity(this IServiceCollection services)
    {
        const string bearer = JwtBearerDefaults.AuthenticationScheme;
        services.AddAuthentication(bearer).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(bearer).Configure<IConfiguration>(Configure).ValidateOnStart();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(ReadPolicy, policy => policy.RequireAuthenticatedUser().RequireClaim(Capability, "read"));
            options.AddPolicy(ManagePolicy, policy => policy.RequireAuthenticatedUser().RequireClaim(Capability, "manage"));
        });
    }

    private static void Configure(JwtBearerOptions options, IConfiguration configuration)
    {
        var settings = configuration.GetRequiredSection("Workspace:Identity");
        var provider = settings["Provider"];
        var issuer = settings["Issuer"];
        var audience = settings["Audience"];
        var clientId = settings["ClientId"];
        var localHttp = settings.GetValue<bool>("AllowLocalHttp");
        if (provider is not ("Keycloak" or "Entra") || string.IsNullOrWhiteSpace(audience) ||
            !Uri.TryCreate(issuer, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(localHttp && provider == "Keycloak" &&
                uri.Scheme == Uri.UriSchemeHttp && (uri.IsLoopback || uri.Host.EndsWith(".localhost", StringComparison.Ordinal)))) ||
            (provider == "Keycloak" && string.IsNullOrWhiteSpace(clientId)))
            throw new InvalidOperationException("Configure a trusted workspace identity provider, issuer and audience.");

        options.Authority = issuer;
        options.RequireHttpsMetadata = uri.Scheme == Uri.UriSchemeHttps;
        options.MapInboundClaims = false;
        options.IncludeErrorDetails = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            IssuerValidator = (actual, _, _) => actual == issuer
                ? actual : throw new SecurityTokenInvalidIssuerException(),
            AudienceValidator = (actual, _, _) => actual?.Contains(audience, StringComparer.Ordinal) == true
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (context.Principal?.Identity is not ClaimsIdentity identity ||
                    identity.FindAll("sub").Select(claim => claim.Value).ToArray() is not [var subject] ||
                    string.IsNullOrWhiteSpace(subject))
                {
                    context.Fail("Token has no stable subject.");
                    return Task.CompletedTask;
                }

                // These claims are derived locally. A token carrying these names grants no rights by itself.
                foreach (var claim in identity.Claims.Where(claim => claim.Type is Capability or ActorIssuer or ActorSubject).ToArray())
                    identity.RemoveClaim(claim);
                var roles = provider == "Keycloak" ? KeycloakRoles(identity, clientId!) :
                    identity.FindAll("roles").Select(claim => claim.Value).ToArray();
                if (roles.Contains(ManageRole, StringComparer.Ordinal))
                    identity.AddClaim(new Claim(Capability, "manage"));
                if (roles.Contains(ManageRole, StringComparer.Ordinal) || roles.Contains(ReadRole, StringComparer.Ordinal))
                    identity.AddClaim(new Claim(Capability, "read"));
                identity.AddClaim(new Claim(ActorIssuer, context.SecurityToken.Issuer));
                identity.AddClaim(new Claim(ActorSubject, subject));
                return Task.CompletedTask;
            }
        };
    }

    private static string[] KeycloakRoles(ClaimsIdentity identity, string clientId)
    {
        if (identity.FindAll("resource_access").ToArray() is not [var access]) return [];
        try
        {
            using var value = JsonDocument.Parse(access.Value);
            if (value.RootElement.ValueKind != JsonValueKind.Object ||
                !value.RootElement.TryGetProperty(clientId, out var client) ||
                client.ValueKind != JsonValueKind.Object ||
                !client.TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Array) return [];
            return roles.EnumerateArray().Where(role => role.ValueKind == JsonValueKind.String)
                .Select(role => role.GetString()!).ToArray();
        }
        catch (JsonException) { return []; }
    }

    public static Actor GetActor(ClaimsPrincipal principal) =>
        new(principal.FindFirstValue(ActorIssuer)!, principal.FindFirstValue(ActorSubject)!);

    public static WorkspaceSession GetSession(ClaimsPrincipal principal) =>
        new(principal.HasClaim(Capability, "read"), principal.HasClaim(Capability, "manage"));
}
