namespace WasmBridge.Sdk.Tests.TestHelpers;

/// <summary>
/// Central place for where tests write generated scratch output - next to the test runner's
/// own binaries (<see cref="AppContext.BaseDirectory"/>) rather than the OS temp folder, so
/// generated files are easy to find/inspect after a test run. Each caller gets its own
/// GUID-named subfolder to avoid collisions between parallel test classes; callers are
/// responsible for deleting their own subfolder once done.
/// </summary>
public static class TestPaths
{
    public static readonly string Root = Path.Combine(AppContext.BaseDirectory, "TestOutput");

    /// <summary>Creates and returns a fresh, uniquely-named subfolder under <see cref="Root"/>.</summary>
    public static string CreateScratchDirectory()
    {
        string dir = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
