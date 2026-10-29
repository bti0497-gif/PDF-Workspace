/**
 * PDF 코어 엔진 서비스 (pdf-lib & pdfjs-dist 기반)
 * - 가상 매니페스트를 최종 PDF 바이트로 일괄 합성(Deferred Baking)
 * - 선택된 가상 페이지만 새 PDF로 추출
 * - PDF.js 북마크(Outline) 트리 파싱 및 페이지 번호 해석
 */
import { PDFDocument, degrees, rgb, StandardFonts } from 'pdf-lib'
import fontkit from '@pdf-lib/fontkit'
import { rasterizedTextSource } from './text-raster-save'
import { writePdfBookmarks } from './bookmark-persistence'
import type * as pdfjsLib from 'pdfjs-dist'
import type { VirtualPage } from '../domain/page'
import type { SourceDocument } from '../domain/document'
import type { BookmarkNode } from '../domain/bookmark'
import { generateBookmarkId } from '../domain/bookmark'
import type { Annotation } from '../../../electron/shared/types'
import type { DocumentMetadata } from '../../lib/store'

/** #RRGGBB -> pdf-lib rgb */
function hexToRgb(hex: string): ReturnType<typeof rgb> {
  const clean = hex.replace('#', '')
  const r = parseInt(clean.substring(0, 2), 16) / 255
  const g = parseInt(clean.substring(2, 4), 16) / 255
  const b = parseInt(clean.substring(4, 6), 16) / 255
  return rgb(r || 0, g || 0, b || 0)
}

/**
 * [지연 베이킹 (Deferred Baking)]
 * 가상 매니페스트(순서, 회전, 여러 소스)와 주석을 결합해 최종 단일 PDF 바이트로 1회 일괄 합성
 */
