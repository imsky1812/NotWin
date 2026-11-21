/* Frame-exact export: seek each frame, screenshot, pipe PNGs to ffmpeg, mux the synthesized track. */
import puppeteer from 'puppeteer-core'; import { spawn } from 'node:child_process'; import ffmpeg from 'ffmpeg-static';
const [out, q='', fps='60'] = process.argv.slice(2); const FPS=+fps;
const b = await puppeteer.launch({executablePath:'C:/Program Files/Google/Chrome/Application/chrome.exe', headless:'new'});
const p = await b.newPage(); p.on('pageerror', e => console.log('PAGEERROR', e.message));
await p.goto('file:///D:/NothingLauncher/promo/index.html?clean=1'+q, { waitUntil: 'networkidle0' });
await p.waitForFunction('window.OPENER && window.OPENER.ready');
const { W, H, D } = await p.evaluate(() => ({ W: OPENER.W, H: OPENER.H, D: OPENER.DURATION }));
await p.setViewport({ width: W, height: H });
const ff = spawn(ffmpeg, ['-y','-loglevel','error','-f','image2pipe','-framerate',String(FPS),'-i','-','-i','../out/notwin-beat.wav',
  '-af','loudnorm=I=-14:TP=-1.5:LRA=9,alimiter=limit=0.7:level=false','-c:v','libx264','-preset','slow','-crf','16','-tune','grain','-pix_fmt','yuv420p',
  '-c:a','aac','-b:a','256k','-ar','48000','-shortest','-movflags','+faststart', out], { stdio: ['pipe','inherit','inherit'] });
const N = Math.round(D*FPS); const t0=Date.now();
for (let i=0;i<N;i++){ const t=i/FPS;
  await p.evaluate(async t => { OPENER.seek(t); await new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))); OPENER.seek(t); }, t);
  const buf = await p.screenshot({ type:'png', optimizeForSpeed:true });
  if (!ff.stdin.write(buf)) await new Promise(r=>ff.stdin.once('drain',r));
  if (i%FPS===0) console.log(`${(t).toFixed(0)}s / ${D}s  (${((Date.now()-t0)/1000).toFixed(0)}s elapsed)`); }
ff.stdin.end(); await new Promise(r=>ff.on('close',r)); await b.close(); console.log('done', out);
