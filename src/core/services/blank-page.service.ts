/**
 * 빈 페이지 생성 및 삽입 서비스
 */
import { PDFDocument } from 'pdf-lib'
import { useStore } from '../../lib/store'
import { getOrLoadPdfDoc, loadPdfFromBytes } from '../../hooks/usePdfDocument'

export async function insertBlankPageAt(
  targetIndex: number,
  preset: 'match' | 'A4-portrait' | 'A4-landscape' = 'match'
): Promise<void> {
  const store = useStore.getState()
  const pages = store.pages

  let width = 595.28
  let height = 841.89

  if (preset === 'A4-portrait') {
    width = 595.28
    height = 841.89
  } else if (preset === 'A4-landscape') {
    width = 841.89
    height = 595.28
  } else {
    // match: 기준 페이지의 가로/세로 비율 복제
    const refPage = pages[targetIndex] ?? pages[store.currentPage] ?? pages[0]
    if (refPage?.width && refPage?.height) {
      width = refPage.width
      height = refPage.height
    }
  }

  const pdfDoc = await PDFDocument.create()
  pdfDoc.addPage([width, height])
  const bytes = await pdfDoc.save()

  if (!store.document) {
    const { meta, dimensions } = await loadPdfFromBytes(bytes, '새 문서.pdf', null)
    store.setDocument(meta, dimensions)
    return
  }

  const sourceId = `blank_${Date.now()}_${Math.random().toString(36).slice(2, 7)}`
  await getOrLoadPdfDoc(sourceId, bytes)

  store.insertPagesFromDocument(
    sourceId,
    '빈 페이지',
    bytes,
    1,
    targetIndex,
    { width, height }
  )
}
