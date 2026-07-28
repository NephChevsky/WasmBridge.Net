using System;

namespace WasmBridge.Attributes;

/// <summary>
/// Marks a method to be exposed on the generated WebAssembly JS-interop bridge for the
/// containing class (see <see cref="WasmBridgeAttribute"/>). Works for both instance and
/// static methods.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class WasmBridgeExportAttribute : Attribute
{
    /// <summary>
    /// Optional name to expose the method as on the generated bridge. Defaults to the
    /// method's own name.
    /// </summary>
    public string? Name { get; set; }
}
