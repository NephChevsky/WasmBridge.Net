using WasmBridge.Sdk.Tests.TestHelpers;
using Xunit;

namespace WasmBridge.Sdk.Tests;

/// <summary>
/// Covers <see cref="GenerateWasmBridgesTask"/>: the generated <c>[JSExport]</c> bridge class
/// for <c>Fixtures/Calculator.cs</c> exposes exactly its <c>[WasmBridgeExport]</c> methods
/// under their configured names, serializes <c>[WasmBridgeTsInterface]</c>-rooted return types
/// to JSON via the generated <c>WasmBridgeJsonContext</c>, and is syntactically valid C#.
/// </summary>
public sealed class GenerateWasmBridgesTaskTests : TaskTestBase
{
    private readonly string _content;

    public GenerateWasmBridgesTaskTests()
    {
        var task = RunTask(() => new GenerateWasmBridgesTask
        {
            Assemblies = TestAssemblies.AsTaskItems(),
            OutputPath = OutputDir,
        });

        bool succeeded = task.Execute();
        Assert.True(succeeded, "Task.Execute() reported failure:\n" + string.Join("\n", BuildEngine.Errors));
        Assert.Empty(BuildEngine.Errors);

        string filePath = GetGeneratedFile(task.GeneratedFiles, "CalculatorBridge.g.cs");
        _content = File.ReadAllText(filePath);
    }

    [Fact]
    public void Execute_GeneratesJSExportMethodsForExportedMembersOnly()
    {
        Assert.Contains("internal static partial class CalculatorBridge", _content);
        Assert.Contains("[JSExport]", _content);
        Assert.Contains("internal static double Add(double a, double b) => _target.Add(a, b);", _content);
        Assert.Contains("internal static double Multiply(double a, double b) => _target.Times(a, b);", _content);
        Assert.Contains("internal static bool IsPositive(double value)", _content);
        Assert.DoesNotContain(" Ignored(", _content);
    }

    [Fact]
    public void Execute_JsonSerializesTsInterfaceRootedReturnType()
    {
        Assert.Contains(
            "internal static string Describe(string name) => global::System.Text.Json.JsonSerializer.Serialize(_target.Describe(name), WasmBridgeJsonContext.Default.Person);",
            _content);
    }

    [Fact]
    public void Execute_PassesThroughTaskOfPrimitiveDirectly()
    {
        Assert.Contains(
            "internal static global::System.Threading.Tasks.Task<double> AddAsync(double a, double b) => global::WasmBridge.Sdk.Tests.Fixtures.Calculator.AddAsync(a, b);",
            _content);
    }

    [Fact]
    public void Execute_AwaitsAndJsonSerializesTaskOfTsInterfaceRootedReturnType()
    {
        Assert.Contains("internal static async global::System.Threading.Tasks.Task<string> DescribeAsync(string name)", _content);
        Assert.Contains("var result = await _target.DescribeAsync(name);", _content);
        Assert.Contains("return global::System.Text.Json.JsonSerializer.Serialize(result, WasmBridgeJsonContext.Default.Person);", _content);
    }

    [Fact]
    public void Execute_AwaitsPlainTaskWithNoReturnValue()
    {
        Assert.Contains(
            "internal static async global::System.Threading.Tasks.Task WaitAsync() => await global::WasmBridge.Sdk.Tests.Fixtures.Calculator.WaitAsync();",
            _content);
    }

    [Fact]
    public void Execute_GeneratedFile_IsSyntacticallyValidCSharp()
    {
        CSharpValidator.AssertParsesWithoutSyntaxErrors(_content);
    }
}
