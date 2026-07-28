# WasmBridge.Net

MSBuild tooling that generates WebAssembly JS-interop code for .NET projects (`Microsoft.NET.Sdk.WebAssembly` / Blazor WASM) from plain attributes, so you don't hand-write `[JSExport]` bridge classes or keep a parallel set of TypeScript types in sync by hand.

## Objective

When you publish a .NET class library to WebAssembly, you need:

1. A **JS-interop bridge**: static methods marked `[JSExport]` (from `System.Runtime.InteropServices.JavaScript`) that the front-end can actually call into from JavaScript/TypeScript.
2. **Matching TypeScript types** on the front-end for whatever data those methods pass back and forth, kept in sync with the C# types.

Both are tedious and error-prone to maintain by hand as the C# API evolves. WasmBridge.Net solves this by letting you annotate your existing C# types/methods with lightweight marker attributes, and generating both the bridge class and the TypeScript types automatically as part of the build.

### Why an MSBuild task instead of a Roslyn source generator?

`[JSExport]` is processed by the compiler's built-in `Microsoft.Interop.JavaScript.JSExportGenerator`. Roslyn source generators cannot see source produced by *another* generator in the same compilation, so a generator that emits `[JSExport]` methods would silently produce no interop code. WasmBridge.Net works around this by running as an **MSBuild `Task`** that writes real `.cs`/`.ts` files to disk *before* `CoreCompile`, so by the time the compiler's `JSExportGenerator` runs, the generated bridge is ordinary source.

## Packages

| Package | What it is | Referenced from |
|---|---|---|
| **WasmBridge.Net.Attributes** | Tiny, dependency-free marker-attribute library (`[WasmBridge]`, `[WasmBridgeExport]`, `[WasmBridgeTsInterface]`). | The class library containing your logic, and anywhere else that needs to declare bridge/TS-root types. |
| **WasmBridge.Net.Sdk** | MSBuild `Task`s that scan already-built reference assemblies for those attributes and generate the bridge `.cs` file(s) and TypeScript `.ts` file(s), plus a `.props` file with common WASM project settings (see below). Auto-imports itself via NuGet - no manual `<Import>` needed. | The `Microsoft.NET.Sdk.WebAssembly` project that actually gets published as WASM. |
| **[wasmbridge-net](wasmbridge-net)** (npm) | CLI that builds/publishes the WASM project and syncs its output (compiled app + generated TypeScript types) into a front-end project. | Your front-end project (e.g. a Vite/React app). |

## Setup

### 1. Reference the attributes from your class library

```xml
<!-- YourLib.csproj -->
<ItemGroup>
  <PackageReference Include="WasmBridge.Net.Attributes" Version="x.y.z" />
</ItemGroup>
```

### 2. Reference the SDK from your WASM project

```xml
<!-- YourApp.Wasm.csproj (Sdk="Microsoft.NET.Sdk.WebAssembly") -->
<ItemGroup>
  <ProjectReference Include="..\YourLib\YourLib.csproj" />
  <PackageReference Include="WasmBridge.Net.Sdk" Version="x.y.z" />
</ItemGroup>
```

That's it - no manual `<Import>`, no extra `<Target>` wiring. Restoring the WASM project pulls in `build\WasmBridge.Net.Sdk.props`/`.targets` automatically, which:

- Sets `AllowUnsafeBlocks`, disables static web asset/WASM fingerprinting (so front-ends that reference `dotnet.js` etc. by a fixed path keep working), and enables debug-symbol settings (with `PublishTrimmed=false`) in Debug builds.
- Runs the two generator tasks before `CoreCompile` on every build.

### 3. Annotate your C# types

```csharp
using WasmBridge.Attributes;

[WasmBridge] // generates a "CalculatorBridge" static class with [JSExport] methods
public class Calculator
{
    [WasmBridgeExport] // exposed on the bridge as "Add"
    public double Add(double a, double b) => a + b;
}

[WasmBridgeTsInterface] // generates result.ts: interface Result + parseResult()
public sealed class Result
{
    public double Value { get; init; }
    // ...
}
```

Building the WASM project now generates:

- `$(IntermediateOutputPath)WasmBridge\*.cs` - the `[JSExport]` bridge class(es), compiled straight into the WASM assembly.
- `$(IntermediateOutputPath)wasm-interfaces\*.ts` (or wherever `$(WasmBridgeTypeScriptOutputPath)` points) - TypeScript interfaces + `parseX` helpers for every `[WasmBridgeTsInterface]`-rooted type and its reachable type graph.

Both paths are configurable via `$(WasmBridgeGeneratedOutputPath)` / `$(WasmBridgeTypeScriptOutputPath)` if you need the front-end build to pick the files up from somewhere else.

## Repo layout

```
WasmBridge.Attributes/   # WasmBridge.Net.Attributes package source
WasmBridge.Sdk/          # WasmBridge.Net.Sdk package source (tasks + build\*.props/*.targets)
wasmbridge-net/          # wasmbridge-net npm package source (see its own README)
WasmBridge.Net.slnx
pack.ps1                 # packs both .NET projects into ./artifacts (a local NuGet feed)
```

## Local development / testing

NuGet package versions are immutable once pushed to nuget.org, so test changes locally first:

1. Bump `<Version>` in the relevant `.csproj` (`WasmBridge.Attributes/WasmBridge.Attributes.csproj` or `WasmBridge.Sdk/WasmBridge.Sdk.csproj`).
2. Run `.\pack.ps1` (or `dotnet pack <project> -c Release -o artifacts` directly) to produce a `.nupkg` in `./artifacts`.
3. In the consuming repo, add a `NuGet.Config` with a package source pointing at this repo's `artifacts` folder (alongside `nuget.org`), and bump the consumer's `PackageReference` version to match. Run `dotnet restore --force`.

`wasmbridge-net` (npm) has its own local-testing/publishing instructions in [wasmbridge-net/README.md](wasmbridge-net/README.md).
