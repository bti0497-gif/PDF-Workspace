/**
 * IPC 핸들러 - 메인 프로세스에서 렌더러의 요청을 처리
 */
import { ipcMain, dialog, BrowserWindow, shell } from 'electron'
import { readFile, writeFile } from 'node:fs/promises'

const PDF_FILTER = { name: 'PDF 문서', extensions: ['pdf'] }

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
      data: Array.from(new Uint8Array(buffer))
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
    await writeFile(filePath, new Uint8Array(data))
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
      await writeFile(result.filePath, new Uint8Array(data))
      return result.filePath
    }
  )

  /** 탐색기에서 파일 표시 */
  ipcMain.handle('shell:showInFolder', async (_e, filePath: string) => {
    shell.showItemInFolder(filePath)
    return true
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
