# wasmbridge-net

[![npm](https://img.shields.io/npm/v/wasmbridge-net)](https://www.npmjs.com/package/wasmbridge-net)

CLI companion to [WasmBridge.Net](../../README.md) that builds (Debug) or publishes (Release) a
`WasmBridge.Net`-enabled `Microsoft.NET.Sdk.WebAssembly` project and syncs its output into a
front-end project:

1. The compiled app's `wwwroot` -> a static assets directory (e.g. Vite's `public/wasm-app`).
2. The generated TypeScript interfaces (from `WasmBridge.Net.Sdk`'s `GenerateTypeScriptTypesTask`)
   -> your source tree (e.g. `src/wasm-interfaces`).

It replaces a hand-written `scripts/sync-wasm.mjs` per front-end - the target framework is
auto-detected from the `.csproj`, so it doesn't need updating when you bump `TargetFramework`.

## Install

```
npm install -D wasmbridge-net
```

### Local / pre-publish testing

Before the package is published, or to test unreleased changes, reference it directly from a
checkout via a `file:` dependency instead:

```jsonc
// front-end/package.json
{
  "devDependencies": {
    "wasmbridge-net": "file:../../WasmBridge.Net/wasmbridge-net"
  }
}
```

```
npm install
```

## Configure

Run `init` from your front-end project (next to its `package.json`) to scaffold everything below
in one go:

```
npx wasmbridge-net init
```

It auto-detects the WebAssembly project by looking for a single `*.csproj` with
`Sdk="Microsoft.NET.Sdk.WebAssembly"` in sibling directories, writes `wasmbridge.config.json`, and
adds the `sync:wasm*`/`predev`/`prebuild` scripts + the `wasmbridge-net` devDependency to
`package.json` (existing scripts/dependency entries are left untouched). Pass `--project <path>` if
auto-detection can't find (or picks the wrong) project, and `--force` to overwrite an existing
config. Run `npm install` afterwards to pick up the new devDependency.

Or add `wasmbridge.config.json` by hand next to your front-end's `package.json`:

```json
{
  "wasmProject": "../GameEngine.Wasm/GameEngine.Wasm.csproj",
  "publicDir": "public/wasm-app",
  "srcDir": "src/wasm-interfaces"
}
```

All paths are resolved relative to the config file's directory. `publicDir`/`srcDir` default to
`public/wasm-app`/`src/wasm-interfaces` if omitted.

## Use

```jsonc
// front-end/package.json
{
  "scripts": {
    "sync:wasm": "wasmbridge-net sync --release",
    "sync:wasm:debug": "wasmbridge-net sync --debug",
    "predev": "npm run sync:wasm:debug",
    "prebuild": "npm run sync:wasm"
  }
}
```

CLI flags (`--project`, `--public-dir`, `--src-dir`, `--config <path>`) override the config file
if you need a one-off different value.

## Programmatic API

```js
import { syncWasm } from 'wasmbridge-net';

syncWasm({
  cwd: import.meta.dirname, // base dir for relative paths, default process.cwd()
  wasmProject: '../GameEngine.Wasm/GameEngine.Wasm.csproj',
  configuration: 'Debug', // or 'Release'
  publicDir: 'public/wasm-app',
  srcDir: 'src/wasm-interfaces',
});
```

This is the same function the CLI calls, kept separate so future tooling (a watch mode, a Vite
plugin, etc.) can reuse it without shelling back out to the CLI.

## Development

```
npm ci
npm test      # node:test - config loading + input validation
```
