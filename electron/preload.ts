/**
 * Preload - contextBridge 로 안전하게 렌더러에 API 노출
 */
import { contextBridge, ipcRenderer } from 'electron'
import type { OpenFileResult, MenuEvent } from './shared/types'

const api = {
  /** 단일 PDF 파일 열기 */
  openFile: (): Promise<OpenFileResult | null> => ipcRenderer.invoke('dialog:openFile'),

  /** 여러 PDF 파일 선택 (합치기) */
  openMultiple: (): Promise<string[]> => ipcRenderer.invoke('dialog:openMultiple'),

  /** 저장 다이얼로그 (경로만) */
  saveFile: (defaultName?: string): Promise<string | null> =>
    ipcRenderer.invoke('dialog:saveFile', defaultName),

  /** 경로로 파일 읽기 */
  readFile: (filePath: string): Promise<number[]> =>
    ipcRenderer.invoke('file:read', filePath),

  /** 경로에 파일 쓰기 */
  writeFile: (filePath: string, data: number[]): Promise<boolean> =>
    ipcRenderer.invoke('file:write', filePath, data),

  /** PDF 바이트를 저장 (다이얼로그 + 쓰기 통합) */
  savePdfBytes: (data: number[], suggestedName?: string): Promise<string | null> =>
    ipcRenderer.invoke('file:savePdfBytes', data, suggestedName),

  /** 탐색기에서 표시 */
  showInFolder: (filePath: string): Promise<boolean> =>
    ipcRenderer.invoke('shell:showInFolder', filePath),

  /** 합치기 다이얼로그 */
  mergeDialog: (): Promise<string[]> => ipcRenderer.invoke('dialog:merge'),

  /** 분할 다이얼로그 */
  splitDialog: (sourcePath: string, suggestedName: string): Promise<string | null> =>
    ipcRenderer.invoke('dialog:split', sourcePath, suggestedName),

  /** 메뉴 이벤트 리스너 등록 */
  onMenuEvent: (handler: (event: MenuEvent) => void): (() => void) => {
    const listener = (_e: unknown, event: MenuEvent) => handler(event)
    ipcRenderer.on('menu:event', listener)
    return () => ipcRenderer.removeListener('menu:event', listener)
  }
}

export type WorkspaceApi = typeof api

contextBridge.exposeInMainWorld('workspace', api)
