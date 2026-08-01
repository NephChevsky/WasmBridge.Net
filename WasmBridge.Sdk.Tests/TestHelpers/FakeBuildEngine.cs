using System.Collections;
using Microsoft.Build.Framework;

namespace WasmBridge.Sdk.Tests.TestHelpers;

/// <summary>
/// Minimal <see cref="IBuildEngine"/> implementation that records logged errors/warnings/
/// messages so tests can assert on <c>Log.LogError(...)</c> calls without needing a real
/// MSBuild host.
/// </summary>
public sealed class FakeBuildEngine : IBuildEngine
{
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> Messages { get; } = new();

    public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e.Message ?? string.Empty);

    public void LogWarningEvent(BuildWarningEventArgs e) => Warnings.Add(e.Message ?? string.Empty);

    public void LogMessageEvent(BuildMessageEventArgs e) => Messages.Add(e.Message ?? string.Empty);

    public void LogCustomEvent(CustomBuildEventArgs e)
    {
    }

    public bool ContinueOnError => false;
    public int LineNumberOfTaskNode => 0;
    public int ColumnNumberOfTaskNode => 0;
    public string ProjectFileOfTaskNode => string.Empty;

    public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) =>
        throw new NotSupportedException();
}
