#!/usr/bin/env node
import { syncWasm } from '../src/index.js';
import { loadConfig } from '../src/config.js';

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
      default:
        console.error(`wasmbridge-net: unknown option "${arg}"`);
        process.exit(1);
    }
  }
  return args;
}

function printUsage() {
  console.log(`Usage: wasmbridge-net sync [--debug|--release] [--config <path>] [--project <path>] [--public-dir <dir>] [--src-dir <dir>]

Builds (Debug) or publishes (Release) a WasmBridge.Net-enabled WebAssembly
project and copies its output (compiled app + generated TypeScript types)
into your front-end project.

Configuration is read from ./wasmbridge.config.json by default:
  {
    "wasmProject": "../GameEngine.Wasm/GameEngine.Wasm.csproj",
    "publicDir": "public/wasm-app",
    "srcDir": "src/wasm-interfaces"
  }
CLI flags override values from the config file.`);
}

const [command, ...rest] = process.argv.slice(2);

if (command !== 'sync') {
  printUsage();
  process.exit(command ? 1 : 0);
}

const args = parseArgs(rest);
const cwd = process.cwd();
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
