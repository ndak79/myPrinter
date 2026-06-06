import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const appJs = readFileSync(resolve(__dirname, '..', 'app.js'), 'utf8');

const startPrintBlock = appJs.match(/async _startPrint\(\)\s*\{[\s\S]*?\n    async _continuePrint/)?.[0] ?? '';
const continuePrintBlock = appJs.match(/async _continuePrint\(\)\s*\{[\s\S]*?\n    async _completeCurrentManualJob/)?.[0] ?? '';

assert.match(startPrintBlock, /manualJobs\s*=\s*\[\]/, 'manual front-pass jobs should be accumulated before showing the flip modal');
assert.match(startPrintBlock, /manualJobs\.push/, 'manual jobs should be queued instead of stopping at the first file');
const manualFrontBlock = startPrintBlock.match(/if \(result\.jobState\?\.waitingForFlip\) \{[\s\S]*?manualJobs\.push\(manualJob\);\s*continue;\s*\}/)?.[0] ?? '';
assert.match(manualFrontBlock, /manualJobs\.push\(manualJob\);[\s\S]*continue;/, 'multi-file manual duplex must continue collecting front-pass jobs');
assert.doesNotMatch(manualFrontBlock, /return;/, 'manual front-pass collection must not return before later files are sent');
assert.match(
  continuePrintBlock,
  /manualBatch\.jobs\.slice\(\)\.reverse\(\)/,
  'back pass for a front-pass batch must run in reverse file order for a face-down stack',
);
assert.doesNotMatch(appJs, /setTimeout\(r,\s*500\)/, 'printing should not add idle pauses between files');