export async function bakeManifestToPdf(params: {
  pages: VirtualPage[]
  sources: Map<string, SourceDocument>
  annotations?: Annotation[]
  customFont?: Uint8Array
  metadata?: DocumentMetadata
  bookmarks?: BookmarkNode[]
}): Promise<Uint8Array> {
  const { pages, sources, annotations = [], customFont, metadata, bookmarks } = params

  if (pages.length === 0) {
    throw new Error('저장할 페이지가 없습니다.')
  }

  const dstDoc = await PDFDocument.create()
  try {
    dstDoc.registerFontkit(fontkit)
  } catch {
    /* 이미 등록된 경우 무시 */
  }

  // 필요한 소스 PDF 문서들을 캐시 로드
  const loadedSources = new Map<string, PDFDocument>()
  for (const [id, src] of sources.entries()) {
    const loaded = await PDFDocument.load(src.bytes)
    loadedSources.set(id, loaded)
  }

  // 가상 페이지 순서대로 dstDoc에 복사 및 회전/정규화/크롭 적용
  for (const vPage of pages) {
    let srcDoc = loadedSources.get(vPage.sourceDocId)
    if (!srcDoc) continue
    if (vPage.sourcePageIndex < 0 || vPage.sourcePageIndex >= srcDoc.getPageCount()) continue

    let sourceIndex = vPage.sourcePageIndex
    if (vPage.textReplacements?.length) {
      if (!customFont) throw new Error('본문 교체 저장에는 편집용 한글 폰트가 필요합니다.')
      srcDoc = await rasterizedTextSource(sources.get(vPage.sourceDocId)!.bytes, vPage)
      sourceIndex = 0
    }
    const srcPage = srcDoc.getPages()[sourceIndex]
    const { width: srcW, height: srcH } = srcPage.getSize()

    if (vPage.normalizedConfig) {
      // 조판 규격 일치화 (embedPage 기반 벡터 보존)
      let targetW = vPage.normalizedConfig.targetWidth
      let targetH = vPage.normalizedConfig.targetHeight

      const isSrcLandscape = srcW > srcH
      const isTargetLandscape = targetW > targetH
      if (vPage.normalizedConfig.autoOrientation && isSrcLandscape !== isTargetLandscape) {
        ;[targetW, targetH] = [targetH, targetW]
      }

      const emb = await dstDoc.embedPage(srcPage)
      const newPage = dstDoc.addPage([targetW, targetH])

      let drawW = targetW
      let drawH = targetH
      let offsetX = 0
      let offsetY = 0

      if (vPage.normalizedConfig.fitMode === 'letterbox') {
        const scale = Math.min(targetW / srcW, targetH / srcH)
        drawW = srcW * scale
        drawH = srcH * scale
        offsetX = (targetW - drawW) / 2
        offsetY = (targetH - drawH) / 2
      } else if (vPage.normalizedConfig.fitMode === 'fill') {
        const scale = Math.max(targetW / srcW, targetH / srcH)
        drawW = srcW * scale
        drawH = srcH * scale
        offsetX = (targetW - drawW) / 2
        offsetY = (targetH - drawH) / 2
      }

      newPage.drawPage(emb, {
        x: offsetX,
        y: offsetY,
        width: drawW,
        height: drawH
      })

      if (vPage.rotation !== 0) {
        newPage.setRotation(degrees((vPage.rotation + 360) % 360))
      }

      if (vPage.cropMargins) {
        const { left, bottom, right, top } = vPage.cropMargins
        newPage.setCropBox(
          left,
          bottom,
          Math.max(10, targetW - left - right),
          Math.max(10, targetH - top - bottom)
        )
      }
    } else {
      // 일반 복사
      const [copied] = await dstDoc.copyPages(srcDoc, [sourceIndex])
      copied.setRotation(degrees((vPage.rotation + 360) % 360))
      if (vPage.cropMargins) {
        const { left, bottom, right, top } = vPage.cropMargins
        const box = copied.getCropBox()
        copied.setCropBox(
          box.x + left,
          box.y + bottom,
          Math.max(10, box.width - left - right),
          Math.max(10, box.height - top - bottom)
        )
      }
      dstDoc.addPage(copied)
    }
  }

  const dstPages = dstDoc.getPages()
  const hasTextReplacements = pages.some(
    (p) => p.textReplacements && p.textReplacements.length > 0
  )
  const hasWatermarks = pages.some((p) => Boolean(p.watermarkConfig))
  const hasPageNumbers = pages.some((p) => Boolean(p.pageNumberConfig))

  // 폰트 임베딩 (주석, 본문 텍스트 수정, 워터마크, 페이지 번호가 있는 경우)
  let embeddedFont: Awaited<ReturnType<typeof dstDoc.embedFont>> | null = null
  if (annotations.length > 0 || hasTextReplacements || hasWatermarks || hasPageNumbers) {
    embeddedFont = customFont
      // fontkit subsetting can omit glyph outlines for this Korean TTF.
      // Embed the complete font so saved text actually renders, not only extracts.
      ? await dstDoc.embedFont(customFont, { subset: false })
      : await dstDoc.embedFont(StandardFonts.Helvetica)
  }

  const fontToUse = embeddedFont ?? (await dstDoc.embedFont(StandardFonts.Helvetica))

  // 1) 본문 텍스트 소거 및 나눔고딕 대체 합성 (Text Replacement Baking)
  if (hasTextReplacements) {
    for (let i = 0; i < pages.length; i++) {
      const vPage = pages[i]
      const page = dstPages[i]
      if (!page || !vPage.textReplacements || vPage.textReplacements.length === 0) continue

      for (const tr of vPage.textReplacements) {
        const fontSize = tr.fontSize || 12
        const whiteoutW = tr.origPdfWidth ?? tr.pdfWidth
        // Original text was removed from the explicitly consented raster source above.

        // 새 텍스트 합성
        if (tr.newText && fontToUse) {
          const textColor = hexToRgb(tr.color || '#000000')
          let textW = 0
          const supported = new Set(fontToUse.getCharacterSet())
          if ([...tr.newText].some(c => !supported.has(c.codePointAt(0)!))) throw new Error('대체 폰트에 없는 문자가 포함되어 있습니다.')
          try {
            textW = fontToUse.widthOfTextAtSize(tr.newText, fontSize)
          } catch {
            throw new Error('대체 폰트로 표현할 수 없는 문자가 포함되어 있습니다.')
          }
          if (textW > whiteoutW + 0.1) throw new Error('수정 문구가 원래 범위를 넘습니다. 문구 또는 글자 크기를 조절하세요.')

          let drawX = tr.pdfX
          if (tr.align === 'center') {
            drawX = tr.pdfX + (whiteoutW - textW) / 2
          } else if (tr.align === 'right') {
            drawX = tr.pdfX + (whiteoutW - textW)
          }

          page.drawText(tr.newText, {
            x: drawX,
            y: tr.pdfY,
            size: fontSize,
            font: fontToUse,
            color: textColor
          })
        }
      }
    }
  }

  // 2) 워터마크 및 페이지 번호 합성 (Watermark & Page Numbering Baking)
  for (let i = 0; i < pages.length; i++) {
    const vPage = pages[i]
    const page = dstPages[i]
    if (!page) continue
    const { width: pw, height: ph } = page.getSize()

    // 워터마크
    if (vPage.watermarkConfig) {
      const wm = vPage.watermarkConfig
      const color = hexToRgb(wm.color)
      const rad = (wm.rotation * Math.PI) / 180
      const cos = Math.cos(rad)
      const sin = Math.sin(rad)
      let textW = 0
      try {
        textW = fontToUse.widthOfTextAtSize(wm.text, wm.fontSize)
      } catch {
        textW = wm.fontSize * wm.text.length * 0.7
      }
      const textH = wm.fontSize

      const drawWatermarkAt = (cx: number, cy: number, scale = 1) => {
        const curSize = wm.fontSize * scale
        const curW = textW * scale
        const curH = textH * scale
        const x = cx - (curW / 2) * cos + (curH / 3) * sin
        const y = cy - (curW / 2) * sin - (curH / 3) * cos
        try {
          page.drawText(wm.text, {
            x,
            y,
            size: curSize,
            font: fontToUse,
            color,
            opacity: wm.opacity,
            rotate: degrees(wm.rotation)
          })
        } catch (e) {
          console.warn('워터마크 그리기 실패:', e)
        }
      }

      if (wm.tiled) {
        const cols = [pw / 6, pw / 2, (5 * pw) / 6]
        const rows = [ph / 6, ph / 2, (5 * ph) / 6]
        for (const cx of cols) {
          for (const cy of rows) {
            drawWatermarkAt(cx, cy, 0.7)
          }
        }
      } else {
        drawWatermarkAt(pw / 2, ph / 2, 1)
      }
    }

    // 페이지 번호
    if (vPage.pageNumberConfig) {
      const pn = vPage.pageNumberConfig
      const color = hexToRgb(pn.color)
      let textW = 0
      try {
        textW = fontToUse.widthOfTextAtSize(pn.text, pn.fontSize)
      } catch {
        textW = pn.fontSize * pn.text.length * 0.6
      }
      let x = pn.margin
      let y = pn.margin

      if (pn.position.startsWith('top')) {
        y = ph - pn.margin - pn.fontSize
      } else {
        y = pn.margin
      }

      if (pn.position.endsWith('left')) {
        x = pn.margin
      } else if (pn.position.endsWith('right')) {
        x = pw - pn.margin - textW
      } else {
        x = (pw - textW) / 2
      }

      try {
        page.drawText(pn.text, {
          x,
          y,
          size: pn.fontSize,
          font: fontToUse,
          color
        })
      } catch (e) {
        console.warn('페이지 번호 그리기 실패:', e)
      }
    }
  }

  // 3) 주석 합성 (Annotation Baking)
  if (annotations.length > 0) {
    const font = fontToUse

    for (const ann of annotations) {
      const page = dstPages[ann.pageIndex]
      if (!page) continue
      const color = hexToRgb(ann.color)
      const { height: ph } = page.getSize()

      switch (ann.type) {
        case 'highlight':
          page.drawRectangle({
            x: ann.x,
            y: ph - (ann.y ?? 0) - (ann.height ?? 0),
            width: ann.width,
            height: ann.height,
            color,
            opacity: ann.opacity !== undefined ? ann.opacity * 0.5 : 0.35
          })
          break
        case 'rectangle':
          page.drawRectangle({
            x: ann.x,
            y: ph - (ann.y ?? 0) - (ann.height ?? 0),
            width: ann.width,
            height: ann.height,
            borderColor: color,
            borderWidth: ann.strokeWidth ?? 2,
            borderOpacity: ann.opacity ?? 1,
            opacity: 0
          })
          break
        case 'textbox': {
          const fontSize = ann.fontSize ?? 14
          const text = (ann.text ?? '').trim()
          if (text) {
            page.drawText(text, {
              x: ann.x + 4,
              y: ph - (ann.y ?? 0) - fontSize,
              size: fontSize,
              font,
              color,
              opacity: ann.opacity ?? 1,
              maxWidth: Math.max(10, ann.width - 8)
            })
          }
          page.drawRectangle({
            x: ann.x,
            y: ph - (ann.y ?? 0) - (ann.height ?? 0),
            width: ann.width,
            height: ann.height,
            borderColor: color,
            borderWidth: ann.strokeWidth ?? 1,
            borderOpacity: ann.opacity ?? 1,
            opacity: 0
          })
          break
        }
        case 'drawing': {
          const pts = ann.points ?? []
          for (let i = 1; i < pts.length; i++) {
            const a = pts[i - 1]
            const b = pts[i]
            page.drawLine({
              start: { x: a.x, y: ph - a.y },
              end: { x: b.x, y: ph - b.y },
              color,
              thickness: ann.strokeWidth ?? 2,
              opacity: ann.opacity ?? 1
            })
          }
          break
        }
        case 'stamp': {
          if (ann.imageDataUrl) {
            try {
              const base64Data = ann.imageDataUrl.split(',')[1]
              if (base64Data) {
                const binStr = atob(base64Data)
                const imgBytes = new Uint8Array(binStr.length)
                for (let b = 0; b < binStr.length; b++) {
                  imgBytes[b] = binStr.charCodeAt(b)
                }
                const isJpg =
                  ann.imageDataUrl.startsWith('data:image/jpeg') ||
                  ann.imageDataUrl.startsWith('data:image/jpg')
                const embeddedImg = isJpg
                  ? await dstDoc.embedJpg(imgBytes)
                  : await dstDoc.embedPng(imgBytes)

                const w = ann.width ?? 120
                const h = ann.height ?? 60
                const x = ann.x
                const y = ph - ann.y - h
                page.drawImage(embeddedImg, {
                  x,
                  y,
                  width: w,
                  height: h,
                  opacity: ann.opacity ?? 1
                })
              }
            } catch (err) {
              console.warn('스탬프/이미지 주석 베이킹 실패:', err)
            }
          }
          break
        }
        default:
          throw new Error('지원하지 않는 주석이 있어 저장을 중단했습니다.')
      }
    }
  }

  // 4) 문서 메타데이터 (Document Properties)
  if (metadata) {
    if (metadata.title !== undefined) dstDoc.setTitle(metadata.title)
    if (metadata.author !== undefined) dstDoc.setAuthor(metadata.author)
    if (metadata.subject !== undefined) dstDoc.setSubject(metadata.subject)
    if (metadata.keywords !== undefined) {
      const kw = Array.isArray(metadata.keywords)
        ? metadata.keywords
        : metadata.keywords.split(',').map((s) => s.trim()).filter(Boolean)
      dstDoc.setKeywords(kw)
    }
    if (metadata.creator !== undefined) dstDoc.setCreator(metadata.creator)
  }

  if (bookmarks) writePdfBookmarks(dstDoc, bookmarks, pages)
  return dstDoc.save()
}

