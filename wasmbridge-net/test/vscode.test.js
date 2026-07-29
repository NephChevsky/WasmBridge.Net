import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { writeVsCodeConfig, findWorkspaceRoot } from '../src/vscode.js';

function makeTempDir() {
  return mkdtempSync(path.join(tmpdir(), 'wasmbridge-net-test-'));
}

function makeWorkspace(root) {
  writeFileSync(path.join(root, 'Solution.slnx'), '<Solution />');
  const frontEndDir = path.join(root, 'front-end');
  mkdirSync(frontEndDir, { recursive: true });

  const wasmDir = path.join(root, 'GameEngine.Wasm');
  mkdirSync(wasmDir, { recursive: true });
  const csprojPath = path.join(wasmDir, 'GameEngine.Wasm.csproj');
  writeFileSync(
    csprojPath,
    '<Project Sdk="Microsoft.NET.Sdk.WebAssembly"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>',
  );

  return { root, frontEndDir, csprojPath };
}

test('findWorkspaceRoot walks up to the nearest ancestor with a .sln/.slnx', () => {
  const root = makeTempDir();
  try {
    const { frontEndDir } = makeWorkspace(root);
    assert.equal(findWorkspaceRoot(frontEndDir), root);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('findWorkspaceRoot falls back to cwd when nothing is found', () => {
  const dir = makeTempDir();
  try {
    assert.equal(findWorkspaceRoot(dir), dir);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('writeVsCodeConfig writes tasks.json and launch.json from scratch', () => {
  const root = makeTempDir();
  try {
    const { frontEndDir, csprojPath } = makeWorkspace(root);

    const result = writeVsCodeConfig({ cwd: frontEndDir, wasmProjectPath: csprojPath });

    assert.equal(result.workspaceRoot, root);
    assert.equal(result.taskAdded, true);
    assert.equal(result.configAdded, true);

    const tasks = JSON.parse(readFileSync(result.tasksPath, 'utf8'));
    assert.equal(tasks.tasks.length, 1);
    assert.deepEqual(tasks.tasks[0], {
      label: 'sync-wasm-debug',
      type: 'npm',
      script: 'sync:wasm:debug',
      path: 'front-end',
      problemMatcher: [],
    });

    const launch = JSON.parse(readFileSync(result.launchPath, 'utf8'));
    assert.equal(launch.configurations.length, 1);
    const configEntry = launch.configurations[0];
    assert.equal(configEntry.type, 'blazorwasm');
    assert.equal(configEntry.preLaunchTask, 'sync-wasm-debug');
    assert.equal(configEntry.url, 'http://localhost:5173');
    assert.equal(configEntry.browser, 'chrome');
    assert.equal(configEntry.cwd, '${workspaceFolder}/GameEngine.Wasm');
    assert.equal(configEntry.webRoot, '${workspaceFolder}/GameEngine.Wasm/bin/Debug/net9.0/wwwroot');
    assert.equal(configEntry.browserConfig.server.cwd, '${workspaceFolder}/front-end');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('writeVsCodeConfig does not duplicate an existing task/config unless forced', () => {
  const root = makeTempDir();
  try {
    const { frontEndDir, csprojPath } = makeWorkspace(root);

    writeVsCodeConfig({ cwd: frontEndDir, wasmProjectPath: csprojPath });
    const second = writeVsCodeConfig({ cwd: frontEndDir, wasmProjectPath: csprojPath, port: 4000 });

    assert.equal(second.taskAdded, false);
    assert.equal(second.configAdded, false);

    const launch = JSON.parse(readFileSync(second.launchPath, 'utf8'));
    assert.equal(launch.configurations.length, 1);
    assert.equal(launch.configurations[0].url, 'http://localhost:5173');

    const third = writeVsCodeConfig({ cwd: frontEndDir, wasmProjectPath: csprojPath, port: 4000, force: true });
    assert.equal(third.configAdded, true);

    const launchAfterForce = JSON.parse(readFileSync(third.launchPath, 'utf8'));
    assert.equal(launchAfterForce.configurations.length, 1);
    assert.equal(launchAfterForce.configurations[0].url, 'http://localhost:4000');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('writeVsCodeConfig respects --browser edge in the config name and entry', () => {
  const root = makeTempDir();
  try {
    const { frontEndDir, csprojPath } = makeWorkspace(root);

    const result = writeVsCodeConfig({ cwd: frontEndDir, wasmProjectPath: csprojPath, browser: 'edge' });

    const launch = JSON.parse(readFileSync(result.launchPath, 'utf8'));
    assert.equal(launch.configurations[0].browser, 'edge');
    assert.match(launch.configurations[0].name, /Edge/);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('writeVsCodeConfig throws a helpful error on an unparsable existing tasks.json', () => {
  const root = makeTempDir();
  try {
    const { frontEndDir, csprojPath } = makeWorkspace(root);
    mkdirSync(path.join(root, '.vscode'), { recursive: true });
    writeFileSync(path.join(root, '.vscode', 'tasks.json'), '{ // a comment\n "tasks": [] }');

    assert.throws(
      () => writeVsCodeConfig({ cwd: frontEndDir, wasmProjectPath: csprojPath }),
      /failed to parse/,
    );
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
