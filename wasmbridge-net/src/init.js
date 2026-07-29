// Scaffolds a front-end project for wasmbridge-net: writes `wasmbridge.config.json` and wires up
// the `sync:wasm*`/`predev`/`prebuild` scripts + the `wasmbridge-net` devDependency in `package.json`.
import { existsSync, readFileSync, writeFileSync, readdirSync } from 'node:fs';
import path from 'node:path';

const CONFIG_FILE_NAME = 'wasmbridge.config.json';
const IGNORED_DIRS = new Set(['node_modules', 'bin', 'obj', '.git']);

const DEFAULT_SCRIPTS = {
  'sync:wasm': 'wasmbridge-net sync --release',
  'sync:wasm:debug': 'wasmbridge-net sync --debug',
  predev: 'npm run sync:wasm:debug',
  prebuild: 'npm run sync:wasm',
};

/**
 * @typedef {Object} InitOptions
 * @property {string} [cwd] Base directory the front-end's `package.json` lives in.
 *   Default: `process.cwd()`.
 * @property {string} [wasmProject] Path to the `.csproj` (resolved against `cwd`). Auto-detected
 *   from sibling directories (looking for `Sdk="Microsoft.NET.Sdk.WebAssembly"`) if omitted.
 * @property {string} [publicDir] Default: `"public/wasm-app"`.
 * @property {string} [srcDir] Default: `"src/wasm-interfaces"`.
 * @property {boolean} [force] Overwrite an existing `wasmbridge.config.json`. Default: `false`.
 * @property {string} [packageVersion] Version range to record for the `wasmbridge-net`
 *   devDependency. Defaults to `^<installed version>`.
 */

/**
 * @param {InitOptions} options
 * @returns {{ configPath: string, packageJsonPath: string, wasmProject: string, scriptsAdded: string[], dependencyAdded: boolean }}
 */
export function initProject(options = {}) {
  const cwd = options.cwd ?? process.cwd();

  const packageJsonPath = path.join(cwd, 'package.json');
  if (!existsSync(packageJsonPath)) {
    throw new Error(`wasmbridge-net: no package.json found at ${packageJsonPath}`);
  }

  const wasmProject = options.wasmProject ?? findWasmProject(cwd);
  if (!wasmProject) {
    throw new Error(
      'wasmbridge-net: could not auto-detect a WebAssembly project (looked for a *.csproj with ' +
        'Sdk="Microsoft.NET.Sdk.WebAssembly" in sibling directories); pass --project <path-to-csproj>.',
    );
  }
  const wasmProjectPath = path.resolve(cwd, wasmProject);
  if (!existsSync(wasmProjectPath)) {
    throw new Error(`wasmbridge-net: wasm project not found at ${wasmProjectPath}`);
  }

  const configPath = path.join(cwd, CONFIG_FILE_NAME);
  if (existsSync(configPath) && !options.force) {
    throw new Error(`wasmbridge-net: ${configPath} already exists (use --force to overwrite).`);
  }

  const config = {
    wasmProject: toPosixRelative(cwd, wasmProjectPath),
    publicDir: options.publicDir ?? 'public/wasm-app',
    srcDir: options.srcDir ?? 'src/wasm-interfaces',
  };
  writeFileSync(configPath, `${JSON.stringify(config, null, 2)}\n`);

  const packageJson = JSON.parse(readFileSync(packageJsonPath, 'utf8'));

  const scriptsAdded = [];
  packageJson.scripts ??= {};
  for (const [name, command] of Object.entries(DEFAULT_SCRIPTS)) {
    if (!(name in packageJson.scripts)) {
      packageJson.scripts[name] = command;
      scriptsAdded.push(name);
    }
  }

  let dependencyAdded = false;
  const alreadyDeclared =
    packageJson.dependencies?.['wasmbridge-net'] ?? packageJson.devDependencies?.['wasmbridge-net'];
  if (!alreadyDeclared) {
    packageJson.devDependencies ??= {};
    packageJson.devDependencies['wasmbridge-net'] = options.packageVersion ?? `^${getOwnVersion()}`;
    dependencyAdded = true;
  }

  writeFileSync(packageJsonPath, `${JSON.stringify(packageJson, null, 2)}\n`);

  return {
    configPath,
    packageJsonPath,
    wasmProject: config.wasmProject,
    scriptsAdded,
    dependencyAdded,
  };
}

/**
 * Looks for a single `*.csproj` with `Sdk="Microsoft.NET.Sdk.WebAssembly"` one level up from
 * `cwd` (i.e. a sibling of the front-end directory, the conventional WasmBridge.Net layout).
 * Returns `undefined` if zero or more than one candidate is found.
 */
function findWasmProject(cwd) {
  const parentDir = path.dirname(cwd);
  if (!existsSync(parentDir)) {
    return undefined;
  }

  const matches = [];
  for (const entry of readdirSync(parentDir, { withFileTypes: true })) {
    if (!entry.isDirectory() || IGNORED_DIRS.has(entry.name)) {
      continue;
    }

    const dir = path.join(parentDir, entry.name);
    let files;
    try {
      files = readdirSync(dir);
    } catch {
      continue;
    }

    for (const file of files) {
      if (!file.endsWith('.csproj')) {
        continue;
      }

      const csprojPath = path.join(dir, file);
      const content = readFileSync(csprojPath, 'utf8');
      if (/Sdk\s*=\s*"Microsoft\.NET\.Sdk\.WebAssembly"/.test(content)) {
        matches.push(csprojPath);
      }
    }
  }

  return matches.length === 1 ? matches[0] : undefined;
}

/**
 * Formats `toPath` relative to `fromDir` as a POSIX-style relative path (always prefixed with
 * `./` or `../`). Exported for reuse by the VS Code config generator.
 */
export function toPosixRelative(fromDir, toPath) {
  const relative = path.relative(fromDir, toPath).split(path.sep).join('/');
  return relative.startsWith('.') ? relative : `./${relative}`;
}

function getOwnVersion() {
  const ownPackageJson = JSON.parse(readFileSync(new URL('../package.json', import.meta.url), 'utf8'));
  return ownPackageJson.version;
}
