using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace WasmBridge.Sdk.Tests.TestHelpers;

/// <summary>
/// Gathers the set of assembly paths a WasmBridge.Sdk task needs to see in order to reflect
/// over the test fixture types: the currently running test assembly (which contains every
/// <c>[WasmBridgeTsInterface]</c>/<c>[WasmBridge]</c>-annotated fixture type), the
/// <c>WasmBridge.Attributes</c> assembly, and every "trusted platform assembly" (the BCL/
/// runtime assemblies .NET Core resolves core types from) so <see cref="System.Reflection.MetadataLoadContext"/>
/// can fully resolve the fixture types' base types/interfaces. This mirrors what MSBuild's
/// <c>@(ReferencePath)</c> item normally supplies to these tasks in a real build.
/// </summary>
public static class TestAssemblies
{
    public static ITaskItem[] AsTaskItems() => GetAssemblyPaths().Select(p => (ITaskItem)new TaskItem(p)).ToArray();

    public static List<string> GetAssemblyPaths()
    {
        var paths = new List<string>();

        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedPlatformAssemblies)
        {
            paths.AddRange(trustedPlatformAssemblies.Split(Path.PathSeparator));
        }

        paths.Add(typeof(WasmBridge.Attributes.WasmBridgeTsInterfaceAttribute).Assembly.Location);
        paths.Add(typeof(TestAssemblies).Assembly.Location);

        return paths
            .Where(p => !string.IsNullOrEmpty(p) && File.Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
