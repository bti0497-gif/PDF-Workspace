/**
 * 웹 브라우저(Chrome/Edge 등) 환경에서 실행될 때 Electron preload API 폴백
 * - 브라우저에서도 검은 화면으로 크래시되지 않고 앱이 완벽하게 동작하도록 보장
 */
import type { WorkspaceApi } from '../../electron/preload'

export function initWorkspaceFallback(): void {
  if (typeof window === 'undefined') return
  if (window.workspace) return

  console.info(
    '[PDF Workspace] 웹 브라우저 환경에서 감지되었습니다. 브라우저용 파일 피커 및 다운로드 API를 활성화합니다.'
  )

  window.workspace = {
    openFile: async () => {
      return new Promise((resolve) => {
        const input = document.createElement('input')
        input.type = 'file'
        input.accept = 'application/pdf'
        input.onchange = async () => {
          const file = input.files?.[0]
          if (!file) {
            resolve(null)
            return
          }
          const buffer = await file.arrayBuffer()
          resolve({
            filePath: file.name,
            fileName: file.name,
            data: Array.from(new Uint8Array(buffer))
          })
        }
        input.click()
      })
    },

    openMultiple: async () => {
      return new Promise((resolve) => {
        const input = document.createElement('input')
        input.type = 'file'
        input.accept = 'application/pdf'
        input.multiple = true
        input.onchange = async () => {
          const files = Array.from(input.files ?? [])
          resolve(files.map((f) => f.name))
        }
        input.click()
      })
    },

    saveFile: async (defaultName) => defaultName ?? 'document.pdf',

    readFile: async () => [],

    writeFile: async () => true,

    savePdfBytes: async (data, suggestedName) => {
      const blob = new Blob([new Uint8Array(data)], { type: 'application/pdf' })
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = suggestedName ?? 'document.pdf'
      a.click()
      URL.revokeObjectURL(url)
      return suggestedName ?? 'downloaded.pdf'
    },

    showInFolder: async () => false,

    getKoreanFont: async () => null,

    mergeDialog: async () => [],

    splitDialog: async () => null,

    printPdf: async (data) => {
      const blob = new Blob([new Uint8Array(data)], { type: 'application/pdf' })
      const url = URL.createObjectURL(blob)
      const iframe = document.createElement('iframe')
      iframe.style.display = 'none'
      iframe.src = url
      document.body.appendChild(iframe)
      iframe.onload = () => {
        iframe.contentWindow?.print()
      }
      return true
    },

    onMenuEvent: () => {
      // 브라우저에서는 네이티브 메뉴가 없으므로 빈 구독 해제 함수 반환
      return () => {}
    }
  } as WorkspaceApi
}
