import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const appJs = readFileSync(resolve(__dirname, '..', 'app.js'), 'utf8');
const stylesCss = readFileSync(resolve(__dirname, '..', 'styles.css'), 'utf8');

assert.match(appJs, /_measureStableModalSize/);
assert.match(appJs, /guide\.recovery\.content/);
assert.match(appJs, /modal\.style\.height = `\$\{height}px`;/);
assert.match(appJs, /modal\.style\.width = `\$\{width}px`;/);
assert.match(stylesCss, /overflow-y:\s*auto;/);
