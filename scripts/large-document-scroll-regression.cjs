// Synthetic large-PDF UI regression. Tests bounded work/resources, not a user's original PDF or physical wheel latency.
const {app,BrowserWindow}=require('electron')
const {PDFDocument,rgb}=require('pdf-lib')
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict')
if(!process.argv[2])throw Error('Pass isolated temporary user data')
app.setPath('userData',path.resolve(process.argv[2]));app.commandLine.appendSwitch('disable-smooth-scrolling')
let win
const delay=ms=>new Promise(r=>setTimeout(r,ms)),run=code=>win.webContents.executeJavaScript(code).catch(e=>{console.error('Failed renderer step:',code.substring(0,180));throw e})
app.whenReady().then(async()=>{
 win=new BrowserWindow({show:false,skipTaskbar:true,width:1400,height:950,webPreferences:{backgroundThrottling:false,contextIsolation:true,nodeIntegration:false}})
 await win.loadURL('http://127.0.0.1:5874/')
 for(let i=0;i<150;i++){if(await run("!!document.querySelector('.menu-bar') && !document.querySelector('.splash-screen')"))break;await delay(50)}
 await run("(async()=>{window.__perfStore=(await import('/lib/store.ts')).useStore})()")
 const pdf=await PDFDocument.create(),count=240
 for(let i=0;i<count;i++){const width=i%5===0?842:595,height=i%5===0?595:842;const p=pdf.addPage([width,height]);p.drawText('Large-document scroll sample / '+(i+1),{x:35,y:height-55,size:18});for(let j=0;j<18;j++)p.drawRectangle({x:40+(j%3)*160,y:60+Math.floor(j/3)*65,width:130,height:38,color:rgb((j%5)/6,.3,.65)})}
 const bytes=[...await pdf.save()]
 await run(`(async()=>{const x=await (await import('/hooks/usePdfDocument.ts')).loadPdfFromBytes(new Uint8Array(${JSON.stringify(bytes)}),'240-page mixed-size sample.pdf',null);window.__perfStore.getState().setDocument(x.meta,x.dimensions)})()`)
 await delay(100)
 await run("document.querySelector('button[title=\"1열 레이아웃\"]').click()")
 await delay(1200)
 await run(`window.__pageReads=0;window.__renderErrors=[];const originalRect=Element.prototype.getBoundingClientRect;Element.prototype.getBoundingClientRect=function(){if(this.classList?.contains('viewer__virtual-page'))window.__pageReads++;return originalRect.call(this)};const originalError=console.error;console.error=(...args)=>{window.__renderErrors.push(args.map(String).join(' '));originalError(...args)};void 0`)
 const results=[]
 for(const mode of ['continuous','two-page']) {
  await run(`window.__perfStore.getState().setViewMode('${mode}')`);await delay(750)
  const samples=[]
  for(let i=0;i<36;i++){
   await run(`(()=>{window.__pageReads=0;const viewer=document.querySelector('.viewer');viewer.scrollTop=(viewer.scrollHeight-viewer.clientHeight)*${i/35}})()`)
   await run('new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r)))');await delay(45)
   const state=await run(`({page:window.__perfStore.getState().currentPage,reads:window.__pageReads,viewerCanvases:document.querySelectorAll('.viewer .page-canvas').length,thumbnailCanvases:document.querySelectorAll('.sidebar .thumbnail__canvas').length,rootScroll:[scrollY,document.documentElement.scrollTop,document.body.scrollTop,document.getElementById('root').scrollTop]})`)
   assert(state.reads<80,JSON.stringify({mode,step:i,...state}));assert(state.viewerCanvases<=16,JSON.stringify(state));assert(state.thumbnailCanvases<=24,JSON.stringify(state));assert(state.rootScroll.every(n=>n===0));samples.push(state)
  }
  assert(samples.at(-1).page>=count-4,'current-page tracking must follow the bottom of the document')
  results.push({mode,samples,maxPageMeasurements:Math.max(...samples.map(s=>s.reads)),maxViewerCanvases:Math.max(...samples.map(s=>s.viewerCanvases)),maxThumbnailCanvases:Math.max(...samples.map(s=>s.thumbnailCanvases))})
 }
 // Sidebar-only scrolling must also release old canvases without changing grid extent.
 const sidebar=[]
 const extent=await run("document.querySelector('.sidebar__content').scrollHeight")
 for(let i=0;i<24;i++){
  await run(`(()=>{const s=document.querySelector('.sidebar__content');s.scrollTop=(s.scrollHeight-s.clientHeight)*${i/23}})()`);await delay(100)
  const s=await run("({canvases:document.querySelectorAll('.sidebar .thumbnail__canvas').length,height:document.querySelector('.sidebar__content').scrollHeight})")
  assert(s.canvases<=24,JSON.stringify(s));assert(Math.abs(s.height-extent)<=3,JSON.stringify({expected:extent,...s}));sidebar.push(s)
 }
 await run("document.querySelector('.sidebar__content').scrollTop=0");await delay(600)
 const revisited=await run("(()=>{const c=document.querySelector('.thumbnail[data-index=\"0\"] canvas');return !!c&&c.width>0&&c.height>0})()")
 assert(revisited,'a revisited thumbnail must get its canvas back')
 // Stable callback props must preserve modifiers and multi-selection drag state.
 const selection=await run(`(()=>{const item=i=>document.querySelector('.thumbnail[data-index="'+i+'"]');item(0).click();item(1).dispatchEvent(new MouseEvent('click',{bubbles:true,ctrlKey:true}));const ctrl=[...window.__perfStore.getState().selectedPageIndices];item(4).dispatchEvent(new MouseEvent('click',{bubbles:true,shiftKey:true}));const range=[...window.__perfStore.getState().selectedPageIndices];item(1).dispatchEvent(new MouseEvent('contextmenu',{bubbles:true,clientX:50,clientY:150}));const context=[...window.__perfStore.getState().selectedPageIndices];const dt=new DataTransfer();item(1).dispatchEvent(new DragEvent('dragstart',{bubbles:true,dataTransfer:dt}));const drag=[...window.__perfStore.getState().selectedPageIndices];item(1).dispatchEvent(new DragEvent('dragend',{bubbles:true,dataTransfer:dt}));return {ctrl,range,context,drag,payload:dt.getData('text/plain')}})()`)
 assert.deepEqual(selection.ctrl,[0,1]);assert(selection.range.includes(1)&&selection.range.includes(4));assert.deepEqual(selection.context,selection.range);assert.deepEqual(selection.drag,selection.range);assert.equal(selection.payload,'1')
 await run("document.querySelector('button[title=\"3열 레이아웃\"]').click()");await delay(800)
 const gridExtent=await run("document.querySelector('.sidebar__content').scrollHeight")
 const grids=[]
 for(const fraction of [0,.5,1,0]) {await run(`(()=>{const s=document.querySelector('.sidebar__content');s.scrollTop=(s.scrollHeight-s.clientHeight)*${fraction}})()`);await delay(250);const grid=await run("({canvases:document.querySelectorAll('.sidebar .thumbnail__canvas').length,height:document.querySelector('.sidebar__content').scrollHeight})");assert(grid.canvases<=90,JSON.stringify(grid));assert(Math.abs(grid.height-gridExtent)<=3,JSON.stringify({expected:gridExtent,...grid}));grids.push({fraction,...grid})}
 const errors=await run('window.__renderErrors');assert.deepEqual(errors,[])
 assert.equal(await run('window.__perfStore.getState().isDirty'),false,'scroll and selection must not edit the PDF')
 fs.mkdirSync('test-output',{recursive:true});fs.writeFileSync('test-output/large-document-scroll-regression.json',JSON.stringify({scope:'240-page generated mixed-size PDF; hidden Electron with smooth scrolling disabled; no claim about user-file FPS or physical input latency',results,sidebar,revisited,selection,grids,errors},null,2))
 console.log('PASS: 240 mixed-size pages, continuous/two-page scroll, bounded geometry reads and page/thumbnail canvases, stable sidebar extent, revisit rendering, no captured render errors. '+JSON.stringify(results.map(({mode,maxPageMeasurements,maxViewerCanvases,maxThumbnailCanvases})=>({mode,maxPageMeasurements,maxViewerCanvases,maxThumbnailCanvases}))))
 win.destroy();app.exit(0)
}).catch(e=>{console.error(e);if(win&&!win.isDestroyed())win.destroy();app.exit(1)})
