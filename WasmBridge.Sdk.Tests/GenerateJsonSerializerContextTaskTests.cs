using WasmBridge.Sdk.Tests.TestHelpers;
using Xunit;

namespace WasmBridge.Sdk.Tests;

/// <summary>
/// Covers <see cref="GenerateJsonSerializerContextTask"/>: it emits a
/// <c>JsonSerializerContext</c> registering every <c>[WasmBridgeTsInterface]</c> root type
/// (and a <c>List&lt;T&gt;</c> of each) found in the scanned assemblies, and the emitted file
/// is valid, parseable C#.
/// </summary>
public sealed class GenerateJsonSerializerContextTaskTests : TaskTestBase
{
    [Fact]
    public void Execute_RegistersEveryRootTypeAndListOfIt()
    {
        var task = RunTask(() => new GenerateJsonSerializerContextTask
        {
            Assemblies = TestAssemblies.AsTaskItems(),
            OutputPath = OutputDir,
        });

        bool succeeded = task.Execute();
        Assert.True(succeeded, "Task.Execute() reported failure:\n" + string.Join("\n", BuildEngine.Errors));
        Assert.Empty(BuildEngine.Errors);

        string filePath = Assert.Single(task.GeneratedFiles.Select(f => f.ItemSpec));
        string content = File.ReadAllText(filePath);

        Assert.Contains("[JsonSerializable(typeof(global::WasmBridge.Sdk.Tests.Fixtures.Person))]", content);
        Assert.Contains("[JsonSerializable(typeof(List<global::WasmBridge.Sdk.Tests.Fixtures.Person>))]", content);
        Assert.Contains("[JsonSerializable(typeof(global::WasmBridge.Sdk.Tests.Fixtures.Employee))]", content);
        Assert.Contains("[JsonSerializable(typeof(global::WasmBridge.Sdk.Tests.Fixtures.ScoreBoard))]", content);
        Assert.Contains("internal partial class WasmBridgeJsonContext : JsonSerializerContext", content);

        CSharpValidator.AssertParsesWithoutSyntaxErrors(content);
    }
}
