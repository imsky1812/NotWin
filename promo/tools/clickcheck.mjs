/* For every click, report which element sits under the cursor tip, in the given aspect ratio. */
import puppeteer from 'puppeteer-core';
const q = process.argv[2] || '';
const b = await puppeteer.launch({executablePath:'C:/Program Files/Google/Chrome/Application/chrome.exe', headless:'new'});
const p = await b.newPage(); p.on('pageerror', e => console.log('PAGEERROR', e.message));
await p.goto('file:///D:/NothingLauncher/promo/index.html?clean=1'+q, { waitUntil:'networkidle0' });
await p.waitForFunction('window.OPENER && window.OPENER.ready');
const {W,H} = await p.evaluate(() => ({W:OPENER.W, H:OPENER.H})); await p.setViewport({width:W, height:H});
const times = [7.5,16.0,19.5,20.5,21.0,21.74,22.0,22.49,23.0,27.5,28.25,28.99,29.125,31.0,41.0,42.0];
for (const t of times) {
  const r = await p.evaluate(async t => {
    OPENER.seek(t); await new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))); OPENER.seek(t);
    const c = document.querySelector('#cursor'); const cr = c.getBoundingClientRect(); const x = cr.left+2, y = cr.top+2;
    const rg=document.querySelector('#ring'); c.style.visibility = rg.style.visibility = 'hidden'; const el = document.elementFromPoint(x, y); c.style.visibility = rg.style.visibility = '';
    const inView = x>=0 && y>=0 && x<innerWidth && y<innerHeight;
    const host = el && el.closest('[id]');
    return `${t}s screen(${x.toFixed(0)},${y.toFixed(0)}) ${inView?'':'OFFSCREEN '}-> ${el?el.tagName.toLowerCase()+(el.className&&typeof el.className==='string'?'.'+el.className:''):'none'} in #${host?host.id:'?'}`;
  }, t);
  console.log(r);
}
await b.close();
