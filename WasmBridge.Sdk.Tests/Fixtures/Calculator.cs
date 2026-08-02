using WasmBridge.Attributes;

namespace WasmBridge.Sdk.Tests.Fixtures;

/// <summary>Fixture type used to exercise <c>GenerateBridgeTypeScriptTask</c> and <c>GenerateWasmBridgesTask</c>.</summary>
[WasmBridge]
public class Calculator
{
    [WasmBridgeExport]
    public double Add(double a, double b) => a + b;

    [WasmBridgeExport(Name = "Multiply")]
    public double Times(double a, double b) => a * b;

    [WasmBridgeExport]
    public static bool IsPositive(double value) => value > 0;

    [WasmBridgeExport]
    public Person Describe(string name) => new()
    {
        Name = name,
        Age = 0,
        Role = Role.Member,
        IsActive = true,
        HomeAddress = new Address { Street = "Unknown", City = "Unknown" },
        Tags = Array.Empty<string>(),
        PastAddresses = Array.Empty<Address>(),
    };

    [WasmBridgeExport]
    public static async Task<double> AddAsync(double a, double b)
    {
        await Task.Yield();
        return a + b;
    }

    [WasmBridgeExport]
    public async Task<Person> DescribeAsync(string name)
    {
        await Task.Yield();
        return Describe(name);
    }

    [WasmBridgeExport]
    public static async Task WaitAsync()
    {
        await Task.Yield();
    }

    // Not annotated with [WasmBridgeExport] - should never show up on the generated bridge.
    public void Ignored()
    {
    }
}
