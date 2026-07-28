import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { loadConfig } from '../src/config.js';

function makeTempDir() {
  return mkdtempSync(path.join(tmpdir(), 'wasmbridge-net-test-'));
}

test('loadConfig returns {} when the default config file is missing', () => {
  const dir = makeTempDir();
  try {
    assert.deepEqual(loadConfig(dir, undefined), {});
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('loadConfig throws when an explicitly requested config file is missing', () => {
  const dir = makeTempDir();
  try {
    assert.throws(() => loadConfig(dir, 'does-not-exist.json'), /config file not found/);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('loadConfig parses the default wasmbridge.config.json', () => {
  const dir = makeTempDir();
  try {
    const configPath = path.join(dir, 'wasmbridge.config.json');
    writeFileSync(configPath, JSON.stringify({ wasmProject: '../Foo/Foo.csproj', configuration: 'Debug' }));

    const config = loadConfig(dir, undefined);

    assert.equal(config.wasmProject, '../Foo/Foo.csproj');
    assert.equal(config.configuration, 'Debug');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('loadConfig throws a helpful error on invalid JSON', () => {
  const dir = makeTempDir();
  try {
    const configPath = path.join(dir, 'wasmbridge.config.json');
    writeFileSync(configPath, '{ not valid json');

    assert.throws(() => loadConfig(dir, undefined), /failed to parse/);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