/**
 * [선택 페이지 내보내기]
 * 가상 매니페스트의 특정 인덱스 페이지들만 새 PDF 바이트로 추출
 */
export async function extractVirtualPagesToPdf(
  pages: VirtualPage[],
  sources: Map<string, SourceDocument>,
  targetIndices: number[],
  customFont?: Uint8Array
): Promise<Uint8Array> {
  const selectedPages = targetIndices
    .filter((idx) => idx >= 0 && idx < pages.length)
    .map((idx) => pages[idx])

  if (selectedPages.length === 0) {
    throw new Error('내보낼 페이지가 선택되지 않았습니다.')
  }

  return bakeManifestToPdf({
    pages: selectedPages,
    sources,
    customFont
  })
}

/**
 * [북마크 파싱]
 * PDF.js 문서 객체로부터 계층형 북마크(목차) 트리를 파싱하고 목적지 페이지 번호(0-based)를 계산
 */
export async function parsePdfBookmarks(
  pdfDoc: pdfjsLib.PDFDocumentProxy
): Promise<BookmarkNode[]> {
  try {
    const outline = await pdfDoc.getOutline()
    if (!outline || outline.length === 0) return []

    const processItem = async (item: {
      title: string
      dest?: unknown
      items?: unknown[]
    }): Promise<BookmarkNode> => {
      let targetPageIndex: number | null = null

      if (item.dest) {
        try {
          let explicitDest: unknown = item.dest
          if (typeof explicitDest === 'string') {
            explicitDest = await pdfDoc.getDestination(explicitDest)
          }
          if (Array.isArray(explicitDest) && explicitDest.length > 0) {
            const ref = explicitDest[0]
            if (typeof ref === 'object') {
              targetPageIndex = await pdfDoc.getPageIndex(ref)
            } else if (typeof ref === 'number') {
              targetPageIndex = ref
            }
          }
        } catch {
          targetPageIndex = null
        }
      }

      const children: BookmarkNode[] = []
      if (item.items && Array.isArray(item.items)) {
        for (const child of item.items) {
          children.push(await processItem(child as typeof item))
        }
      }

      return {
        id: generateBookmarkId(),
        title: item.title || '제목 없음',
        targetPageIndex,
        dest: item.dest,
        isExpanded: true,
        children
      }
    }

    const result: BookmarkNode[] = []
    for (const topItem of outline) {
      result.push(await processItem(topItem))
    }
    return result
  } catch (err) {
    console.error('북마크 파싱 실패:', err)
    return []
  }
}
