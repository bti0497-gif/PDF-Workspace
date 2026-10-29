const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict')
const out=path.resolve('test-output/nearest-page-test.cjs')
require('esbuild').buildSync({entryPoints:['src/lib/nearest-page.ts'],outfile:out,bundle:true,platform:'node',format:'cjs'})
try {
 const {nearestPageByRow}=require(out)
 let checks=0;const measurements=[]
 for(const count of [0,1,2,3,12,100,501,2000]) for(const columns of [1,2]) {
  const mids=[];let top=24
  for(let i=0;i<count;i+=columns){let rowHeight=0;for(let j=i;j<Math.min(count,i+columns);j++){const h=200+(j*137)%1100;mids.push(top+h/2);rowHeight=Math.max(rowHeight,h)}top+=rowHeight+24}
  let maximumReads=0
  for(let n=0;n<150;n++) {
   const y=(n/149)*(top+200)-100;let calls=0
   const actual=nearestPageByRow(count,columns,y,i=>{calls++;return mids[i]})
   let expected=0,best=Infinity;for(let i=0;i<count;i++){const d=Math.abs(mids[i]-y);if(d<best){best=d;expected=i}}
   assert.equal(actual,expected,JSON.stringify({count,columns,y,actual,expected}))
   const bound=count===0?0:columns*(Math.ceil(Math.log2(Math.ceil(count/columns)))+3)
   assert(calls<=bound,`Measured ${calls} pages, expected <=${bound}`);maximumReads=Math.max(maximumReads,calls);checks++
  }
  measurements.push({pages:count,columns,legacyReadsPerScroll:count,maximumReads})
 }
 assert.equal(nearestPageByRow(2,1,100,i=>i?150:50),0,'ties keep earlier page')
 fs.writeFileSync('test-output/scroll-index-regression.json',JSON.stringify({scope:'Deterministic geometry/complexity checks, not real-document frame-rate measurements',checks,measurements},null,2))
 console.log('PASS: '+checks+' variable-height single/two-page cases match full scan; logarithmic page measurements. '+JSON.stringify(measurements.filter(m=>m.pages===2000)))
} finally {fs.rmSync(out,{force:true})}
