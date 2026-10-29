/**
 * Electron 메인 프로세스 진입점 (정밀 진단 로그 포함)
 */
import { app, BrowserWindow, shell, screen, nativeImage, Menu, ipcMain, dialog } from 'electron'
import { join, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'
import { existsSync } from 'node:fs'
import { randomUUID } from 'node:crypto'
import { createUpdateController } from './update-controller'
import updater from 'electron-updater'
const { autoUpdater } = updater
import { registerIpcHandlers } from './ipc-handlers'
import { attachMenu } from './menu'

console.log('[DIAGNOSTIC] === Electron Main Process Starting ===')
console.log(`[DIAGNOSTIC] Node: ${process.version}, Electron: ${process.versions.electron}, Chrome: ${process.versions.chrome}`)
console.log(`[DIAGNOSTIC] PID: ${process.pid}, Platform: ${process.platform}, Arch: ${process.arch}`)

const __dirname = dirname(fileURLToPath(import.meta.url))

let mainWindow: BrowserWindow | null = null

// 외부에서 더블클릭하여 앱이 실행되었을 때 전달된 .pdf 인자 파싱
function extractPdfPathFromArgs(args: string[]): string | null {
  for (let i = 1; i < args.length; i++) {
    const arg = args[i]
    if (arg.startsWith('--')) continue
    const cleanArg = arg.replace(/^"|"$/g, '')
    if (cleanArg.toLowerCase().endsWith('.pdf') && existsSync(cleanArg)) {
      return cleanArg
    }
  }
  return null
}

let startupPdfPath: string | null = extractPdfPathFromArgs(process.argv)
let isDocumentDirty = false

// 단일 인스턴스 락: 이미 실행 중인 경우 포커스 및 중복 파일 열기 전달
const gotTheLock = app.requestSingleInstanceLock()
if (!gotTheLock) {
  console.log('[DIAGNOSTIC] Another instance is already running. Quitting...')
  app.quit()
} else {
  app.on('second-instance', (_event, commandLine) => {
    console.log('[DIAGNOSTIC] second-instance triggered with args:', commandLine)
    if (mainWindow && !mainWindow.isDestroyed()) {
      if (mainWindow.isMinimized()) mainWindow.restore()
      mainWindow.focus()
      const pdfPath = extractPdfPathFromArgs(commandLine)
      if (pdfPath) {
        console.log('[DIAGNOSTIC] Sending file:open-external-path to renderer:', pdfPath)
        mainWindow.webContents.send('file:open-external-path', pdfPath)
      }
    }
  })
}

// Ask the live renderer after input is locked; missing replies must never permit install.
async function canInstallUpdate(): Promise<boolean> {
  const window = mainWindow
  if (!window || window.isDestroyed() || isDocumentDirty || window.webContents.isLoadingMainFrame()) return false
  return new Promise((resolve) => {
    const id = randomUUID()
    const finish = (safe: boolean) => {
      clearTimeout(timer)
      ipcMain.removeListener('updater:safety-result', onResult)
      resolve(safe)
    }
    const onResult = (event: Electron.IpcMainEvent, requestId: string, safe: boolean) => {
      if (event.sender === window.webContents && requestId === id) finish(safe === true && !isDocumentDirty)
    }
    const timer = setTimeout(() => finish(false), 3000)
    ipcMain.on('updater:safety-result', onResult)
    try { window.webContents.send('updater:check-safe', id) } catch { finish(false) }
  })
}
const updates = createUpdateController({
  updater: autoUpdater,
  isPackaged: () => app.isPackaged,
  emit: (state) => {
    if (mainWindow && !mainWindow.isDestroyed()) mainWindow.webContents.send('updater:status', state)
  },
  canInstall: canInstallUpdate,
  confirmInstall: async () => {
    if (!mainWindow || mainWindow.isDestroyed()) return false
    const result = await dialog.showMessageBox(mainWindow, {
      type: 'info', buttons: ['재시작하여 업데이트', '나중에'], defaultId: 1, cancelId: 1,
      title: '더존 PDF 편집기 업데이트',
      message: '저장된 문서를 닫고 새 버전으로 재시작합니다. 지금 적용할까요?',
      detail: '일반 종료만으로 업데이트가 자동 설치되지는 않습니다.'
    })
    return result.response === 0
  },
  lockInteraction: (locked) => {
    if (mainWindow && !mainWindow.isDestroyed()) mainWindow.setEnabled(!locked)
  }
})

function createWindow(): void {
  // 디스플레이 정보 진단 및 현재 마우스 커서 위치 기준 디스플레이 선택 (듀얼 모니터 완벽 지원)
  const displays = screen.getAllDisplays()
  console.log(`[DIAGNOSTIC] Detected ${displays.length} display(s):`)
  displays.forEach((d, idx) => {
    console.log(`  Display #${idx} ID: ${d.id} | Bounds: ${JSON.stringify(d.bounds)} | WorkArea: ${JSON.stringify(d.workArea)} | Scale: ${d.scaleFactor}`)
  })

  const cursorPoint = screen.getCursorScreenPoint()
  const activeDisplay = screen.getDisplayNearestPoint(cursorPoint)
  console.log(`[DIAGNOSTIC] Cursor at (${cursorPoint.x}, ${cursorPoint.y}) -> Active Display ID: ${activeDisplay.id} (${activeDisplay.bounds.width}x${activeDisplay.bounds.height})`)

  const workArea = activeDisplay.workArea
  const targetWidth = Math.min(1100, workArea.width - 60)
  const targetHeight = Math.min(650, workArea.height - 60)
  const posX = workArea.x + Math.max(0, Math.round((workArea.width - targetWidth) / 2))
  const posY = workArea.y + Math.max(0, Math.round((workArea.height - targetHeight) / 2))

  console.log(`[DIAGNOSTIC] Creating BrowserWindow with size: ${targetWidth}x${targetHeight} at (${posX}, ${posY})`)

  let windowIcon: string | undefined = undefined
  const possibleIcons = [
    join(__dirname, '../../build/icon.png'),
    join(__dirname, '../../build/icon.ico'),
    join(process.cwd(), 'build/icon.png'),
    join(process.cwd(), 'build/icon.ico')
  ]
  for (const p of possibleIcons) {
    if (existsSync(p)) {
      windowIcon = p
      break
    }
  }

  mainWindow = new BrowserWindow({
    width: targetWidth,
    height: targetHeight,
    x: posX,
    y: posY,
    minWidth: 600,
    minHeight: 400,
    show: false,
    autoHideMenuBar: true,
    title: '더존 PDF 편집기',
    icon: windowIcon,
    backgroundColor: '#1e1e1e',
    webPreferences: {
      preload: join(__dirname, '../preload/index.mjs'),
      sandbox: false,
      contextIsolation: true,
      nodeIntegration: false
    }
  })

  // 중복되는 윈도우 기본 흰색 Win32 메뉴바 제거 (하단 리본 및 툴바 컴포넌트로 통합)
  mainWindow.removeMenu()

  let hwndHex = 'UNKNOWN'
  try {
    const handleBuf = mainWindow.getNativeWindowHandle()
    const hwnd = handleBuf.readUInt32LE ? handleBuf.readUInt32LE(0) : handleBuf.readBigUInt64LE(0)
    hwndHex = '0x' + hwnd.toString(16).toUpperCase()
  } catch (err) {
    hwndHex = `ERR: ${err}`
  }
  console.log(`[DIAGNOSTIC] BrowserWindow Created! ID: ${mainWindow.id}, Native HWND: ${hwndHex}`)

  // 3초 후 강제 표시 타임아웃 (ready-to-show 지연/누락 방지)
  const forceShowTimer = setTimeout(() => {
    if (mainWindow && !mainWindow.isDestroyed()) {
      if (!mainWindow.isVisible()) {
        console.log('[DIAGNOSTIC WARNING] ready-to-show did not fire in 3s! Forcing mainWindow.show() now!')
        mainWindow.maximize()
        mainWindow.show()
        mainWindow.focus()
      } else {
        console.log('[DIAGNOSTIC] 3s check: Window is already visible.')
      }
    }
  }, 3000)

  // 윈도우 라이프사이클 이벤트 진단
  mainWindow.once('ready-to-show', () => {
    clearTimeout(forceShowTimer)
    console.log('[DIAGNOSTIC EVENT] === ready-to-show fired! ===')
    console.log(`[DIAGNOSTIC] Maximizing and calling mainWindow.show() and focus()...`)
    mainWindow?.maximize()
    mainWindow?.show()
    mainWindow?.focus()
    console.log(`[DIAGNOSTIC] Post-show isVisible: ${mainWindow?.isVisible()}, isMaximized: ${mainWindow?.isMaximized()}`)
  })

  mainWindow.on('show', () => console.log('[DIAGNOSTIC EVENT] Window "show" triggered'))
  mainWindow.on('hide', () => console.log('[DIAGNOSTIC EVENT] Window "hide" triggered'))
  mainWindow.on('focus', () => console.log('[DIAGNOSTIC EVENT] Window "focus" gained'))
  mainWindow.on('blur', () => console.log('[DIAGNOSTIC EVENT] Window "blur" (focus lost)'))
  mainWindow.on('minimize', () => console.log('[DIAGNOSTIC EVENT] Window "minimize" triggered'))
  mainWindow.on('restore', () => console.log('[DIAGNOSTIC EVENT] Window "restore" triggered'))
  mainWindow.on('close', (e) => {
    console.log('[DIAGNOSTIC EVENT] Window "close" initiated')
    if (isDocumentDirty) {
      const choice = dialog.showMessageBoxSync(mainWindow!, {
        type: 'warning',
        buttons: ['저장하지 않고 종료', '취소'],
        defaultId: 1,
        cancelId: 1,
        title: '더존 PDF 편집기 - 저장 확인',
        message: '문서에 저장하지 않은 편집 내용이 있습니다.\n저장하지 않고 바로 종료하시겠습니까?'
      })
      if (choice === 1) {
        e.preventDefault()
      }
    }
  })
  mainWindow.on('closed', () => {
    console.log('[DIAGNOSTIC EVENT] Window "closed"!')
    mainWindow = null
  })

  // 웹 콘텐츠 라이프사이클 진단
  mainWindow.webContents.on('dom-ready', () => {
    console.log('[DIAGNOSTIC EVENT] WebContents "dom-ready"')
  })
  mainWindow.webContents.on('did-finish-load', () => {
    console.log('[DIAGNOSTIC EVENT] WebContents "did-finish-load"')
  })
  mainWindow.webContents.on('render-process-gone', (_event, details) => {
    console.error('[DIAGNOSTIC CRITICAL] Renderer process gone:', details)
  })
  mainWindow.webContents.on('unresponsive', () => {
    console.warn('[DIAGNOSTIC WARNING] Renderer process is unresponsive!')
  })

  mainWindow.webContents.on('console-message', (_event, level, message, line, sourceId) => {
    console.log(`[Renderer Console ${level}] ${message} (${sourceId}:${line})`)
  })

  mainWindow.webContents.on('did-fail-load', (_event, errorCode, errorDescription, validatedURL) => {
    console.error(`[Renderer Fail Load] ${errorCode}: ${errorDescription} (${validatedURL})`)
  })

  mainWindow.webContents.setWindowOpenHandler((details) => {
    shell.openExternal(details.url)
    return { action: 'deny' }
  })

  // dev: HMR 서버, prod: 빌드 결과 파일
  if (process.env['ELECTRON_RENDERER_URL']) {
    console.log('[DIAGNOSTIC] Loading dev URL:', process.env['ELECTRON_RENDERER_URL'])
    mainWindow.loadURL(process.env['ELECTRON_RENDERER_URL'])

    mainWindow.webContents.on('before-input-event', (event, input) => {
      if (input.key === 'F12' && input.type === 'keyDown') {
        mainWindow?.webContents.toggleDevTools()
        event.preventDefault()
      }
    })
  } else {
    const filePath = join(__dirname, '../renderer/index.html')
    console.log('[DIAGNOSTIC] Loading file:', filePath)
    mainWindow.loadFile(filePath)
  }

  // 주기적 상태 진단 하트비트 (3초 간격 5회)
  let heartbeatCount = 0
  const heartbeatTimer = setInterval(() => {
    heartbeatCount++
    if (!mainWindow || mainWindow.isDestroyed()) {
      console.log(`[DIAGNOSTIC HEARTBEAT #${heartbeatCount}] mainWindow is DESTROYED or NULL!`)
      clearInterval(heartbeatTimer)
      return
    }
    const b = mainWindow.getBounds()
    console.log(
      `[DIAGNOSTIC HEARTBEAT #${heartbeatCount}] HWND: ${hwndHex} | Visible: ${mainWindow.isVisible()} | Minimized: ${mainWindow.isMinimized()} | Focused: ${mainWindow.isFocused()} | Bounds: (${b.x}, ${b.y}, ${b.width}x${b.height})`
    )
    if (heartbeatCount >= 5) clearInterval(heartbeatTimer)
  }, 3000)
}

app.whenReady().then(() => {
  if (!gotTheLock) return
  console.log('[DIAGNOSTIC] === app.whenReady() resolved ===')
  if (process.platform === 'win32') {
    app.setAppUserModelId('PDFWorkspace.App')
    console.log('[DIAGNOSTIC] setAppUserModelId("PDFWorkspace.App") executed')
  }

  registerIpcHandlers()
  console.log('[DIAGNOSTIC] registerIpcHandlers() executed')

  // 시작 파일 조회 IPC
  ipcMain.handle('app:getStartupFile', () => {
    const p = startupPdfPath
    startupPdfPath = null
    return p
  })

  // 문서 변경 상태 동기화 IPC
  ipcMain.on('app:set-dirty', (_e, dirty: boolean) => {
    isDocumentDirty = Boolean(dirty)
  })

  // Do not accept updater commands from unrelated web contents.
  ipcMain.handle('app:checkForUpdates', (event) => event.sender === mainWindow?.webContents
    ? updates.check() : { status: 'error', message: '허용되지 않은 요청입니다.' })
  ipcMain.handle('app:getUpdateStatus', (event) => event.sender === mainWindow?.webContents
    ? updates.getStatus() : { status: 'error' })
  ipcMain.handle('app:installUpdate', (event) => event.sender === mainWindow?.webContents
    ? updates.install() : { status: 'error', message: '허용되지 않은 요청입니다.' })

  Menu.setApplicationMenu(null)
  console.log('[DIAGNOSTIC] Native Win32 menu removed. Modern ribbon header active.')
  createWindow()

  // 패키징된 배포 환경인 경우 앱 실행 3초 후 자동 업데이트 검사 실행
  if (app.isPackaged) {
    setTimeout(() => {
      console.log('[AutoUpdater] Initiating automatic update check...')
      updates.check().catch((err) => {
        console.warn('[AutoUpdater] Initial update check failed:', err)
      })
    }, 3000)
    const updateInterval = setInterval(() => { void updates.check() }, 4 * 60 * 60 * 1000)
    updateInterval.unref()
  }

  app.on('activate', () => {
    console.log('[DIAGNOSTIC] app.activate triggered')
    if (BrowserWindow.getAllWindows().length === 0) createWindow()
  })
})

app.on('window-all-closed', () => {
  console.log('[DIAGNOSTIC] app.window-all-closed triggered')
  if (process.platform !== 'darwin') app.quit()
})

process.on('uncaughtException', (err) => {
  console.error('[DIAGNOSTIC CRITICAL] Uncaught exception in main process:', err)
})
