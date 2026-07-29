// Scaffolds VS Code's `.vscode/tasks.json` + `.vscode/launch.json` for debugging a
// WasmBridge.Net-enabled WebAssembly project through its front-end (VS Code's "blazorwasm" debug
// type). Merges into existing files non-destructively (an existing task/config with the same
// label/name is left untouched unless `force` is set).
import { existsSync, readFileSync, writeFileSync, mkdirSync, readdirSync } from 'node:fs';
import path from 'node:path';
import { getTargetFramework } from './index.js';
import { toPosixRelative } from './init.js';

/**
 * Walks up from `cwd` looking for the enclosing workspace root (a directory containing a
 * `.sln`/`.slnx` file, or failing that a `.git` directory). Falls back to `cwd` itself if none is
 * found within a few levels - the conventional WasmBridge.Net layout has the front-end and the
 * WebAssembly project as siblings one level below the workspace root.
 * @param {string} cwd
 */
export function findWorkspaceRoot(cwd) {
  let dir = cwd;
  for (let i = 0; i < 4; i++) {
    let entries;
    try {
      entries = readdirSync(dir);
    } catch {
      break;
    }
    if (entries.some((name) => name.endsWith('.sln') || name.endsWith('.slnx'))) {
      return dir;
    }
    const parent = path.dirname(dir);
    if (parent === dir) {
      break;
    }
    dir = parent;
  }
  // No .sln/.slnx found - fall back to the nearest ancestor with a .git directory, else cwd.
  dir = cwd;
  for (let i = 0; i < 4; i++) {
    if (existsSync(path.join(dir, '.git'))) {
      return dir;
    }
    const parent = path.dirname(dir);
    if (parent === dir) {
      break;
    }
    dir = parent;
  }
  return cwd;
}

/**
 * @typedef {Object} VsCodeConfigOptions
 * @property {string} [cwd] The front-end project's directory (where `package.json` lives).
 *   Default: `process.cwd()`.
 * @property {string} wasmProjectPath Absolute path to the WebAssembly project's `.csproj`.
 * @property {string} [workspaceRoot] Directory `.vscode` is written to. Auto-detected (nearest
 *   ancestor with a `.sln`/`.slnx` or `.git`) if omitted.
 * @property {number} [port] Front-end dev server port. Default: `5173`.
 * @property {'chrome' | 'edge'} [browser] Default: `"chrome"`.
 * @property {string} [devCommand] Command VS Code runs to start the dev server. Default: `"npm run dev"`.
 * @property {string} [syncTaskLabel] Task label for the `sync:wasm:debug` pre-launch task.
 *   Default: `"sync-wasm-debug"`.
 * @property {string} [configName] `launch.json` configuration name. Defaults to a name derived
 *   from the project/browser.
 * @property {boolean} [force] Overwrite an existing task/config with the same label/name.
 *   Default: `false`.
 */

/**
 * @param {VsCodeConfigOptions} options
 * @returns {{ tasksPath: string, launchPath: string, taskAdded: boolean, configAdded: boolean, workspaceRoot: string }}
 */
export function writeVsCodeConfig(options = {}) {
  const cwd = options.cwd ?? process.cwd();

  if (!options.wasmProjectPath) {
    throw new Error('wasmbridge-net: "wasmProjectPath" is required (absolute path to the .csproj).');
  }
  if (!existsSync(options.wasmProjectPath)) {
    throw new Error(`wasmbridge-net: wasm project not found at ${options.wasmProjectPath}`);
  }

  const workspaceRoot = options.workspaceRoot ?? findWorkspaceRoot(cwd);
  const port = options.port ?? 5173;
  const browser = options.browser === 'edge' ? 'edge' : 'chrome';
  const devCommand = options.devCommand ?? 'npm run dev';
  const syncTaskLabel = options.syncTaskLabel ?? 'sync-wasm-debug';

  const wasmProjectDir = path.dirname(options.wasmProjectPath);
  const projectName = path.basename(options.wasmProjectPath, path.extname(options.wasmProjectPath));
  const targetFramework = getTargetFramework(options.wasmProjectPath);

  // Strip the leading "./" toPosixRelative always adds - VS Code's tasks.json `path` and
  // launch.json paths built on `${workspaceFolder}/...` read more naturally without it.
  const frontEndRel = toPosixRelative(workspaceRoot, cwd).replace(/^\.\//, '');
  const wasmProjectRel = toPosixRelative(workspaceRoot, wasmProjectDir).replace(/^\.\//, '');

  const vscodeDir = path.join(workspaceRoot, '.vscode');
  mkdirSync(vscodeDir, { recursive: true });

  const tasksPath = path.join(vscodeDir, 'tasks.json');
  const tasksJson = readJson(tasksPath) ?? { version: '2.0.0', tasks: [] };
  tasksJson.tasks ??= [];
  const taskAdded = upsert(
    tasksJson.tasks,
    (task) => task.label === syncTaskLabel,
    {
      label: syncTaskLabel,
      type: 'npm',
      script: 'sync:wasm:debug',
      path: frontEndRel,
      problemMatcher: [],
    },
    options.force,
  );
  writeFileSync(tasksPath, `${JSON.stringify(tasksJson, null, 2)}\n`);

  const launchPath = path.join(vscodeDir, 'launch.json');
  const launchJson = readJson(launchPath) ?? { version: '0.2.0', configurations: [] };
  launchJson.configurations ??= [];
  const configName =
    options.configName ?? `Debug ${projectName} in ${browser === 'edge' ? 'Edge' : 'Chrome'} (via ${path.basename(cwd)})`;
  const configAdded = upsert(
    launchJson.configurations,
    (configEntry) => configEntry.name === configName,
    {
      name: configName,
      type: 'blazorwasm',
      request: 'launch',
      preLaunchTask: syncTaskLabel,
      cwd: `\${workspaceFolder}/${wasmProjectRel}`,
      url: `http://localhost:${port}`,
      browser,
      webRoot: `\${workspaceFolder}/${wasmProjectRel}/bin/Debug/${targetFramework}/wwwroot`,
      browserConfig: {
        runtimeArgs: ['--remote-allow-origins=*'],
        server: {
          command: devCommand,
          cwd: `\${workspaceFolder}/${frontEndRel}`,
          autoAttachChildProcesses: false,
        },
      },
      trace: 'verbose',
    },
    options.force,
  );
  writeFileSync(launchPath, `${JSON.stringify(launchJson, null, 2)}\n`);

  return { tasksPath, launchPath, taskAdded, configAdded, workspaceRoot };
}

function readJson(filePath) {
  if (!existsSync(filePath)) {
    return undefined;
  }
  const raw = readFileSync(filePath, 'utf8');
  try {
    return JSON.parse(raw);
  } catch (err) {
    throw new Error(
      `wasmbridge-net: failed to parse ${filePath} (${err.message}) - comments/trailing commas ` +
        'aren\'t supported for auto-merging; add the task/config by hand or remove them first.',
    );
  }
}

/**
 * Inserts `entry` into `list` unless a matching item already exists (per `matches`), in which
 * case it's left untouched - unless `force` is set, where it's replaced in place.
 * @returns {boolean} Whether `entry` was added or replaced.
 */
function upsert(list, matches, entry, force) {
  const index = list.findIndex(matches);
  if (index === -1) {
    list.push(entry);
    return true;
  }
  if (force) {
    list[index] = entry;
    return true;
  }
  return false;
}
