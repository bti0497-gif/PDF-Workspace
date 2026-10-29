import { createPortal } from 'react-dom'
import { useState, useEffect, useRef } from 'react'
import type { PDFFont } from 'pdf-lib'
import { useStore } from '../lib/store'
import { getEditableTextItems, type EditableTextItem } from '../core/services/text-editor.service'
import { loadEditFont, measureEditText, fitsText } from '../core/services/text-edit-metrics'
import type { TextReplacement } from '../core/domain/page'
interface Props { pageIndex: number; zoom: number; rotation: number }
export function TextEditLayer({ pageIndex, zoom }: Props) {
  const v = useStore(s=>s.pages[pageIndex])
  const active = useStore(s=>s.activeTool)==='text-edit'
  const currentPage = useStore(s=>s.currentPage)
  const portal = document.getElementById('text-edit-notice')
  const add = useStore(s=>s.addTextReplacement)
  const remove = useStore(s=>s.removeTextReplacement)
  const [items,setItems]=useState<EditableTextItem[]>([])
  const [font,setFont]=useState<PDFFont|null>(null)
  const [error,setError]=useState('')
  const [item,setItem]=useState<EditableTextItem|null>(null)
  const [existing,setExisting]=useState<TextReplacement|null>(null)
  const [text,setText]=useState('')
  const [size,setSize]=useState(12)
  const [color,setColor]=useState('#000000')
  const [align,setAlign]=useState<'left'|'center'|'right'>('left')
  const [consent,setConsent]=useState(false)
  const input=useRef<HTMLInputElement>(null)
  const composing=useRef(false)
  useEffect(()=>{let live=true; if(v) getEditableTextItems(v).then(x=>{if(live)setItems(x)}).catch(e=>{if(live)setError(String(e))}); return()=>{live=false}},[v])
  useEffect(()=>{if(active) loadEditFont().then(f=>{setFont(f);setError('')}).catch(e=>setError(e.message))},[active])
  useEffect(()=>{setConsent(false);setItem(null)},[v?.id])
  useEffect(()=>{if(item)input.current?.focus()},[item])
  if(!v)return null
  const replacements=v.textReplacements??[]
  let measured=0
  let measureError=''
  try { if(font) measured=measureEditText(font,text,size) } catch { measureError='이 문자는 대체 폰트로 표현할 수 없습니다.' }
  const overflow=!!item && !fitsText(measured,item.pdfWidth)
  const canApply=!!font && consent && !overflow && !measureError && size>0 && !!item && size<=item.fontSize+0.01
  const start=(target:EditableTextItem,tr?:TextReplacement)=>{useStore.getState().setCurrentPage(pageIndex);setItem(target);setExisting(tr??null);setText(tr?.newText??target.str);setSize(tr?.fontSize??target.fontSize);setColor(tr?.color??'#000000');setAlign(tr?.align??'left')}
  const apply=()=>{
    if(!item||!canApply||composing.current)return
    if(existing)remove(pageIndex,existing.id)
    if(text!==item.str || size!==item.fontSize || align!=='left' || color!=='#000000') add({id:`tr_${Date.now()}`,pageIndex,originalItemId:item.id,pdfX:item.pdfX,pdfY:item.pdfY,pdfWidth:item.pdfWidth,pdfHeight:item.pdfHeight,vx:item.vx,vy:item.vy,vw:item.vw,vh:item.vh,origVw:item.vw,origPdfWidth:item.pdfWidth,originalText:item.str,newText:text,fontSize:size,align,color,backgroundColor:'#ffffff',safeRasterize:true})
    setItem(null)
  }
  return <div style={{position:'absolute',inset:0,pointerEvents:'none',zIndex:15}}>
    {active&&currentPage===pageIndex&&portal&&createPortal(<div style={{padding:'8px 16px',background:'var(--color-bg-elevated)',border:'1px solid var(--color-border)',borderRadius:6,pointerEvents:'auto',zIndex:40,fontSize:12}}>
      <strong>배치 보호 편집 · 선택 범위 밖은 고정</strong>
      <p style={{margin:'5px 0'}}>대체 글꼴: 편집용 한글 폰트. 흰 배경의 가로 문구만 지원합니다. 서로 다른 글자 조각·표의 칸은 자동으로 합치지 않습니다.</p>
      <label style={{display:'flex',gap:6,alignItems:'flex-start'}}><input type="checkbox" checked={consent} onChange={e=>setConsent(e.target.checked)}/>안전 교체 사본 저장에 동의: 수정한 페이지는 이미지화되고, 그 페이지에서는 새 문구만 검색·복사할 수 있고 기존 링크·양식도 이미지로 고정됩니다. 원본 파일은 별도로 보관하세요.</label>
      {(v.rotation%360!==0||v.normalizedConfig)&&<p role="alert">회전·조판 페이지는 좌표 보호를 위해 본문 편집을 지원하지 않습니다.</p>}
      {!items.length&&<p>안전하게 분리할 수 있는 문구가 없습니다. 스캔·겹친 글자·복잡한 공백 구간은 편집 대상에서 제외합니다.</p>}
      {error&&<p role="alert" style={{color:'#d33'}}>{error}</p>}
    </div>,portal)}
    {replacements.map(tr=><div key={tr.id} onClick={()=>{if(active){const original=items.find(x=>x.id===tr.originalItemId);if(original)start(original,tr)}}} title="수정한 문구 다시 편집" style={{position:'absolute',left:tr.vx*zoom,top:tr.vy*zoom,width:(tr.origVw??tr.vw)*zoom,height:tr.vh*zoom,background:'#fff',pointerEvents:active?'auto':'none',outline:active?'1px dashed #2580c8':'none',cursor:active?'text':'default',overflow:'hidden'}}>
      <span style={{position:'absolute',left:0,right:0,top:0,fontFamily:'WorkspaceEditFont',fontSize:tr.fontSize*zoom,lineHeight:`${tr.vh*zoom}px`,whiteSpace:'pre',textAlign:tr.align??'left',color:tr.color}}>{tr.newText}</span>
    </div>)}
    {active&&items.filter(x=>!replacements.some(r=>r.originalItemId===x.id)).map(x=><button key={x.id} aria-label={`문구 수정: ${x.str}`} title="이 문구만 수정" onClick={()=>start(x)} style={{position:'absolute',left:x.vx*zoom,top:x.vy*zoom,width:Math.max(4,x.vw*zoom),height:x.vh*zoom,padding:0,background:'transparent',border:'1px dashed #218f8b66',pointerEvents:'auto',cursor:'text'}}/>)}
    {active&&item&&<div role="dialog" aria-label="배치 보호 문구 편집" style={{position:'fixed',bottom:48,left:'50%',transform:'translateX(-50%)',width:600,maxWidth:'90vw',background:'var(--color-bg-elevated)',color:'var(--color-text)',padding:16,border:'1px solid var(--color-accent)',borderRadius:8,boxShadow:'0 8px 30px #0005',pointerEvents:'auto',zIndex:80}}>
      <strong>선택한 문구만 수정</strong><div style={{fontSize:12,margin:'7px 0'}}>원문: {item.str} · 고정 폭 {item.pdfWidth.toFixed(1)}pt · 새 문구 {measured.toFixed(1)}pt</div>
      <div style={{display:'flex',gap:10,alignItems:'center',marginBottom:8}}>
        <label>크기 <input aria-label="편집 글자 크기" type="number" min={4} max={item.fontSize} step={0.5} value={size} onChange={e=>setSize(Number(e.target.value))} style={{width:64}}/></label>
        <label>정렬 <select aria-label="편집 정렬" value={align} onChange={e=>setAlign(e.target.value as typeof align)}><option value="left">왼쪽 고정</option><option value="center">중심 고정</option><option value="right">오른쪽 고정</option></select></label>
        <input aria-label="편집 글자 색" type="color" value={color} onChange={e=>setColor(e.target.value)}/>
      </div>
      <input ref={input} aria-label="수정할 문구" value={text} onChange={e=>setText(e.target.value)} onCompositionStart={()=>{composing.current=true}} onCompositionEnd={()=>{composing.current=false}} onKeyDown={e=>{e.stopPropagation();if(composing.current||e.nativeEvent.isComposing||e.keyCode===229)return;if(e.key==='Enter'){e.preventDefault();apply()}if(e.key==='Escape')setItem(null)}} style={{width:'100%',boxSizing:'border-box',fontFamily:'WorkspaceEditFont',fontSize:18,padding:8,color,whiteSpace:'pre'}}/>
      <div style={{margin:'10px 0',fontSize:12}}>선택 범위 미리보기 · 대체 글꼴 사용</div>
      <div style={{width:item.pdfWidth*zoom,maxWidth:'100%',height:Math.max(24,item.vh*zoom),border:`1px solid ${overflow?'#d33':'#218f8b'}`,background:'#fff',color,fontFamily:'WorkspaceEditFont',fontSize:size*zoom,whiteSpace:'pre',textAlign:align,overflow:'hidden'}}>{text}</div>
      {(overflow||measureError||size>item.fontSize)&&<p role="alert" style={{color:'#d33'}}> {measureError||'선택 범위를 넘습니다. 문구를 줄이거나 글자 크기를 직접 낮춰 주세요. 주변 글자는 이동하지 않습니다.'}</p>}
      {!font&&<p>편집 폰트를 준비 중입니다. 폰트를 불러오지 못하면 적용할 수 없습니다.</p>}
      {!consent&&<p>위쪽의 안전 교체 사본 안내를 확인하고 동의해야 적용할 수 있습니다.</p>}
      <div style={{display:'flex',gap:8,justifyContent:'flex-end',marginTop:12}}>{existing&&<button onClick={()=>{remove(pageIndex,existing.id);setItem(null)}}>원문 복원</button>}<button onClick={()=>setItem(null)}>취소</button><button disabled={!canApply} onClick={apply}>{text===''?'문구 삭제 적용':'적용'}</button></div>
    </div>}
  </div>
}
