// Real React/PDF rendering in an isolated hidden Electron window. No installed-app data or physical monitors.
const { app, BrowserWindow } = require('electron')
const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict')
const { PDFDocument } = require('pdf-lib')
if (!process.argv[2]) throw Error('Pass an isolated temporary user-data directory')
app.setPath('userData', path.resolve(process.argv[2]))
// Hidden windows do not reliably advance compositor scroll animations. Test offsets, not animation timing.
app.commandLine.appendSwitch('disable-smooth-scrolling')
const probeOnly = process.argv.includes('--probe')
let win
const delay = ms => new Promise(r => setTimeout(r, ms))
const run = code => win.webContents.executeJavaScript(code).catch(e => { console.error('Failed renderer step:',code.substring(0,160)); throw e })
const settle = () => run('new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))')
const measure = () => run(`(() => {
 const bar=document.querySelector('.menu-bar'), header=document.querySelector('.smart-header'), viewer=document.querySelector('.viewer'), empty=document.querySelector('.viewer__empty');
 const b=bar.getBoundingClientRect(), v=viewer.getBoundingClientRect(), e=empty?.getBoundingClientRect(), ribbon=document.querySelector('.ribbon-flyout__inner');
 return { viewport:[innerWidth,innerHeight], headerTop:header.getBoundingClientRect().top, headerBottom:header.getBoundingClientRect().bottom, menuHeight:b.height,
  rootScroll:[scrollY,document.documentElement.scrollTop,document.body.scrollTop,document.getElementById('root').scrollTop],
  menuOverflow:[bar.scrollWidth-bar.clientWidth,bar.scrollHeight-bar.clientHeight], sidebar:!!document.querySelector('.sidebar'), ribbon:!!ribbon,
  pinned:header.classList.contains('smart-header--pinned'), preference:localStorage.getItem('pdf_ribbon_pinned'),
  viewer:[v.left,v.top,v.width,v.height], emptyCenterError:e?Math.abs(e.left+e.width/2-innerWidth/2):null,
  ribbonOverflow:ribbon?ribbon.scrollWidth-ribbon.clientWidth:0,
  clipped:[...bar.querySelectorAll('button')].filter(el=>{const r=el.getBoundingClientRect();return r.top < b.top-.5 || r.bottom > b.bottom+.5 || r.left < b.left-.5 || r.right > b.right+.5}).map(el=>el.title||el.textContent.trim()) }
})()`)
function assertHeader(s) {
 assert(s.headerTop>=-.5, JSON.stringify(s)); assert(s.rootScroll.every(n=>n===0),JSON.stringify(s))
 assert.deepEqual(s.clipped,[],JSON.stringify(s)); assert(s.menuOverflow.every(n=>n<=1),JSON.stringify(s)); assert(s.ribbonOverflow<=1,JSON.stringify(s))
}
app.whenReady().then(async () => {
 win=new BrowserWindow({show:false,skipTaskbar:true,width:1280,height:800,webPreferences:{backgroundThrottling:false,contextIsolation:true,nodeIntegration:false}})
 await win.loadURL('http://127.0.0.1:5874/')
 for(let i=0;i<150;i++){if(await run("!!document.querySelector('.menu-bar')"))break;await delay(50)}
 await run("localStorage.clear();localStorage.setItem('pdf_ribbon_pinned','true');localStorage.setItem('pdf_recent_files',JSON.stringify([{fileName:'Layout sample.pdf',filePath:'C:/Samples/Layout sample.pdf',pageCount:12,openedAt:Date.now()}]));location.reload()")
 for(let i=0;i<150;i++){await delay(50);if(await run("!!document.querySelector('.menu-bar') && !document.querySelector('.splash-screen')"))break}
 await run("(async()=>{window.__layoutStore=(await import('/lib/store.ts')).useStore})()")
 await settle()
 const results=[]
 const start=await measure();results.push({phase:'start',...start});assert(!start.ribbon&&!start.sidebar);assert(start.emptyCenterError<=1);assertHeader(start)
 await run("document.querySelectorAll('.menu-tab-btn')[0].click()")
 await settle();const flyout=await measure();assert(flyout.ribbon&&!flyout.pinned);assert.equal(flyout.headerBottom,start.headerBottom);assert.equal(flyout.preference,'true')
 await run("document.querySelector('.viewer__empty-title').dispatchEvent(new MouseEvent('pointerdown',{bubbles:true}))")
 await settle();assert(!(await measure()).ribbon)
 const pdf=await PDFDocument.create();for(let i=0;i<12;i++){const p=pdf.addPage([595,842]);p.drawText('Layout sample / page '+(i+1),{x:60,y:760,size:22})}
 const bytes=[...await pdf.save()]
 await run(`(async()=>{window.__layoutFixture=await (await import('/hooks/usePdfDocument.ts')).loadPdfFromBytes(new Uint8Array(${JSON.stringify(bytes)}),'Layout sample.pdf',null);window.__layoutStore.getState().setDocument(window.__layoutFixture.meta,window.__layoutFixture.dimensions)})()`)
 await delay(800);await settle();const opened=await measure();assert(opened.ribbon&&opened.sidebar&&opened.pinned)
 await run('window.__layoutStore.getState().setCurrentPage(8)');await delay(850)
 const afterNavigation=await measure();results.push({phase:'page-navigation',...afterNavigation})
 if(probeOnly){console.log(JSON.stringify(results,null,2));win.destroy();app.exit(0);return}
 assertHeader(afterNavigation)
 const navigation=await run(`(() => { const v=document.querySelector('.viewer'),p=v.querySelector('[data-page="8"]'),c=document.querySelector('.sidebar__content'),t=c.querySelector('[data-index="8"]'),vr=v.getBoundingClientRect(),cr=c.getBoundingClientRect(),tr=t.getBoundingClientRect(); return {viewerScroll:v.scrollTop,pageTopError:Math.abs(p.getBoundingClientRect().top-vr.top),thumbVisible:tr.bottom>cr.top&&tr.top<cr.bottom} })()`)
 assert(navigation.viewerScroll>0&&navigation.pageTopError<=2&&navigation.thumbVisible,JSON.stringify(navigation))
 // Synthetic oversized shell: focus/scroll requests must not move outer roots.
 // This is a guard test, not a reproduction of a physical monitor transition.
 await run(`document.querySelector('.app').style.minHeight=(innerHeight+120)+'px';document.querySelector('[data-page="8"]').scrollIntoView({block:'start'});for(const e of [document.documentElement,document.body,document.getElementById('root')])e.scrollTop=80;window.scrollTo(0,80)`)
 await settle();const guarded=await measure();assertHeader(guarded);results.push({phase:'synthetic-outer-scroll-guard',...guarded})
 await run("document.querySelector('.app').style.minHeight=''");await settle()
 const cases=[[1920,1080,1],[1000,650,1],[600,400,1],[3840,2160,1],[3840,2160,2],[2560,1440,1.5],[1920,1080,1.25],[1920,1080,1.5],[1280,720,1],[800,600,1],[1100,650,1],[1920,1080,1]]
 for(const [w,h,z] of cases){win.setContentSize(w,h);win.webContents.setZoomFactor(z);await settle();await run('window.__layoutStore.getState().setCurrentPage(window.__layoutStore.getState().currentPage===8?2:8)');await delay(750);const s=await measure();assertHeader(s);results.push({phase:'resize-and-navigation',size:[w,h],zoom:z,...s})}
 await run("document.querySelector('.menu-bar__ribbon-toggle').click()")
 await settle();assert(!(await measure()).ribbon)
 await run("window.__layoutStore.setState({document:{...window.__layoutStore.getState().document,fileName:'Renamed.pdf'}})")
 await settle();assert(!(await measure()).ribbon,'metadata change must not reopen ribbon')
 // Use the real clean-document close control, not a direct store reset.
 await run("[...document.querySelectorAll('.document-workspace-bar button')].find(e=>e.textContent==='문서 닫기').click()")
 await settle();const closed=await measure();assert(!closed.ribbon&&!closed.sidebar);assert.equal(closed.preference,'true');assert(closed.emptyCenterError<=1);assertHeader(closed);results.push({phase:'closed',...closed})
 await run('window.__layoutStore.getState().setDocument(window.__layoutFixture.meta,window.__layoutFixture.dimensions)');await settle();assert((await measure()).ribbon)
 await run("document.querySelector('.ribbon-pin-btn').click()")
 await run("[...document.querySelectorAll('.document-workspace-bar button')].find(e=>e.textContent==='문서 닫기').click()")
 await settle();assert.equal((await measure()).preference,'false')
 await run('window.__layoutStore.getState().setDocument(window.__layoutFixture.meta,window.__layoutFixture.dimensions)');await settle();assert(!(await measure()).ribbon,'auto-hide preference preserved')
 await run("[...document.querySelectorAll('.document-workspace-bar button')].find(e=>e.textContent==='문서 닫기').click()")
 await settle()
 // Long recent list on a short screen stays scrollable without clipping the top/header.
 await run("localStorage.setItem('pdf_recent_files',JSON.stringify(Array.from({length:8},(_,i)=>({fileName:'Sample '+i+'.pdf',filePath:'C:/Samples/'+i+'.pdf',pageCount:12,openedAt:Date.now()}))));location.reload()")
 win.setContentSize(600,400)
 for(let i=0;i<150;i++){await delay(50);if(await run("!!document.querySelector('.viewer__empty') && !document.querySelector('.splash-screen')"))break}
 await settle();const short=await measure();assertHeader(short);assert(short.emptyCenterError<=7);const emptyTop=await run("document.querySelector('.viewer__empty-icon').getBoundingClientRect().top");assert(emptyTop>=short.viewer[1]);results.push({phase:'short-recent-list',...short})
 win.setContentSize(1280,800);await run("localStorage.setItem('pdf_recent_files','[]');location.reload()")
 for(let i=0;i<150;i++){await delay(50);if(await run("!!document.querySelector('.viewer__empty') && !document.querySelector('.splash-screen')"))break}
 await settle();await delay(400)
 const screenshotArg=process.argv.find(a=>a.startsWith('--screenshot='));if(screenshotArg){fs.writeFileSync(screenshotArg.substring(13),(await win.webContents.capturePage()).toPNG())}
 fs.mkdirSync('test-output',{recursive:true});fs.writeFileSync('test-output/empty-layout-regression.json',JSON.stringify({scope:'Isolated hidden Electron; real React and generated PDF; smooth scrolling disabled for deterministic offsets; physical monitor movement/native installation not tested',results},null,2))
 console.log('PASS: initial/closed centered screen, temporary menu flyout, pin preference preservation, metadata stability, actual document close, long recent list, 12 resize/zoom + PDF-navigation cases with zero outer scrolling.')
 win.destroy();app.exit(0)
}).catch(e=>{console.error(e);if(win&&!win.isDestroyed())win.destroy();app.exit(1)})
