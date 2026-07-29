// IDE-agnostic debug orchestration: syncs a Debug build, starts the front-end dev server, and
// launches Chrome/Edge with remote debugging enabled. This automates everything a CLI *can*
// automate; the actual C#-breakpoint bridging (browser V8 protocol <-> .NET/mono debugger) is
// still provided by whichever IDE/debugger you attach afterwards (VS Code's "blazorwasm" launch
// config - see `wasmbridge-net init --vscode` - another IDE's Blazor WebAssembly support, or
// plain `chrome://inspect`).
import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { syncWasm } from './index.js';

/**
 * @typedef {Object} DebugSessionOptions
 * @property {string} [cwd] Front-end project directory. Default: `process.cwd()`.
 * @property {string} wasmProject Path to the `.csproj` (resolved against `cwd`).
 * @property {string} [publicDir] Forwarded to `syncWasm`.
 * @property {string} [srcDir] Forwarded to `syncWasm`.
 * @property {number} [port] Dev server port. Default: `5173`.
 * @property {'chrome' | 'edge'} [browser] Default: `"chrome"`.
 * @property {string} [browserPath] Explicit path to the browser executable, bypassing auto-detection.
 * @property {string} [devCommand] Command that starts the dev server. Default: `"npm run dev"`.
 * @property {number} [remoteDebuggingPort] Chrome DevTools Protocol port. Default: `9222`.
 * @property {string} [userDataDir] Chrome/Edge profile dir. Default: a dedicated temp dir, so
 *   this doesn't collide with your everyday browser profile.
 * @property {number} [serverTimeoutMs] How long to wait for the dev server before launching the
 *   browser anyway. Default: `20000`.
 */

/**
 * @param {DebugSessionOptions} options
 */
export async function runDebugSession(options = {}) {
  const cwd = options.cwd ?? process.cwd();
  const port = options.port ?? 5173;
  const browser = options.browser === 'edge' ? 'edge' : 'chrome';
  const devCommand = options.devCommand ?? 'npm run dev';
  const remoteDebuggingPort = options.remoteDebuggingPort ?? 9222;
  const url = `http://localhost:${port}`;

  syncWasm({
    cwd,
    wasmProject: options.wasmProject,
    publicDir: options.publicDir,
    srcDir: options.srcDir,
    configuration: 'Debug',
  });

  console.log(`[wasmbridge-net] Starting dev server: ${devCommand}`);
  const devServer = spawn(devCommand, { cwd, shell: true, stdio: 'inherit' });

  const ready = await waitForServer(url, options.serverTimeoutMs ?? 20000);
  if (!ready) {
    console.warn(`[wasmbridge-net] ${url} didn't respond within the timeout - launching the browser anyway.`);
  }

  const browserPath = findBrowserExecutable(browser, options.browserPath);
  if (!browserPath) {
    console.warn(
      `[wasmbridge-net] Could not locate a ${browser} executable - open ${url} yourself with ` +
        `--remote-debugging-port=${remoteDebuggingPort} --remote-allow-origins=* to enable attaching a debugger ` +
        '(or pass --browser-path).',
    );
  } else {
    const userDataDir = options.userDataDir ?? path.join(tmpdir(), 'wasmbridge-net-debug-profile');
    console.log(`[wasmbridge-net] Launching ${browser} (remote debugging on port ${remoteDebuggingPort})...`);
    const browserProcess = spawn(
      browserPath,
      [`--remote-debugging-port=${remoteDebuggingPort}`, '--remote-allow-origins=*', `--user-data-dir=${userDataDir}`, url],
      { stdio: 'ignore', detached: true },
    );
    browserProcess.unref();
  }

  console.log(`[wasmbridge-net] Ready - attach a debugger to ${url} (Chrome DevTools Protocol on port ${remoteDebuggingPort}):`);
  console.log('  - VS Code: a "blazorwasm" launch config (see `wasmbridge-net init --vscode`), or a generic "Attach to Chrome" config.');
  console.log('  - Other IDEs: their Blazor WebAssembly / attach-to-browser debug feature, pointed at the same port.');
  console.log('  - No IDE: open chrome://inspect in Chrome.');
  console.log('Press Ctrl+C to stop.');

  const shutdown = () => {
    devServer.kill();
    process.exit(0);
  };
  process.on('SIGINT', shutdown);
  process.on('SIGTERM', shutdown);

  await new Promise((resolve) => devServer.on('exit', resolve));
}

/**
 * Polls `url` until it responds (any status) or `timeoutMs` elapses.
 * @param {string} url
 * @param {number} timeoutMs
 */
export async function waitForServer(url, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    try {
      await fetch(url);
      return true;
    } catch {
      // Not up yet.
    }
    await new Promise((resolve) => setTimeout(resolve, 300));
  }
  return false;
}

/**
 * Well-known install locations for Chrome/Edge, checked in order. Absolute candidates are only
 * returned if they exist; bare executable names (Linux) are returned as-is and resolved via PATH
 * by `spawn` itself.
 * @param {'chrome' | 'edge'} browser
 */
export function defaultBrowserPaths(browser) {
  const programFiles = process.env['ProgramFiles'] || 'C:\\Program Files';
  const programFilesX86 = process.env['ProgramFiles(x86)'] || 'C:\\Program Files (x86)';

  if (process.platform === 'win32') {
    return browser === 'edge'
      ? [
          path.join(programFilesX86, 'Microsoft', 'Edge', 'Application', 'msedge.exe'),
          path.join(programFiles, 'Microsoft', 'Edge', 'Application', 'msedge.exe'),
        ]
      : [
          path.join(programFiles, 'Google', 'Chrome', 'Application', 'chrome.exe'),
          path.join(programFilesX86, 'Google', 'Chrome', 'Application', 'chrome.exe'),
        ];
  }

  if (process.platform === 'darwin') {
    return browser === 'edge'
      ? ['/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge']
      : ['/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'];
  }

  return browser === 'edge'
    ? ['microsoft-edge', 'microsoft-edge-stable']
    : ['google-chrome', 'google-chrome-stable', 'chromium', 'chromium-browser'];
}

/**
 * @param {'chrome' | 'edge'} browser
 * @param {string} [explicitPath]
 */
export function findBrowserExecutable(browser, explicitPath) {
  if (explicitPath) {
    return explicitPath;
  }
  for (const candidate of defaultBrowserPaths(browser)) {
    if (!path.isAbsolute(candidate) || existsSync(candidate)) {
      return candidate;
    }
  }
  return undefined;
}
