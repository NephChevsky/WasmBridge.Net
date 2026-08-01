using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using WasmBridge.Sdk.Tests.TestHelpers;
using Xunit;

namespace WasmBridge.Sdk.Tests;

/// <summary>
/// Shared plumbing for running a WasmBridge.Sdk <see cref="Microsoft.Build.Utilities.Task"/>
/// against this test project's own compiled assembly (which contains every fixture type under
/// <c>Fixtures/</c>) - mirroring how MSBuild invokes these tasks against
/// <c>@(ReferencePath)</c> in a real build, and cleaning up its scratch output directory
/// (see <see cref="TestPaths"/>) afterwards.
/// </summary>
public abstract class TaskTestBase : IDisposable
{
    protected readonly string OutputDir = TestPaths.CreateScratchDirectory();
    protected readonly FakeBuildEngine BuildEngine = new();

    protected TTask RunTask<TTask>(Func<TTask> factory) where TTask : Microsoft.Build.Utilities.Task
    {
        TTask task = factory();
        task.BuildEngine = BuildEngine;
        return task;
    }

    public void Dispose()
    {
        if (Directory.Exists(OutputDir))
        {
            Directory.Delete(OutputDir, recursive: true);
        }
    }

    protected static string GetGeneratedFile(ITaskItem[] generatedFiles, string fileName) =>
        Assert.Single(generatedFiles.Select(f => f.ItemSpec), p => Path.GetFileName(p).Equals(fileName, StringComparison.OrdinalIgnoreCase));
}
