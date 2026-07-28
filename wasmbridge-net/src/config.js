import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';

const DEFAULT_CONFIG_NAME = 'wasmbridge.config.json';

/**
 * Loads the JSON config file (default `wasmbridge.config.json` in `cwd`, or `explicitPath` if
 * given). Returns `{}` if the default file doesn't exist (config is optional when every value
 * is supplied via CLI flags); throws if an explicitly requested path doesn't exist.
 * @param {string} cwd
 * @param {string | undefined} explicitPath
 */
export function loadConfig(cwd, explicitPath) {
  const configPath = path.resolve(cwd, explicitPath ?? DEFAULT_CONFIG_NAME);
  if (!existsSync(configPath)) {
    if (explicitPath) {
      throw new Error(`wasmbridge-net: config file not found at ${configPath}`);
    }
    return {};
  }

  const raw = readFileSync(configPath, 'utf8');
  try {
    return JSON.parse(raw);
  } catch (err) {
    throw new Error(`wasmbridge-net: failed to parse ${configPath}: ${err.message}`);
  }
}
