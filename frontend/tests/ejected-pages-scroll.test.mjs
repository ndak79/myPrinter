import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const appJs = readFileSync(resolve(__dirname, '..', 'app.js'), 'utf8');
const stylesCss = readFileSync(resolve(__dirname, '..', 'styles.css'), 'utf8');

const ejectedRenderBlock = appJs.match(/const ejectedCol = document\.createElement\('div'\);[\s\S]*?inner\.appendChild\(ejectedCol\);/)?.[0] ?? '';

assert.match(ejectedRenderBlock, /const ejectedPagesScroll = document\.createElement\('div'\);/);
assert.match(ejectedRenderBlock, /ejectedPagesScroll\.className = 'ejected-pages-scroll';/);
assert.match(ejectedRenderBlock, /ejectedPagesScroll\.appendChild\(card\);/);
assert.match(ejectedRenderBlock, /ejectedCol\.appendChild\(ejectedPagesScroll\);/);
assert.doesNotMatch(ejectedRenderBlock, /ejectedCol\.appendChild\(card\);/);

assert.match(stylesCss, /\.ejected-column\s*\{[\s\S]*position:\s*sticky;/);
assert.match(stylesCss, /\.ejected-column\s*\{[\s\S]*top:\s*0;/);
assert.match(stylesCss, /\.ejected-column\s*\{[\s\S]*max-height:\s*calc\(100dvh - 48px\);/);
assert.match(stylesCss, /\.ejected-pages-scroll\s*\{[\s\S]*overflow-y:\s*auto;/);
assert.match(stylesCss, /\.ejected-pages-scroll\s*\{[\s\S]*overscroll-behavior:\s*contain;/);
