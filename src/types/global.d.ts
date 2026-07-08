/**
 * 전역 window 객체에 노출된 workspace API 타입 선언
 */
import type { WorkspaceApi } from '../../electron/preload'

declare global {
  interface Window {
    workspace: WorkspaceApi
  }
}

export {}
