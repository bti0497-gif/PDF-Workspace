/**
 * Preload - contextBridge 로 안전하게 렌더러에 API 노출
 */
import { contextBridge, ipcRenderer } from 'electron'
import type { OpenFileResult, MenuEvent, UpdateCheckResult } from './shared/types'

const api = {
  /** 단일 PDF 파일 열기 */
  openFile: (): Promise<OpenFileResult | null> => ipcRenderer.invoke('dialog:openFile'),

  /** 여러 PDF 파일 선택 (합치기) */
  openMultiple: (): Promise<string[]> => ipcRenderer.invoke('dialog:openMultiple'),

  /** 저장 다이얼로그 (경로만) */
  saveFile: (defaultName?: string): Promise<string | null> =>
    ipcRenderer.invoke('dialog:saveFile', defaultName),

  /** 경로로 파일 읽기 */
  readFile: (filePath: string): Promise<number[] | Uint8Array> =>
    ipcRenderer.invoke('file:read', filePath),

  /** 경로에 파일 쓰기 */
  writeFile: (filePath: string, data: number[] | Uint8Array): Promise<boolean> =>
    ipcRenderer.invoke('file:write', filePath, data),

  /** PDF 바이트를 저장 (다이얼로그 + 쓰기 통합) */
  savePdfBytes: (data: number[] | Uint8Array, suggestedName?: string): Promise<string | null> =>
    ipcRenderer.invoke('file:savePdfBytes', data, suggestedName),

  /** PDF 인쇄 실행 */
  printPdf: (data: number[] | Uint8Array): Promise<boolean> =>
    ipcRenderer.invoke('file:printPdf', data),

  /** 탐색기에서 표시 */
  showInFolder: (filePath: string): Promise<boolean> =>
    ipcRenderer.invoke('shell:showInFolder', filePath),

  /** 한글 지원 폰트 바이트 반환 (주석 텍스트 렌더링용) */
  getKoreanFont: (): Promise<number[] | Uint8Array | null> => ipcRenderer.invoke('font:getKorean'),

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
  },

  /** 시작 시 전달된 외부 PDF 파일 경로 조회 */
  getStartupFile: (): Promise<string | null> => ipcRenderer.invoke('app:getStartupFile'),

  /** 앱 실행 중 외부 더블클릭으로 전달된 파일 경로 리스너 */
  onOpenExternalPath: (callback: (filePath: string) => void): (() => void) => {
    const listener = (_e: unknown, filePath: string) => callback(filePath)
    ipcRenderer.on('file:open-external-path', listener)
    return () => ipcRenderer.removeListener('file:open-external-path', listener)
  },

  /** 문서 변경 상태 동기화 (종료 시 저장 확인용) */
  setDocumentDirty: (dirty: boolean): void => {
    ipcRenderer.send('app:set-dirty', dirty)
  },

  /** 업데이트 수동 확인 */
  checkForUpdates: (): Promise<UpdateCheckResult> => ipcRenderer.invoke('app:checkForUpdates'),

  /** 업데이트 상태 수신 리스너 */
  onUpdaterStatus: (callback: (data: UpdateCheckResult) => void): (() => void) => {
    const listener = (_e: unknown, data: UpdateCheckResult) => callback(data)
    ipcRenderer.on('updater:status', listener)
    return () => ipcRenderer.removeListener('updater:status', listener)
  }
}

export type WorkspaceApi = typeof api

contextBridge.exposeInMainWorld('workspace', api)
