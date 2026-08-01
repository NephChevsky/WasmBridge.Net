using WasmBridge.Sdk.Tests.TestHelpers;
using Xunit;

namespace WasmBridge.Sdk.Tests;

/// <summary>
/// Covers <see cref="GenerateBridgeTypeScriptTask"/>: the TypeScript interface generated for
/// <c>Fixtures/Calculator.cs</c>'s <c>[WasmBridge]</c> class exposes exactly its
/// <c>[WasmBridgeExport]</c> methods, with correctly mapped parameter/return types (including
/// the "returns a [WasmBridgeTsInterface]-rooted type => JSON string" rule), and is itself
/// valid TypeScript.
/// </summary>
public sealed class GenerateBridgeTypeScriptTaskTests : TaskTestBase
{
    private readonly GenerateBridgeTypeScriptTask _task;

    public GenerateBridgeTypeScriptTaskTests()
    {
        _task = RunTask(() => new GenerateBridgeTypeScriptTask
        {
            Assemblies = TestAssemblies.AsTaskItems(),
            OutputPath = OutputDir,
        });

        bool succeeded = _task.Execute();
        Assert.True(succeeded, "Task.Execute() reported failure:\n" + string.Join("\n", BuildEngine.Errors));
        Assert.Empty(BuildEngine.Errors);
    }

    [Fact]
    public void Execute_GeneratesInterfaceWithExportedMethodsOnly()
    {
        string content = File.ReadAllText(GetGeneratedFile(_task.GeneratedFiles, "calculatorBridge.ts"));

        Assert.Contains("export interface CalculatorBridge {", content);
        Assert.Contains("Add: (a: number, b: number) => number", content);
        Assert.Contains("Multiply: (a: number, b: number) => number", content);
        Assert.Contains("IsPositive: (value: number) => boolean", content);
        // Person is a [WasmBridgeTsInterface] root, so the bridge method returns a JSON string.
        Assert.Contains("Describe: (name: string) => string", content);
        Assert.DoesNotContain("Ignored", content);
    }

    [Fact]
    public void Execute_GeneratedFile_IsValidTypeScript()
    {
        string filePath = GetGeneratedFile(_task.GeneratedFiles, "calculatorBridge.ts");

        // The loader appended after the interface references `window`/`document`.
        TypeScriptValidator.AssertValidTypeScript(filePath, includeDom: true);
    }
}
