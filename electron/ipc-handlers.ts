/**
 * IPC 핸들러 - 메인 프로세스에서 렌더러의 요청을 처리
 */
import { ipcMain, dialog, BrowserWindow, shell, app } from 'electron'
import { readFile, writeFile, rename, copyFile, unlink } from 'node:fs/promises'
import { existsSync } from 'node:fs'
import { randomUUID } from 'node:crypto'
import { join, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'

const __dirname = dirname(fileURLToPath(import.meta.url))

/** 번들에 포함된 한글 폰트(NanumGothic) 경로 - dev/prod 모두 완벽 지원 */
function getFontPath(): string {
  if (app.isPackaged) {
    return join(process.resourcesPath, 'assets/fonts/NanumGothic-Edit.ttf')
  }
  return join(__dirname, '../../assets/fonts/NanumGothic-Edit.ttf')
}

const PDF_FILTER = { name: 'PDF 문서', extensions: ['pdf'] }

/** Same-directory temporary write and backup before replacing an existing PDF. */
async function writePdfSafely(filePath: string, data: number[] | Uint8Array): Promise<void> {
  const bytes = new Uint8Array(data)
  if (bytes.length < 5 || Buffer.from(bytes.subarray(0, 5)).toString() !== '%PDF-') {
    throw new Error('올바른 PDF 데이터가 아니므로 저장하지 않았습니다.')
  }
  const temp = `${filePath}.${randomUUID()}.tmp`
  try {
    await writeFile(temp, bytes, { flag: 'wx' })
    if (existsSync(filePath)) {
      const stamp = new Date().toISOString().replace(/[:.]/g, '-')
      await copyFile(filePath, `${filePath}.${stamp}.bak`)
    }
    await rename(temp, filePath)
  } finally {
    await unlink(temp).catch(() => {})
  }
}

export function registerIpcHandlers(): void {
  /** 단일 PDF 파일 열기 다이얼로그 */
  ipcMain.handle('dialog:openFile', async () => {
    const result = await dialog.showOpenDialog({
      title: 'PDF 파일 열기',
      filters: [PDF_FILTER],
      properties: ['openFile']
    })
    if (result.canceled || result.filePaths.length === 0) return null
    const filePath = result.filePaths[0]
    const buffer = await readFile(filePath)
    return {
      filePath,
      fileName: filePath.split(/[\\/]/).pop() ?? 'document.pdf',
      data: new Uint8Array(buffer)
    }
  })

  /** 여러 PDF 파일 열기 다이얼로그 (합치기 용도) */
  ipcMain.handle('dialog:openMultiple', async () => {
    const result = await dialog.showOpenDialog({
      title: 'PDF 파일 선택',
      filters: [PDF_FILTER],
      properties: ['openFile', 'multiSelections']
    })
    if (result.canceled) return []
    return result.filePaths
  })

  /** 저장 다이얼로그 */
  ipcMain.handle('dialog:saveFile', async (_e, defaultName?: string) => {
    const result = await dialog.showSaveDialog({
      title: 'PDF 저장',
      defaultPath: defaultName ?? '문서.pdf',
      filters: [PDF_FILTER]
    })
    return result.canceled ? null : result.filePath
  })

  /** 파일 경로로 읽기 */
  ipcMain.handle('file:read', async (_e, filePath: string) => {
    const buffer = await readFile(filePath)
    return Array.from(new Uint8Array(buffer))
  })

  /** Uint8Array 를 경로에 쓰기 */
  ipcMain.handle('file:write', async (_e, filePath: string, data: number[]) => {
    await writePdfSafely(filePath, data)
    return true
  })

  /** PDF 바이트를 저장 (다이얼로그 + 쓰기 통합) */
  ipcMain.handle(
    'file:savePdfBytes',
    async (_e, data: number[], suggestedName?: string) => {
      const result = await dialog.showSaveDialog({
        title: 'PDF 저장',
        defaultPath: suggestedName ?? '문서.pdf',
        filters: [PDF_FILTER]
      })
      if (result.canceled || !result.filePath) return null
      await writePdfSafely(result.filePath, data)
      return result.filePath
    }
  )

  /** 탐색기에서 파일 표시 */
  ipcMain.handle('shell:showInFolder', async (_e, filePath: string) => {
    shell.showItemInFolder(filePath)
    return true
  })

  /** PDF 인쇄 실행 (인쇄용 최신 바이트를 임시 파일로 생성 후 시스템 인쇄 및 뷰어 호출) */
  ipcMain.handle('file:printPdf', async (_e, data: number[]) => {
    try {
      const tempPath = join(app.getPath('temp'), `workspace_print_${Date.now()}.pdf`)
      await writeFile(tempPath, new Uint8Array(data))
      await shell.openPath(tempPath)
      return true
    } catch (err) {
      console.error('인쇄 실패:', err)
      return false
    }
  })

  /** 한글 지원 폰트 바이트 반환 (주석 텍스트용) */
  ipcMain.handle('font:getKorean', async () => {
    try {
      const fontPath = getFontPath()
      const buffer = await readFile(fontPath)
      return new Uint8Array(buffer)
    } catch (err) {
      console.error('폰트 로드 실패:', err)
      return null
    }
  })

  /** 합치기 다이얼로그 - 파일 선택 후 경로 배열 반환 */
  ipcMain.handle('dialog:merge', async () => {
    const result = await dialog.showOpenDialog({
      title: '합칠 PDF 파일들 선택',
      filters: [PDF_FILTER],
      properties: ['openFile', 'multiSelections']
    })
    return result.canceled ? [] : result.filePaths
  })

  /** 분할 다이얼로그 - 원본 파일 + 저장 위치 */
  ipcMain.handle(
    'dialog:split',
    async (_e, _sourcePath: string, suggestedName: string) => {
      const saveResult = await dialog.showSaveDialog({
        title: '분할된 PDF 저장 위치',
        defaultPath: suggestedName,
        filters: [PDF_FILTER]
      })
      return saveResult.canceled ? null : saveResult.filePath
    }
  )
}

/** 메뉴 이벤트를 포커스된 창의 렌더러로 전달 */
export function sendMenuEvent(win: BrowserWindow, event: string): void {
  if (win && !win.isDestroyed()) {
    win.webContents.send('menu:event', event)
  }
}
