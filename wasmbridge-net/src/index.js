// Builds (Debug) or publishes (Release) a WasmBridge.Net-enabled Microsoft.NET.Sdk.WebAssembly
// project and copies its output into a front-end project:
//   1. The compiled app's `wwwroot` -> `publicDir` (served as static assets, e.g. Vite's `public/`).
//   2. The generated TypeScript interfaces (`GenerateTypeScriptTypesTask` output) -> `srcDir`.
import { spawnSync } from 'node:child_process';
import { existsSync, rmSync, mkdirSync, cpSync, readFileSync } from 'node:fs';
import path from 'node:path';

/**
 * @typedef {Object} WasmBridgeSyncOptions
 * @property {string} wasmProject Path to the `Microsoft.NET.Sdk.WebAssembly` project's `.csproj`
 *   (resolved against `cwd`).
 * @property {string} [publicDir] Where the compiled app's `wwwroot` is copied to. Resolved
 *   against `cwd`. Default: `"public/wasm-app"`.
 * @property {string} [srcDir] Where the generated TypeScript interfaces are copied to. Resolved
 *   against `cwd`. Default: `"src/wasm-interfaces"`.
 * @property {'Debug' | 'Release'} [configuration] Build configuration. Default: `"Release"`.
 * @property {string} [cwd] Base directory relative paths are resolved against.
 *   Default: `process.cwd()`.
 */

/**
 * @param {WasmBridgeSyncOptions} options
 */
export function syncWasm(options) {
  const cwd = options.cwd ?? process.cwd();
  const configuration = options.configuration === 'Debug' ? 'Debug' : 'Release';
  const isDebug = configuration === 'Debug';

  if (!options.wasmProject) {
    throw new Error('wasmbridge-net: "wasmProject" is required (path to the .csproj).');
  }

  const wasmProjectPath = path.resolve(cwd, options.wasmProject);
  if (!existsSync(wasmProjectPath)) {
    throw new Error(`wasmbridge-net: wasm project not found at ${wasmProjectPath}`);
  }

  const wasmProjectDir = path.dirname(wasmProjectPath);
  const projectName = path.basename(wasmProjectPath, path.extname(wasmProjectPath));
  const targetFramework = getTargetFramework(wasmProjectPath);

  const publicDir = path.resolve(cwd, options.publicDir ?? 'public/wasm-app');
  const srcDir = path.resolve(cwd, options.srcDir ?? 'src/wasm-interfaces');

  const outputDir = isDebug
    ? path.join(wasmProjectDir, 'bin', 'Debug', targetFramework)
    : path.join(wasmProjectDir, 'bin', 'publish-wasm-release');

  const command = isDebug ? 'build' : 'publish';
  const args = [command, wasmProjectPath, '-c', configuration];
  if (!isDebug) {
    args.push('-o', outputDir);
  }

  console.log(`[wasmbridge-net] ${isDebug ? 'Building' : 'Publishing'} ${projectName} (${configuration})...`);
  if (!isDebug) {
    rmSync(outputDir, { recursive: true, force: true });
  }

  const result = spawnSync('dotnet', args, { stdio: 'inherit' });
  if (result.status !== 0) {
    throw new Error(`wasmbridge-net: "dotnet ${command}" failed (exit code ${result.status ?? 'unknown'}).`);
  }

  const sourceWwwroot = path.join(outputDir, 'wwwroot');
  if (!existsSync(sourceWwwroot)) {
    throw new Error(`wasmbridge-net: expected ${command} output not found at ${sourceWwwroot}`);
  }

  console.log(`[wasmbridge-net] Copying ${sourceWwwroot} -> ${publicDir}`);
  rmSync(publicDir, { recursive: true, force: true });
  mkdirSync(publicDir, { recursive: true });
  cpSync(sourceWwwroot, publicDir, { recursive: true });

  // GenerateTypeScriptTypesTask writes generated .ts files into the build's own intermediate
  // output directory (obj/<configuration>/<tfm>/wasm-interfaces), regardless of build vs. publish.
  const tsSourceDir = path.join(wasmProjectDir, 'obj', configuration, targetFramework, 'wasm-interfaces');
  if (!existsSync(tsSourceDir)) {
    throw new Error(`wasmbridge-net: expected generated TypeScript output not found at ${tsSourceDir}`);
  }

  console.log(`[wasmbridge-net] Copying ${tsSourceDir} -> ${srcDir}`);
  rmSync(srcDir, { recursive: true, force: true });
  mkdirSync(srcDir, { recursive: true });
  cpSync(tsSourceDir, srcDir, { recursive: true });

  if (isDebug) {
    // `dotnet build` (unlike `publish`) doesn't lay out a self-contained wwwroot with every
    // static web asset physically copied in - some assets (e.g. dotnet.js, ICU data) only exist
    // via the static web assets manifest. Walk it to copy anything wwwroot doesn't already have.
    const manifestPath = path.join(outputDir, `${projectName}.staticwebassets.runtime.json`);
    if (!existsSync(manifestPath)) {
      throw new Error(`wasmbridge-net: expected static web assets manifest not found at ${manifestPath}`);
    }

    const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'));
    const copyAssets = (node, relativePath = '') => {
      if (node.Asset) {
        const source = path.join(
          manifest.ContentRoots[node.Asset.ContentRootIndex],
          node.Asset.SubPath,
        );
        const destination = path.join(publicDir, relativePath);
        mkdirSync(path.dirname(destination), { recursive: true });
        cpSync(source, destination);
      }

      for (const [name, child] of Object.entries(node.Children ?? {})) {
        copyAssets(child, path.join(relativePath, name));
      }
    };

    copyAssets(manifest.Root);
  }

  console.log(`[wasmbridge-net] WASM assets synced (${configuration}).`);
}

/**
 * Reads `<TargetFramework>` out of a `.csproj` (e.g. `"net9.0"`). Exported for reuse by other
 * commands (e.g. the `debug`/`init --vscode` VS Code config generator) that need to compute a
 * build output path (`bin/<Configuration>/<TargetFramework>`).
 * @param {string} csprojPath
 */
export function getTargetFramework(csprojPath) {
  const content = readFileSync(csprojPath, 'utf8');
  const match = content.match(/<TargetFramework>\s*([^<\s]+)\s*<\/TargetFramework>/);
  if (!match) {
    throw new Error(`wasmbridge-net: could not find <TargetFramework> in ${csprojPath}`);
  }
  return match[1];
}
