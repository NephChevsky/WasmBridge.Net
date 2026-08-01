using WasmBridge.Attributes;

namespace WasmBridge.Sdk.Tests.Fixtures;

/// <summary>Fixture types used to exercise <c>GenerateTypeScriptTypesTask</c>'s full feature set:
/// primitives, enums, nested/non-root reachable types, nullable references, lists, dictionaries,
/// inheritance, and the "root type extends Dictionary&lt;string, T&gt;" type-alias shape.</summary>

public enum Role
{
    Member = 0,
    Admin = 1,
}

/// <summary>A non-root type, only reachable through <see cref="Person"/> - should be inlined as its own interface, not given its own file.</summary>
public sealed class Address
{
    public required string Street { get; set; }
    public required string City { get; set; }
    public string? Country { get; set; }
}

[WasmBridgeTsInterface]
public class Person
{
    public required string Name { get; set; }
    public int Age { get; set; }
    public Role Role { get; set; }
    public bool IsActive { get; set; }
    public DateTime BirthDate { get; set; }
    public required Address HomeAddress { get; set; }
    public Address? WorkAddress { get; set; }
    public required IReadOnlyList<string> Tags { get; set; }
    public required IReadOnlyList<Address> PastAddresses { get; set; }
}

/// <summary>Covers inheritance: generated <c>Employee</c> interface should <c>extends Person</c>.</summary>
[WasmBridgeTsInterface]
public class Employee : Person
{
    public double Salary { get; set; }
}

/// <summary>Covers the string-keyed-dictionary root shape: generates a <c>type</c> alias instead of an <c>interface</c>.</summary>
[WasmBridgeTsInterface]
public class ScoreBoard : Dictionary<string, int>
{
}
