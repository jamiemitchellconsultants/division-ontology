using System.Reflection;

namespace RuntimeKnowledge.Api;

/// <summary>True while Microsoft.Extensions.ApiDescription.Server runs the app to write the committed spec.</summary>
internal static class BuildTimeGeneration
{
    public static bool IsActive { get; } = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
}
