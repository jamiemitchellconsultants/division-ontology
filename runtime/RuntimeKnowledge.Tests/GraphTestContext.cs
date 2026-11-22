using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using RuntimeKnowledge.Web.Services;

namespace RuntimeKnowledge.Tests;

internal static class GraphTestContext
{
    public static BunitJSModuleInterop SetupDiagram(BunitContext context) =>
        context.JSInterop.SetupModule("./graph-diagram.mjs")
            .SetupModule(invocation => invocation.Identifier == "mount");

    public static void SetupUnavailableDiagram(BunitContext context) =>
        context.Services.AddSingleton<IJSRuntime>(new UnavailableDiagramRuntime(context.JSInterop.JSRuntime));

    private sealed class UnavailableDiagramRuntime(IJSRuntime inner) : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) =>
            InvokeAsync<T>(identifier, CancellationToken.None, args);

        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            identifier == "import" && args is ["./graph-diagram.mjs"]
                ? ValueTask.FromResult(default(T)!)
                : inner.InvokeAsync<T>(identifier, cancellationToken, args);
    }

    public static BunitContext Create(HttpMessageHandler handler, IReadOnlyDictionary<string, string>? evidenceRepositories = null)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(_ => new WorkspaceClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://workspace.test")
        }));
        context.Services.AddScoped<DraftState>();
        context.Services.AddSingleton(new GraphEvidenceRepositories(evidenceRepositories ?? new Dictionary<string, string>()));
        return context;
    }
}
