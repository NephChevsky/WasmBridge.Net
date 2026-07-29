#nullable enable
// Shipped as source (not compiled into WasmBridge.Net.Sdk itself - see
// <Compile Remove="src\**\*.cs" /> in WasmBridge.Sdk.csproj) and added to the consuming
// browser-wasm project's own compilation by build\WasmBridge.Net.Sdk.targets, so it compiles
// directly into that project's assembly (required for [JSImport] - see JSImportGenerator).
//
// JSImport bindings for the browser's `localStorage` global. Set
// $(WasmBridgeIncludeLocalStorageInterop) to false in the consuming project to opt out.
using System.Runtime.InteropServices.JavaScript;

internal static partial class LocalStorageInterop
{
    [JSImport("globalThis.localStorage.setItem")]
    internal static partial void SetItem(string key, string value);

    [JSImport("globalThis.localStorage.getItem")]
    internal static partial string? GetItem(string key);

    [JSImport("globalThis.localStorage.removeItem")]
    internal static partial void RemoveItem(string key);
}
