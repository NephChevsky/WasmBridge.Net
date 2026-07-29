import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { initProject } from '../src/init.js';

function makeTempDir() {
  return mkdtempSync(path.join(tmpdir(), 'wasmbridge-net-test-'));
}

function makeFrontEndProject(root) {
  const frontEndDir = path.join(root, 'web');
  mkdirSync(frontEndDir, { recursive: true });
  writeFileSync(
    path.join(frontEndDir, 'package.json'),
    JSON.stringify({ name: 'web', version: '0.0.0', scripts: {} }, null, 2),
  );
  return frontEndDir;
}

function makeWasmProject(root, name = 'GameEngine.Wasm') {
  const wasmDir = path.join(root, name);
  mkdirSync(wasmDir, { recursive: true });
  const csprojPath = path.join(wasmDir, `${name}.csproj`);
  writeFileSync(
    csprojPath,
    '<Project Sdk="Microsoft.NET.Sdk.WebAssembly"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>',
  );
  return csprojPath;
}

test('initProject throws when package.json is missing', () => {
  const dir = makeTempDir();
  try {
    assert.throws(() => initProject({ cwd: dir }), /no package\.json found/);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('initProject throws when it cannot auto-detect a wasm project', () => {
  const root = makeTempDir();
  try {
    const frontEndDir = makeFrontEndProject(root);
    assert.throws(() => initProject({ cwd: frontEndDir }), /could not auto-detect/);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('initProject auto-detects a sibling .csproj and writes the config + package.json', () => {
  const root = makeTempDir();
  try {
    const frontEndDir = makeFrontEndProject(root);
    makeWasmProject(root);

    const result = initProject({ cwd: frontEndDir, packageVersion: '^1.2.3' });

    const config = JSON.parse(readFileSync(result.configPath, 'utf8'));
    assert.equal(config.wasmProject, '../GameEngine.Wasm/GameEngine.Wasm.csproj');
    assert.equal(config.publicDir, 'public/wasm-app');
    assert.equal(config.srcDir, 'src/wasm-interfaces');

    const packageJson = JSON.parse(readFileSync(result.packageJsonPath, 'utf8'));
    assert.equal(packageJson.scripts['sync:wasm'], 'wasmbridge-net sync --release');
    assert.equal(packageJson.scripts['sync:wasm:debug'], 'wasmbridge-net sync --debug');
    assert.equal(packageJson.scripts.predev, 'npm run sync:wasm:debug');
    assert.equal(packageJson.scripts.prebuild, 'npm run sync:wasm');
    assert.equal(packageJson.devDependencies['wasmbridge-net'], '^1.2.3');

    assert.deepEqual(result.scriptsAdded.sort(), ['predev', 'prebuild', 'sync:wasm', 'sync:wasm:debug'].sort());
    assert.equal(result.dependencyAdded, true);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('initProject throws when multiple sibling .csproj candidates are found', () => {
  const root = makeTempDir();
  try {
    const frontEndDir = makeFrontEndProject(root);
    makeWasmProject(root, 'GameEngine.Wasm');
    makeWasmProject(root, 'OtherEngine.Wasm');

    assert.throws(() => initProject({ cwd: frontEndDir }), /could not auto-detect/);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('initProject does not overwrite an existing config unless --force', () => {
  const root = makeTempDir();
  try {
    const frontEndDir = makeFrontEndProject(root);
    makeWasmProject(root);
    writeFileSync(path.join(frontEndDir, 'wasmbridge.config.json'), '{"wasmProject":"custom"}');

    assert.throws(() => initProject({ cwd: frontEndDir }), /already exists/);

    const result = initProject({ cwd: frontEndDir, force: true, packageVersion: '^1.0.0' });
    const config = JSON.parse(readFileSync(result.configPath, 'utf8'));
    assert.equal(config.wasmProject, '../GameEngine.Wasm/GameEngine.Wasm.csproj');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('initProject preserves existing scripts and does not re-add an existing dependency', () => {
  const root = makeTempDir();
  try {
    const frontEndDir = makeFrontEndProject(root);
    makeWasmProject(root);
    writeFileSync(
      path.join(frontEndDir, 'package.json'),
      JSON.stringify(
        {
          name: 'web',
          scripts: { 'sync:wasm': 'custom-command' },
          devDependencies: { 'wasmbridge-net': '^0.5.0' },
        },
        null,
        2,
      ),
    );

    const result = initProject({ cwd: frontEndDir });

    const packageJson = JSON.parse(readFileSync(result.packageJsonPath, 'utf8'));
    assert.equal(packageJson.scripts['sync:wasm'], 'custom-command');
    assert.equal(packageJson.devDependencies['wasmbridge-net'], '^0.5.0');
    assert.equal(result.dependencyAdded, false);
    assert.ok(!result.scriptsAdded.includes('sync:wasm'));
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
