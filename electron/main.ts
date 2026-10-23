/**
 * Electron 메인 프로세스 진입점 (정밀 진단 로그 포함)
 */
import { app, BrowserWindow, shell, screen, nativeImage, Menu } from 'electron'
import { join, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'
import { existsSync } from 'node:fs'
import { registerIpcHandlers } from './ipc-handlers'
import { attachMenu } from './menu'

console.log('[DIAGNOSTIC] === Electron Main Process Starting ===')
console.log(`[DIAGNOSTIC] Node: ${process.version}, Electron: ${process.versions.electron}, Chrome: ${process.versions.chrome}`)
console.log(`[DIAGNOSTIC] PID: ${process.pid}, Platform: ${process.platform}, Arch: ${process.arch}`)

// 기본 하드웨어 가속 유지 (Electron 32 / Windows 11 DWM 렌더링에 필수)


const __dirname = dirname(fileURLToPath(import.meta.url))

let mainWindow: BrowserWindow | null = null

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
  mainWindow.on('close', () => console.log('[DIAGNOSTIC EVENT] Window "close" initiated'))
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
  console.log('[DIAGNOSTIC] === app.whenReady() resolved ===')
  if (process.platform === 'win32') {
    app.setAppUserModelId('PDFWorkspace.App')
    console.log('[DIAGNOSTIC] setAppUserModelId("PDFWorkspace.App") executed')
  }
  registerIpcHandlers()
  console.log('[DIAGNOSTIC] registerIpcHandlers() executed')
  Menu.setApplicationMenu(null)
  console.log('[DIAGNOSTIC] Native Win32 menu removed. Modern ribbon header active.')
  createWindow()

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

