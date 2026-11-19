namespace RuntimeKnowledge.Api;

/// <summary>Swagger UI at /api/docs, signing in with the web UI's public client and PKCE (ADR 0014, ADR 0015).</summary>
internal static class DocsUi
{
    public const string SpecPath = "/api/openapi/v1.json";
    public const string RoutePrefix = "api/docs";

    public static void UseWorkspaceDocsUi(this IApplicationBuilder app)
    {
        var docs = app.ApplicationServices.GetRequiredService<IConfiguration>().GetSection("Workspace:Docs");
        app.UseSwaggerUI(options =>
        {
            options.RoutePrefix = RoutePrefix;
            options.DocumentTitle = "Runtime knowledge workspace API";
            options.SwaggerEndpoint(SpecPath, "v1");
            if (string.IsNullOrWhiteSpace(docs["ClientId"])) return;
            options.OAuthClientId(docs["ClientId"]);
            options.OAuthUsePkce();
            options.OAuthScopes((docs["Scopes"] ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            // Swagger UI renders a secret row even for the workspace's public PKCE client.
            options.HeadContent = """
                <style>
                    .auth-container .wrapper:has(> label[for="client_secret_authorizationCode"]) {
                        display: none;
                    }
                </style>
                """;
        });
    }
}
