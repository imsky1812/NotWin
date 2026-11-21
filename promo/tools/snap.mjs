import puppeteer from 'puppeteer-core';
import { mkdir } from 'node:fs/promises';
const [outDir, ...times] = process.argv.slice(2);
const URL = process.env.URL || 'file:///D:/NothingLauncher/promo/index.html?clean=1';
await mkdir(outDir, { recursive: true });
const b = await puppeteer.launch({executablePath:'C:/Program Files/Google/Chrome/Application/chrome.exe', headless:'new', args:['--allow-file-access-from-files']});
const p = await b.newPage();
p.on('pageerror', e => console.log('PAGEERROR', e.message));
p.on('console', m => { if (m.type()==='error'||m.type()==='warning') console.log('CONSOLE', m.text()); });
await p.goto(URL, { waitUntil: 'networkidle0' });
await p.waitForFunction('window.OPENER && window.OPENER.ready', { timeout: 60000 });
const { W, H } = await p.evaluate(() => ({ W: OPENER.W, H: OPENER.H }));
await p.setViewport({ width: W, height: H });
for (const ts of times) { const t = +ts;
  await p.evaluate(async t => { OPENER.seek(t); await new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))); OPENER.seek(t); }, t);
  await p.screenshot({ path: `${outDir}/t${t.toFixed(2).replace('.','_')}.png` }); }
await b.close(); console.log('done');
