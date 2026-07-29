#!/usr/bin/env node
import path from 'node:path';
import { syncWasm } from '../src/index.js';
import { initProject } from '../src/init.js';
import { loadConfig } from '../src/config.js';
import { writeVsCodeConfig } from '../src/vscode.js';
import { runDebugSession } from '../src/debug.js';

function parseArgs(argv) {
  const args = {};
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    switch (arg) {
      case '--debug':
        args.configuration = 'Debug';
        break;
      case '--release':
        args.configuration = 'Release';
        break;
      case '--config':
        args.config = argv[++i];
        break;
      case '--project':
        args.wasmProject = argv[++i];
        break;
      case '--public-dir':
        args.publicDir = argv[++i];
        break;
      case '--src-dir':
        args.srcDir = argv[++i];
        break;
      case '--force':
        args.force = true;
        break;
      case '--vscode':
        args.vscode = true;
        break;
      case '--port':
        args.port = Number(argv[++i]);
        break;
      case '--browser':
        args.browser = argv[++i];
        break;
      case '--browser-path':
        args.browserPath = argv[++i];
        break;
      case '--dev-command':
        args.devCommand = argv[++i];
        break;
      case '--remote-debugging-port':
        args.remoteDebuggingPort = Number(argv[++i]);
        break;
      default:
        console.error(`wasmbridge-net: unknown option "${arg}"`);
        process.exit(1);
    }
  }
  return args;
}

function printUsage() {
  console.log(`Usage:
  wasmbridge-net init [--project <path>] [--public-dir <dir>] [--src-dir <dir>] [--force]
                      [--vscode] [--port <n>] [--browser chrome|edge]
  wasmbridge-net sync [--debug|--release] [--config <path>] [--project <path>] [--public-dir <dir>] [--src-dir <dir>]
  wasmbridge-net debug [--config <path>] [--project <path>] [--public-dir <dir>] [--src-dir <dir>]
                       [--port <n>] [--browser chrome|edge] [--browser-path <path>]
                       [--dev-command <cmd>] [--remote-debugging-port <n>]

init scaffolds a front-end project: writes ./wasmbridge.config.json (auto-detecting the
WebAssembly project's .csproj from sibling directories if --project isn't given) and adds the
sync:wasm*/predev/prebuild scripts + the wasmbridge-net devDependency to ./package.json.
Pass --vscode to also write/update .vscode/tasks.json and .vscode/launch.json (VS Code's
"blazorwasm" debug type) in the enclosing workspace (nearest ancestor with a .sln/.slnx or .git).

sync builds (Debug) or publishes (Release) a WasmBridge.Net-enabled WebAssembly project and
copies its output (compiled app + generated TypeScript types) into your front-end project.

debug is an IDE-agnostic alternative: it syncs a Debug build, starts the front-end dev server
(--dev-command, default "npm run dev"), and launches Chrome/Edge with remote debugging enabled
so any IDE/debugger can attach (Chrome DevTools Protocol on --remote-debugging-port, default
9222) - e.g. VS Code's own "blazorwasm" launch config (see init --vscode), a generic "Attach to
Chrome" config, chrome://inspect, or another IDE's Blazor WebAssembly debug support.

Configuration is read from ./wasmbridge.config.json by default:
  {
    "wasmProject": "../GameEngine.Wasm/GameEngine.Wasm.csproj",
    "publicDir": "public/wasm-app",
    "srcDir": "src/wasm-interfaces"
  }
CLI flags override values from the config file.`);
}

const [command, ...rest] = process.argv.slice(2);

if (command !== 'sync' && command !== 'init' && command !== 'debug') {
  printUsage();
  process.exit(command ? 1 : 0);
}

const args = parseArgs(rest);
const cwd = process.cwd();

if (command === 'init') {
  try {
    const result = initProject({
      cwd,
      wasmProject: args.wasmProject,
      publicDir: args.publicDir,
      srcDir: args.srcDir,
      force: args.force,
    });
    console.log(`[wasmbridge-net] Wrote ${result.configPath} (wasmProject: ${result.wasmProject})`);
    if (result.scriptsAdded.length) {
      console.log(`[wasmbridge-net] Added scripts to package.json: ${result.scriptsAdded.join(', ')}`);
    }
    if (result.dependencyAdded) {
      console.log('[wasmbridge-net] Added wasmbridge-net to devDependencies - run npm install.');
    }

    if (args.vscode) {
      const vscodeResult = writeVsCodeConfig({
        cwd,
        wasmProjectPath: path.resolve(cwd, result.wasmProject),
        port: args.port,
        browser: args.browser,
        force: args.force,
      });
      console.log(
        `[wasmbridge-net] ${vscodeResult.taskAdded ? 'Added' : 'Already present -'} "sync-wasm-debug" task in ${vscodeResult.tasksPath}`,
      );
      console.log(
        `[wasmbridge-net] ${vscodeResult.configAdded ? 'Added' : 'Already present -'} launch config in ${vscodeResult.launchPath}`,
      );
    }
  } catch (err) {
    console.error(err instanceof Error ? err.message : err);
    process.exit(1);
  }
  process.exit(0);
}

if (command === 'debug') {
  const fileConfig = loadConfig(cwd, args.config);
  try {
    await runDebugSession({
      cwd,
      wasmProject: args.wasmProject ?? fileConfig.wasmProject,
      publicDir: args.publicDir ?? fileConfig.publicDir,
      srcDir: args.srcDir ?? fileConfig.srcDir,
      port: args.port,
      browser: args.browser,
      browserPath: args.browserPath,
      devCommand: args.devCommand,
      remoteDebuggingPort: args.remoteDebuggingPort,
    });
  } catch (err) {
    console.error(err instanceof Error ? err.message : err);
    process.exit(1);
  }
}

if (command === 'sync') {
  const fileConfig = loadConfig(cwd, args.config);

  const options = {
    cwd,
    wasmProject: args.wasmProject ?? fileConfig.wasmProject,
    publicDir: args.publicDir ?? fileConfig.publicDir,
    srcDir: args.srcDir ?? fileConfig.srcDir,
    configuration: args.configuration ?? fileConfig.configuration,
  };

  try {
    syncWasm(options);
  } catch (err) {
    console.error(err instanceof Error ? err.message : err);
    process.exit(1);
  }
}
