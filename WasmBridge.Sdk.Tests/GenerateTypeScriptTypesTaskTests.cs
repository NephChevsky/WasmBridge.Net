using System.Text.Json;
using System.Text.Json.Nodes;
using WasmBridge.Sdk.Tests.Fixtures;
using WasmBridge.Sdk.Tests.TestHelpers;
using Xunit;

namespace WasmBridge.Sdk.Tests;

/// <summary>
/// Covers <see cref="GenerateTypeScriptTypesTask"/>: that the <c>.ts</c> files it generates
/// for <c>Fixtures/Person.cs</c>'s <c>[WasmBridgeTsInterface]</c> types (1) are actually valid
/// TypeScript per the real compiler, and (2) round-trip a JSON payload produced by C#'s
/// <c>System.Text.Json</c> through the generated <c>parseX</c> function without any data loss
/// or shape mismatch.
/// </summary>
public sealed class GenerateTypeScriptTypesTaskTests : TaskTestBase
{
    private readonly GenerateTypeScriptTypesTask _task;

    public GenerateTypeScriptTypesTaskTests()
    {
        _task = RunTask(() => new GenerateTypeScriptTypesTask
        {
            Assemblies = TestAssemblies.AsTaskItems(),
            OutputPath = OutputDir,
        });

        bool succeeded = _task.Execute();
        Assert.True(succeeded, "Task.Execute() reported failure:\n" + string.Join("\n", BuildEngine.Errors));
        Assert.Empty(BuildEngine.Errors);
    }

    [Fact]
    public void Execute_GeneratesOneFilePerRootType()
    {
        var fileNames = _task.GeneratedFiles.Select(f => Path.GetFileName(f.ItemSpec)).ToList();

        Assert.Contains("person.ts", fileNames);
        Assert.Contains("employee.ts", fileNames);
        Assert.Contains("scoreBoard.ts", fileNames);
    }

    [Fact]
    public void Execute_PersonInterface_HasExpectedShape()
    {
        string content = File.ReadAllText(GetGeneratedFile(_task.GeneratedFiles, "person.ts"));

        Assert.Contains("export interface Person {", content);
        Assert.Contains("readonly Name: string", content);
        Assert.Contains("readonly Age: number", content);
        Assert.Contains("readonly Role: number", content);
        Assert.Contains("readonly IsActive: boolean", content);
        Assert.Contains("readonly BirthDate: string", content);
        Assert.Contains("readonly HomeAddress: Address", content);
        Assert.Contains("readonly WorkAddress: Address | null", content);
        Assert.Contains("readonly Tags: readonly string[]", content);
        Assert.Contains("readonly PastAddresses: readonly Address[]", content);
        Assert.Contains("export interface Address {", content);
        Assert.Contains("readonly Country: string | null", content);
        Assert.Contains("export function parsePerson(json: string): Person {", content);
    }

    [Fact]
    public void Execute_EmployeeInterface_ExtendsPerson()
    {
        string content = File.ReadAllText(GetGeneratedFile(_task.GeneratedFiles, "employee.ts"));

        Assert.Contains("export interface Employee extends Person {", content);
        Assert.Contains("readonly Salary: number", content);
    }

    [Fact]
    public void Execute_ScoreBoardRoot_GeneratesTypeAliasNotInterface()
    {
        string content = File.ReadAllText(GetGeneratedFile(_task.GeneratedFiles, "scoreBoard.ts"));

        Assert.Contains("export type ScoreBoard = Readonly<Record<string, number>>", content);
        Assert.DoesNotContain("interface ScoreBoard", content);
    }

    [Theory]
    [InlineData("person.ts")]
    [InlineData("employee.ts")]
    [InlineData("scoreBoard.ts")]
    public void Execute_GeneratedFile_IsValidTypeScript(string fileName)
    {
        string filePath = GetGeneratedFile(_task.GeneratedFiles, fileName);

        TypeScriptValidator.AssertValidTypeScript(filePath);
    }

    [Fact]
    public void ParsePerson_RoundTripsJsonSerializedInstance()
    {
        var person = new Person
        {
            Name = "Ada Lovelace",
            Age = 36,
            Role = Role.Admin,
            IsActive = true,
            BirthDate = new DateTime(1815, 12, 10, 0, 0, 0, DateTimeKind.Utc),
            HomeAddress = new Address { Street = "1 Analytical Engine Way", City = "London", Country = null },
            WorkAddress = null,
            Tags = new[] { "mathematician", "programmer" },
            PastAddresses = new[]
            {
                new Address { Street = "12 Old Rd", City = "Shelbyville", Country = "England" },
            },
        };

        string json = JsonSerializer.Serialize(person);
        string filePath = GetGeneratedFile(_task.GeneratedFiles, "person.ts");

        AssertRoundTrips(filePath, "parsePerson", json);
    }

    [Fact]
    public void ParseEmployee_RoundTripsJsonSerializedInstance_IncludingInheritedMembers()
    {
        var employee = new Employee
        {
            Name = "Grace Hopper",
            Age = 45,
            Role = Role.Member,
            IsActive = false,
            BirthDate = new DateTime(1906, 12, 9, 0, 0, 0, DateTimeKind.Utc),
            HomeAddress = new Address { Street = "1 Compiler Ave", City = "Arlington", Country = "USA" },
            WorkAddress = new Address { Street = "2 Navy Yard", City = "Washington", Country = "USA" },
            Tags = new[] { "rear admiral" },
            PastAddresses = Array.Empty<Address>(),
            Salary = 123456.78,
        };

        string json = JsonSerializer.Serialize(employee);
        string filePath = GetGeneratedFile(_task.GeneratedFiles, "employee.ts");

        AssertRoundTrips(filePath, "parseEmployee", json);
    }

    [Fact]
    public void ParseScoreBoard_RoundTripsDictionaryShapedInstance()
    {
        var scoreBoard = new ScoreBoard
        {
            ["alice"] = 10,
            ["bob"] = 20,
        };

        string json = JsonSerializer.Serialize(scoreBoard);
        string filePath = GetGeneratedFile(_task.GeneratedFiles, "scoreBoard.ts");

        AssertRoundTrips(filePath, "parseScoreBoard", json);
    }

    /// <summary>
    /// Runs <paramref name="parseFunctionName"/> from the compiled <paramref name="tsFilePath"/>
    /// against <paramref name="json"/> (as C#'s <c>System.Text.Json</c> produced it) and asserts
    /// the JS object it returns is deeply equal to the original payload - i.e. the generated
    /// TypeScript types and parser round-trip the exact same data C# serialized, with no key,
    /// casing, or value-shape mismatches.
    /// </summary>
    private static void AssertRoundTrips(string tsFilePath, string parseFunctionName, string json)
    {
        string resultJson = TypeScriptValidator.RunExportedFunction(tsFilePath, parseFunctionName, json);

        JsonNode? expected = JsonNode.Parse(json);
        JsonNode? actual = JsonNode.Parse(resultJson);

        Assert.True(
            JsonNode.DeepEquals(expected, actual),
            $"Round-trip mismatch.\nOriginal (from C#):  {json}\nParsed (via {parseFunctionName}): {resultJson}");
    }
}
