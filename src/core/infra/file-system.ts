/**
 * Infra 계층 - Electron IPC 래퍼
 *
 * Service/ViewModel이 window.workspace 에 직접 접근하지 않도록 캡슐화.
 * 인터페이스 기반으로 작성해 테스트 시 목 구현으로 교체 가능.
 */
import type { OpenFileResult } from '../../../electron/shared/types'

/** 파일 시스템 포트 (추상) */
export interface FileSystemPort {
  /** 단일 PDF 파일 열기 다이얼로그 + 읽기 */
  openPdf(): Promise<OpenFileResult | null>
  /** 여러 PDF 파일 선택 (합치기) */
  openMultiplePdf(): Promise<string[]>
  /** 파일 경로로 읽기 (바이트 배열) */
  readBytes(filePath: string): Promise<Uint8Array>
  /** 경로에 바이트 쓰기 */
  writeBytes(filePath: string, data: Uint8Array): Promise<void>
  /** PDF 저장 다이얼로그 + 쓰기 통합 */
  savePdf(data: Uint8Array, suggestedName?: string): Promise<string | null>
  /** 한글 폰트 바이트 로드 */
  loadKoreanFont(): Promise<Uint8Array | null>
  /** 탐색기에서 표시 */
  showInFolder(filePath: string): Promise<void>
}

/**
 * Electron preload API 기반 구현체
 * window.workspace 가 노출한 메서드를 그대로 호출 (단, 타입 변환 수행)
 */
class ElectronFileSystem implements FileSystemPort {
  async openPdf(): Promise<OpenFileResult | null> {
    const result = await window.workspace.openFile()
    if (!result) return null
    return {
      ...result,
      data: result.data // number[] 유지 (OpenFileResult.data 타입)
    }
  }

  async openMultiplePdf(): Promise<string[]> {
    return window.workspace.openMultiple()
  }

  async readBytes(filePath: string): Promise<Uint8Array> {
    const data = await window.workspace.readFile(filePath)
    return new Uint8Array(data)
  }

  async writeBytes(filePath: string, data: Uint8Array): Promise<void> {
    await window.workspace.writeFile(filePath, Array.from(data))
  }

  async savePdf(data: Uint8Array, suggestedName?: string): Promise<string | null> {
    return window.workspace.savePdfBytes(Array.from(data), suggestedName)
  }

  async loadKoreanFont(): Promise<Uint8Array | null> {
    try {
      const fontData = await window.workspace.getKoreanFont()
      return fontData ? new Uint8Array(fontData) : null
    } catch {
      return null
    }
  }

  async showInFolder(filePath: string): Promise<void> {
    await window.workspace.showInFolder(filePath)
  }
}

/** 기본 파일 시스템 인스턴스 (싱글톤) */
let _instance: FileSystemPort | null = null

export function getFileSystem(): FileSystemPort {
  if (!_instance) _instance = new ElectronFileSystem()
  return _instance
}

/** 테스트/교체용 인스턴스 주입 */
export function setFileSystem(port: FileSystemPort): void {
  _instance = port
}
