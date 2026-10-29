// Local regression harness. Run: node scripts/text-edit-regression.cjs
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const { buildSync }=require('esbuild');
const {createCanvas,DOMMatrix,ImageData,Path2D}=require('@napi-rs/canvas');
global.DOMMatrix=DOMMatrix;global.ImageData=ImageData;global.Path2D=Path2D;
global.document={createElement(name){assert.equal(name,'canvas');const c=createCanvas(1,1);c.toBlob=cb=>cb(new Blob([c.toBuffer('image/png')],{type:'image/png'}));return c}};
(async()=>{
 const temp=path.resolve('test-output/text-engine-test.cjs');
 buildSync({entryPoints:['src/core/services/pdf-engine.service.ts'],bundle:true,platform:'node',format:'cjs',outfile:temp,packages:'external'});
 try {
 const {bakeManifestToPdf}=require(temp);
 const {PDFDocument,degrees}=require('pdf-lib');
 const fontkit=require('@pdf-lib/fontkit');
 const pdfjs=await import('pdfjs-dist/legacy/build/pdf.mjs');
 const source=await PDFDocument.create();source.registerFontkit(fontkit);
 const fontBytes=fs.readFileSync('assets/fonts/NanumGothic-Edit.ttf');
 const font=await source.embedFont(fontBytes,{subset:false});
 const p=source.addPage([400,300]);p.drawText('ORIGINAL',{x:40,y:220,size:14,font});p.drawText('NEIGHBOR',{x:220,y:220,size:14,font});
 const p2=source.addPage([400,300]);p2.drawText('UNTOUCHED',{x:40,y:220,size:14,font});
 const bytes=await source.save();const sources=new Map([['s',{bytes}]]);
 const width=font.widthOfTextAtSize('ORIGINAL',14);
 const tr={id:'t',pageIndex:0,originalItemId:'x',pdfX:40,pdfY:220,pdfWidth:width,pdfHeight:18,vx:40,vy:66,vw:width,vh:18,origPdfWidth:width,origVw:width,originalText:'ORIGINAL',newText:'수정됨',fontSize:14,color:'#000000',safeRasterize:true};
 const pages=[{id:'1',sourceDocId:'s',sourcePageIndex:0,rotation:0,textReplacements:[tr]},{id:'2',sourceDocId:'s',sourcePageIndex:1,rotation:0}];
 const out=await bakeManifestToPdf({pages,sources,customFont:fontBytes});
 const loaded=await pdfjs.getDocument({data:out.slice()}).promise;
 const text1=(await(await loaded.getPage(1)).getTextContent()).items.map(x=>x.str).join(' ');
 const text2=(await(await loaded.getPage(2)).getTextContent()).items.map(x=>x.str).join(' ');
 assert(text1.includes('수정됨'));assert(!text1.includes('ORIGINAL'));assert(!text1.includes('NEIGHBOR'));assert(text2.includes('UNTOUCHED'));
 const ref=await PDFDocument.create();ref.registerFontkit(fontkit);const rf=await ref.embedFont(fontBytes,{subset:false});ref.addPage([400,300]).drawText('수정됨',{x:40,y:220,size:14,font:rf});
 const refBytes=await ref.save();const refLoaded=await pdfjs.getDocument({data:refBytes}).promise;
 async function renderRegion(doc){const pg=await doc.getPage(1);const cv=createCanvas(800,600);await pg.render({canvasContext:cv.getContext('2d'),viewport:pg.getViewport({scale:2}),background:'#ffffff'}).promise;return cv.getContext('2d').getImageData(80,132,130,50).data}
 const actualPixels=await renderRegion(loaded),expectedPixels=await renderRegion(refLoaded);
 let changed=0;for(let i=0;i<actualPixels.length;i++)if(actualPixels[i]!==expectedPixels[i])changed++;
 assert.equal(changed,0,'Saved Korean glyph shapes must match a full-font reference');
 fs.writeFileSync('test-output/text-edited-verified.pdf',out);await refLoaded.destroy();await loaded.destroy();
 await assert.rejects(()=>bakeManifestToPdf({pages:[{...pages[0],textReplacements:[{...tr,safeRasterize:false}]}],sources,customFont:fontBytes}),/안전 교체/);
 await assert.rejects(()=>bakeManifestToPdf({pages:[{...pages[0],textReplacements:[{...tr,newText:'THIS TEXT IS FAR TOO LONG FOR THE ORIGINAL BOX'}]}],sources,customFont:fontBytes}),/범위를 넘/);
 const del=await bakeManifestToPdf({pages:[{...pages[0],textReplacements:[{...tr,newText:''}]}],sources,customFont:fontBytes});
 const dl=await pdfjs.getDocument({data:del.slice()}).promise;assert.equal((await(await dl.getPage(1)).getTextContent()).items.length,0);await dl.destroy();
 p2.setRotation(degrees(90));p2.setCropBox(20,30,350,240);const b2=await source.save();
 const rotated=await bakeManifestToPdf({pages:[{id:'r',sourceDocId:'s',sourcePageIndex:1,rotation:90,cropMargins:{left:5,bottom:6,right:7,top:8}}],sources:new Map([['s',{bytes:b2}]])});
 const r=await PDFDocument.load(rotated);assert.equal(r.getPage(0).getRotation().angle,90);assert.deepEqual(r.getPage(0).getCropBox(),{x:25,y:36,width:338,height:226});
 console.log('PASS: Korean glyph pixel comparison; edited text searchable; original text absent; untouched page text retained; missing consent blocked; overflow blocked; deletion; absolute rotation; CropBox origin.');
 }finally{fs.rmSync(temp,{force:true})}
})().catch(e=>{console.error(e);process.exitCode=1});
