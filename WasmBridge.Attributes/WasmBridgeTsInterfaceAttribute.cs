using System;

namespace WasmBridge.Attributes;

/// <summary>
/// Marks a type as a root for TypeScript generation: <c>WasmBridge.Tasks</c> generates a
/// <c>.ts</c> file for this type (interfaces for its whole reachable type graph, plus a
/// <c>parseX</c> helper). Types without this attribute are only inlined into whichever root's
/// file reaches them. The generated TypeScript name always matches the C# type name.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum, Inherited = false, AllowMultiple = false)]
public sealed class WasmBridgeTsInterfaceAttribute : Attribute
{
}
