/** Browser preview uses real file bytes and downloads, never fake-success saves. */
import type { WorkspaceApi } from '../../electron/preload'
const files = new Map<string, File>()
function pick(multiple = false): Promise<File[]> {
  return new Promise(resolve => {
    const input = document.createElement('input')
    input.type = 'file'; input.accept = '.pdf,application/pdf'; input.multiple = multiple
    input.addEventListener('change', () => resolve(Array.from(input.files ?? [])), { once: true })
    input.addEventListener('cancel', () => resolve([]), { once: true })
    input.click()
  })
}
async function download(data: number[] | Uint8Array, name = 'document.pdf') {
  const blob = new Blob([new Uint8Array(data)], { type: 'application/pdf' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a'); a.href = url; a.download = name.split(/[\\/]/).pop() || 'document.pdf'
  a.click(); setTimeout(() => URL.revokeObjectURL(url), 10000)
  return a.download
}
export function initWorkspaceFallback(): void {
  if (typeof window === 'undefined' || window.workspace) return
  window.workspace = {
    openFile: async () => {
      const [file] = await pick(); if (!file) return null
      const key = 'browser:' + Date.now() + '/' + file.name; files.set(key, file)
      return { fileName: file.name, filePath: key, data: new Uint8Array(await file.arrayBuffer()) }
    },
    openMultiple: async () => (await pick(true)).map((file, i) => {
      const key = 'browser:' + Date.now() + ':' + i + '/' + file.name; files.set(key, file); return key
    }),
    readFile: async (key) => {
      const file = files.get(key); if (!file) throw new Error('브라우저에서는 파일을 다시 선택해야 합니다.')
      return new Uint8Array(await file.arrayBuffer())
    },
    saveFile: async (name) => name ?? 'document.pdf',
    writeFile: async (name, data) => { await download(data, name); return true },
    savePdfBytes: download,
    showInFolder: async () => false,
    getKoreanFont: async () => {
      const res = await fetch('./fonts/NanumGothic-Edit.ttf'); if (!res.ok) return null
      return new Uint8Array(await res.arrayBuffer())
    },
    mergeDialog: async () => [], splitDialog: async () => null,
    printPdf: async (data) => {
      const url = URL.createObjectURL(new Blob([new Uint8Array(data)], { type: 'application/pdf' }))
      window.open(url, '_blank', 'noopener'); setTimeout(() => URL.revokeObjectURL(url), 60000); return true
    },
    onMenuEvent: () => () => {}, getStartupFile: async () => null,
    onOpenExternalPath: () => () => {}, setDocumentDirty: () => {},
    checkForUpdates: async () => ({ status: 'web', message: '브라우저 미리보기에서는 업데이트를 지원하지 않습니다.' }),
    getUpdateStatus: async () => ({ status: 'web', readyToInstall: false }),
    installUpdate: async () => ({ status: 'web', message: '설치된 Windows 앱에서만 업데이트할 수 있습니다.' }),
    onUpdateSafetyCheck: () => () => {},
    onUpdaterStatus: () => () => {}
  } as WorkspaceApi
}
