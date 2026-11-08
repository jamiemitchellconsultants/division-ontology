using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using RuntimeKnowledge.Web;
using RuntimeKnowledge.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.Services.AddOidcAuthentication(options =>
{
    builder.Configuration.GetSection("Identity").Bind(options.ProviderOptions);
    options.ProviderOptions.ResponseType = "code";
    if (builder.Configuration["Identity:ApiScope"] is { Length: > 0 } apiScope)
        options.ProviderOptions.DefaultScopes.Add(apiScope);
});
builder.Services.AddScoped(services =>
{
    var handler = services.GetRequiredService<BaseAddressAuthorizationMessageHandler>();
    handler.InnerHandler = new HttpClientHandler();
    return new WorkspaceClient(new HttpClient(handler) { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
});
builder.Services.AddScoped<DraftState>();
await builder.Build().RunAsync();
