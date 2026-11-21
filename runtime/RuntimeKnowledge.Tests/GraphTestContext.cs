using Bunit;
using Microsoft.Extensions.DependencyInjection;
using RuntimeKnowledge.Web.Services;

namespace RuntimeKnowledge.Tests;

internal static class GraphTestContext
{
    public static BunitContext Create(HttpMessageHandler handler)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(_ => new WorkspaceClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://workspace.test")
        }));
        context.Services.AddScoped<DraftState>();
        return context;
    }
}
