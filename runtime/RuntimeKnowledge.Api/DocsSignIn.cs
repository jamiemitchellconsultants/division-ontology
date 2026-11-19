using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.OpenApi;

namespace RuntimeKnowledge.Api;

/// <summary>
/// The served spec's deployment transformer. It removes the request-derived server address (ADR 0017) and adds
/// the identity provider's sign-in details from its discovery document (ADR 0015). Discovery never blocks the spec.
/// </summary>
internal sealed class DocsSignIn(IConfiguration configuration, IOptionsMonitor<JwtBearerOptions> jwt) : IOpenApiDocumentTransformer
{
    public const string Scheme = "workspaceSignIn";
    public static TimeSpan DiscoveryWait { get; set; } = TimeSpan.FromSeconds(2);

    public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        // Unconditionally: nothing in a request may change where the docs UI sends a token.
        document.Servers = null;

        var clientId = configuration["Workspace:Docs:ClientId"];
        if (BuildTimeGeneration.IsActive || string.IsNullOrWhiteSpace(clientId) || !configuration.GetSection("Workspace:Identity").Exists())
            return;
        if (await DiscoverAsync(cancellationToken) is not { } discovery ||
            !Uri.TryCreate(discovery.AuthorizationEndpoint, UriKind.Absolute, out var authorization) ||
            !Uri.TryCreate(discovery.TokenEndpoint, UriKind.Absolute, out var token))
            return;

        var scopes = (configuration["Workspace:Docs:Scopes"] ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[Scheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OAuth2,
            Description = "Sign in with this deployment's identity provider (authorization code with PKCE).",
            Flows = new OpenApiOAuthFlows
            {
                AuthorizationCode = new OpenApiOAuthFlow
                {
                    AuthorizationUrl = authorization,
                    TokenUrl = token,
                    Scopes = scopes.ToDictionary(scope => scope, _ => "Workspace API access", StringComparer.Ordinal)
                }
            }
        };

        var reference = new OpenApiSecuritySchemeReference(Scheme, document);
        foreach (var operation in document.Paths.Values.SelectMany(item => item.Operations?.Values ?? Enumerable.Empty<OpenApiOperation>()))
            if (operation.Security is { Count: > 0 } security)
                security.Add(new OpenApiSecurityRequirement { [reference] = scopes.ToList() });
    }

    private async Task<OpenIdConnectConfiguration?> DiscoverAsync(CancellationToken cancellationToken)
    {
        if (jwt.Get(JwtBearerDefaults.AuthenticationScheme).ConfigurationManager is not { } manager) return null;
        try
        {
            // The fetch carries on after a timeout, and the configuration manager caches it for later requests.
            return await manager.GetConfigurationAsync(CancellationToken.None).WaitAsync(DiscoveryWait, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
