import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const appJs = readFileSync(resolve(__dirname, '..', 'app.js'), 'utf8');

assert.match(appJs, /const targetLang = this\._lang === 'vi' \? 'EN' : 'VI';/);
assert.match(appJs, /btn\.textContent = targetLang;/);
