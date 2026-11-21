import puppeteer from 'puppeteer-core'; import { writeFile } from 'node:fs/promises';
const [out, q=''] = process.argv.slice(2);
const b = await puppeteer.launch({executablePath:'C:/Program Files/Google/Chrome/Application/chrome.exe', headless:'new'});
const p = await b.newPage(); p.on('pageerror', e => console.log('PAGEERROR', e.message));
await p.goto('file:///D:/NothingLauncher/promo/index.html?clean=1'+q, { waitUntil: 'networkidle0' });
await p.waitForFunction('window.OPENER && window.OPENER.ready');
const b64 = await p.evaluate(() => OPENER.musicWav());
await writeFile(out, Buffer.from(b64, 'base64')); await b.close(); console.log('wrote', out);
