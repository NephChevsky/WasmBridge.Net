# wasmbridge-net

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

Add `wasmbridge.config.json` next to your front-end's `package.json`:

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

## Publishing

Handled by the `publish-npm.yml` GitHub Actions workflow (manual `workflow_dispatch` trigger) in
the parent repo, using npm's [trusted publishing](https://docs.npmjs.com/trusted-publishers)
(OIDC) - no `NPM_TOKEN` secret required. One-time setup on npmjs.com: configure this package with
a trusted publisher pointing at this GitHub repo, workflow file (`publish-npm.yml`), and the
`production` environment. To release:

1. Bump `version` in `package.json` (npm versions are immutable once published).
2. Run the `Publish wasmbridge-net to npm` workflow from the Actions tab.

