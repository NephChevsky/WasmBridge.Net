using System;

namespace WasmBridge.Attributes;

/// <summary>
/// Marks a class as the source for an automatically generated WebAssembly JS-interop
/// bridge. A companion source generator emits a static class that exposes every method
/// annotated with <see cref="WasmBridgeExportAttribute"/> as a <c>[JSExport]</c> entry point.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class WasmBridgeAttribute : Attribute
{
    /// <summary>
    /// Optional name for the generated bridge class. Defaults to "{ClassName}Bridge".
    /// </summary>
    public string? ClassName { get; set; }
}
