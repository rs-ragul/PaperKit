#!/usr/bin/env node
/**
 * Build the VeriPDF web bundle.
 *
 * Output goes to app/dist/ and is a plain static folder - that is what the
 * Windows shell (WebView2 / Tauri / Electron) and the Android app
 * (Capacitor) both load.
 */
import * as esbuild from 'esbuild';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(here, '..');
// The web sources live in app/web in the monorepo and in web/ in the
// standalone packages.
const WEB_CANDIDATES = [path.join(ROOT, 'app/web'), path.join(ROOT, 'web')];
const WEB = WEB_CANDIDATES.find((d) => fs.existsSync(path.join(d, 'index.html'))) || WEB_CANDIDATES[0];

// Output folder. Defaults to app/dist; the standalone Android package passes
// --out www so that it matches Capacitor's webDir.
const outIdx = process.argv.indexOf('--out');
const DIST = outIdx !== -1 && process.argv[outIdx + 1]
  ? path.resolve(ROOT, process.argv[outIdx + 1])
  : path.join(ROOT, 'app/dist');

const dev = process.argv.includes('--dev');
const watch = process.argv.includes('--watch');

fs.rmSync(DIST, { recursive: true, force: true });
fs.mkdirSync(DIST, { recursive: true });

/** copy the static shell */
function copyStatic() {
  fs.copyFileSync(path.join(WEB, 'index.html'), path.join(DIST, 'index.html'));
  fs.copyFileSync(path.join(WEB, 'styles.css'), path.join(DIST, 'styles.css'));
  if (fs.existsSync(path.join(WEB, 'bundled-roots.json'))) {
    fs.copyFileSync(path.join(WEB, 'bundled-roots.json'), path.join(DIST, 'bundled-roots.json'));
  }
  // node-forge is bundled by esbuild, so no vendor copy is needed.
}

const options = {
  entryPoints: [path.join(WEB, 'entry.js')],
  bundle: true,
  format: 'iife',
  target: ['es2020'],
  outfile: path.join(DIST, 'app.js'),
  sourcemap: dev,
  minify: !dev,
  logLevel: 'info',
  define: { 'process.env.NODE_ENV': JSON.stringify(dev ? 'development' : 'production') },
};

copyStatic();

if (watch) {
  const ctx = await esbuild.context(options);
  await ctx.watch();
  console.log('watching...');
} else {
  await esbuild.build(options);
  const size = fs.statSync(path.join(DIST, 'app.js')).size;
  console.log(`built ${path.relative(ROOT, DIST)}/app.js  (${(size / 1024).toFixed(0)} KB)`);
}
