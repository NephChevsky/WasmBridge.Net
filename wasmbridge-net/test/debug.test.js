import { test } from 'node:test';
import assert from 'node:assert/strict';
import path from 'node:path';
import { defaultBrowserPaths, findBrowserExecutable, waitForServer } from '../src/debug.js';

test('defaultBrowserPaths returns edge vs. chrome candidates', () => {
  const chromeCandidates = defaultBrowserPaths('chrome');
  const edgeCandidates = defaultBrowserPaths('edge');

  assert.ok(chromeCandidates.length > 0);
  assert.ok(edgeCandidates.length > 0);
  assert.notDeepEqual(chromeCandidates, edgeCandidates);
});

test('findBrowserExecutable returns an explicit path unconditionally', () => {
  const explicit = path.join('some', 'custom', 'browser.exe');
  assert.equal(findBrowserExecutable('chrome', explicit), explicit);
});

test('findBrowserExecutable falls back to a bare executable name when no absolute candidate exists', () => {
  // On CI/dev machines without Chrome/Edge installed at the well-known absolute paths, non-Windows
  // platforms fall back to a bare executable name resolved via PATH by `spawn` itself.
  if (process.platform === 'win32') {
    return;
  }
  const result = findBrowserExecutable('chrome');
  assert.ok(typeof result === 'string' && result.length > 0);
});

test('waitForServer returns false when nothing is listening', async () => {
  const ready = await waitForServer('http://127.0.0.1:1/does-not-exist', 500);
  assert.equal(ready, false);
});
