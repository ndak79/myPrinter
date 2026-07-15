import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const appJs = readFileSync(resolve(__dirname, '..', 'app.js'), 'utf8');

assert.match(appJs, /_pendingSheetScrollRestore:\s*null/);
assert.match(appJs, /_restorePendingSheetScroll\(fileId\)/);
assert.match(appJs, /const savedSheetScrollTop = fileEntry\._scrollPos\?\.sheet \?\? 0;/);
assert.match(appJs, /this\._scheduleSheetScrollRestore\(fileEntry\.id, savedSheetScrollTop\);/);
assert.match(appJs, /img\.addEventListener\('load', \(\) => this\._restorePendingSheetScroll\(fileId\), \{ once: true \}\);/);
assert.doesNotMatch(appJs, /this\._scrollAndBlinkEjected\(n\);/);
assert.doesNotMatch(appJs, /_scrollAndBlinkEjected\(pageNum\)\s*\{[\s\S]*?scrollIntoView/);
