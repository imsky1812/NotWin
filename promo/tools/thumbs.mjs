/* Twitter/X thumbnails (1600x900): frames of the promo, frozen, with a few extra layers. */
import puppeteer from 'puppeteer-core'; import { mkdir } from 'node:fs/promises';
const OUT='../out/thumbs'; await mkdir(OUT,{recursive:true});
const b = await puppeteer.launch({executablePath:'C:/Program Files/Google/Chrome/Application/chrome.exe', headless:'new'});
const p = await b.newPage(); p.on('pageerror', e => console.log('PAGEERROR', e.message));
await p.setViewport({ width:1600, height:900, deviceScaleFactor:1 });
const shots = [
 ['1-hero', 46.0, () => {
   document.querySelector('#fx').style.opacity=0; ['#TAG','#LINK'].forEach(s=>document.querySelector(s).style.display='none');
   ['#dock','#notch'].forEach(s=>{const e=document.querySelector(s);e.style.opacity=1;});
   const h=document.querySelector('#hud'); const d=document.createElement('div'); d.style.cssText='position:absolute;left:0;top:0;width:1920px;height:1080px';
   d.style.cssText='position:absolute;left:0;top:0;width:1920px;height:1080px';
   const svg=window.__dot('NotWin',36,14.2);
   d.innerHTML=`<div style="position:absolute;left:50%;top:300px;transform:translateX(-50%)">${svg}</div>
     <div style="position:absolute;left:50%;top:640px;transform:translateX(-50%);font-size:58px;font-weight:500;letter-spacing:-.015em;white-space:nowrap;color:#E8E8E8">A Nothing OS-style shell for Windows 11</div>
     <div class="lab" style="position:absolute;left:80px;top:70px;font-size:30px;color:#bbb">For Windows 11</div>
     <div class="lab" style="position:absolute;right:80px;top:70px;font-size:30px;color:#bbb">v1.0.0</div>
     <div class="lab" style="position:absolute;left:50%;top:750px;transform:translateX(-50%);font-size:28px;color:#999;text-transform:none">github.com/imsky1812/NotWin</div>`;
   h.appendChild(d); }],
 ['2-notch', 28.6, () => { document.querySelector('#cursor').style.opacity=0;
   const h=document.querySelector('#hud'); const d=document.createElement('div'); d.style.cssText='position:absolute;left:0;top:0;width:1920px;height:1080px';
   d.innerHTML=`<div style="position:absolute;left:120px;top:640px;font-size:132px;font-weight:500;letter-spacing:-.02em;line-height:1;color:#E8E8E8;white-space:nowrap">A notch for your PC</div>
     <div class="lab" style="position:absolute;left:126px;top:800px;font-size:30px;color:#999">NotWin · timer · media · clipboard · notes</div>`;
   h.appendChild(d); }],
 ['3-desktop', 39.75, () => {
   const h=document.querySelector('#hud'); const d=document.createElement('div'); d.style.cssText='position:absolute;left:0;top:0;width:1920px;height:1080px';
   d.innerHTML=`<div style="position:absolute;left:60px;top:44px;background:#0d0d0d;color:#E8E8E8;border-radius:34px;padding:16px 30px;white-space:nowrap;font-size:34px;font-weight:500;letter-spacing:-.01em;box-shadow:0 10px 30px rgba(0,0,0,.25)">Nothing OS, on Windows 11</div>`;
   h.appendChild(d); }],
 ['4-launcher', 13.3, () => { document.querySelector('#cursor').style.opacity=0; const h=document.querySelector('#hud');
   const d=document.createElement('div'); d.style.cssText='position:absolute;left:0;top:0;width:1920px;height:1080px'; d.innerHTML=`<div class="lab" style="position:absolute;left:50%;top:1000px;transform:translateX(-50%);font-size:28px;color:#999">NotWin · apps · settings · files</div>`; h.appendChild(d);}],
 ['5-quick-settings', 21.6, () => { document.querySelector('#cursor').style.opacity=0; }],
];
for (const [name,t,fn] of shots){
  await p.goto('file:///D:/NothingLauncher/promo/index.html?clean=1', { waitUntil:'networkidle0' });
  await p.waitForFunction('window.OPENER && window.OPENER.ready');
  await p.evaluate(async t => { OPENER.seek(t); await new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))); OPENER.seek(t); }, t);
  await p.evaluate(() => { window.__dot=(s,pch,r)=>{ /* big dot wordmark, red tittle on the i */
     const F={N:['X...X','XX..X','X.X.X','X..XX','X...X','X...X','X...X'],o:['.....','.....','.XXX.','X...X','X...X','X...X','.XXX.'],t:['..X..','..X..','XXXXX','..X..','..X..','..X..','...XX'],W:['X...X','X...X','X...X','X.X.X','X.X.X','XX.XX','X...X'],i:['..X..','.....','.XX..','..X..','..X..','..X..','.XXX.'],n:['.....','.....','XXXX.','X...X','X...X','X...X','X...X']};
     let c='',x=0; for(const ch of s){F[ch].forEach((row,ri)=>[...row].forEach((v,k)=>{if(v==='X')c+=`<circle cx="${x+k*pch}" cy="${ri*pch}" r="${r}" fill="${ch==='i'&&ri===0?'#D71921':'#E8E8E8'}"/>`;}));x+=6*pch;}
     const w=x-2*pch; return `<svg width="${w+2*r}" height="${6*pch+2*r}" viewBox="${-r} ${-r} ${w+2*r} ${6*pch+2*r}">${c}</svg>`;}; });
  await p.evaluate(fn);
  await new Promise(r=>setTimeout(r,150));
  await p.screenshot({ path:`${OUT}/notwin-${name}.png` }); console.log(name);
}
await b.close();
