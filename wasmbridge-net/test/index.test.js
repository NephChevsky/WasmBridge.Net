import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { syncWasm } from '../src/index.js';

test('syncWasm throws when wasmProject is missing', () => {
  assert.throws(() => syncWasm({}), /"wasmProject" is required/);
});

test('syncWasm throws when the .csproj does not exist', () => {
  const dir = mkdtempSync(path.join(tmpdir(), 'wasmbridge-net-test-'));
  try {
    assert.throws(
      () => syncWasm({ cwd: dir, wasmProject: 'DoesNotExist.csproj' }),
      /wasm project not found/,
    );
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('syncWasm throws when the .csproj has no <TargetFramework>', () => {
  const dir = mkdtempSync(path.join(tmpdir(), 'wasmbridge-net-test-'));
  try {
    const csprojPath = path.join(dir, 'Foo.csproj');
    writeFileSync(csprojPath, '<Project Sdk="Microsoft.NET.Sdk.WebAssembly"></Project>');

    assert.throws(
      () => syncWasm({ cwd: dir, wasmProject: 'Foo.csproj' }),
      /could not find <TargetFramework>/,
    );
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
