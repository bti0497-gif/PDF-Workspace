/**
 * 애플리케이션 메뉴 (한국어, 네이티브)
 */
import { Menu, BrowserWindow, MenuItemConstructorOptions, app, shell } from 'electron'

type MenuHandler = (event: string) => void

export function buildAppMenu(onMenu: MenuHandler): Menu {
  const isMac = process.platform === 'darwin'

  const send = (event: string): MenuItemConstructorOptions => ({
    label: '',
    click: () => onMenu(event)
  })

  const template: MenuItemConstructorOptions[] = [
    // 파일(F)
    {
      label: '파일(F)',
      submenu: [
        { ...send('file:open'), label: '열기...', accelerator: 'CmdOrCtrl+O' },
        { type: 'separator' },
        { ...send('file:save'), label: '저장', accelerator: 'CmdOrCtrl+S' },
        {
          ...send('file:saveAs'),
          label: '다른 이름으로 저장...',
          accelerator: 'CmdOrCtrl+Shift+S'
        },
        { type: 'separator' },
        { ...send('file:close'), label: '닫기', accelerator: 'CmdOrCtrl+W' },
        ...(isMac
          ? []
          : [{ type: 'separator' as const }, { role: 'quit' as const, label: '종료' }])
      ]
    },
    // 편집(E)
    {
      label: '편집(E)',
      submenu: [
        { ...send('edit:undo'), label: '실행 취소', accelerator: 'CmdOrCtrl+Z' },
        {
          ...send('edit:redo'),
          label: '다시 실행',
          accelerator: 'CmdOrCtrl+Shift+Z'
        },
        { type: 'separator' },
        {
          ...send('edit:deleteAnnotation'),
          label: '주석 삭제',
          accelerator: 'Delete'
        },
        { type: 'separator' },
        { role: 'selectAll', label: '전체 선택' }
      ]
    },
    // 도구(T)
    {
      label: '도구(T)',
      submenu: [
        { ...send('tool:select'), label: '선택', accelerator: 'CmdOrCtrl+1' },
        { ...send('tool:highlight'), label: '형광펜', accelerator: 'CmdOrCtrl+2' },
        {
          ...send('tool:rectangle'),
          label: '사각형',
          accelerator: 'CmdOrCtrl+3'
        },
        { ...send('tool:textbox'), label: '텍스트 상자', accelerator: 'CmdOrCtrl+4' },
        { ...send('tool:drawing'), label: '자유 그리기', accelerator: 'CmdOrCtrl+5' },
        { type: 'separator' },
        { ...send('tool:merge'), label: 'PDF 합치기...' },
        { ...send('tool:split'), label: 'PDF 나누기...' }
      ]
    },
    // 보기(V)
    {
      label: '보기(V)',
      submenu: [
        {
          ...send('view:zoomIn'),
          label: '확대',
          accelerator: 'CmdOrCtrl+='
        },
        {
          ...send('view:zoomOut'),
          label: '축소',
          accelerator: 'CmdOrCtrl+-'
        },
        {
          ...send('view:zoomReset'),
          label: '실제 크기',
          accelerator: 'CmdOrCtrl+0'
        },
        { ...send('view:fitWidth'), label: '폭에 맞춤' },
        { ...send('view:fitPage'), label: '페이지에 맞춤' },
        { type: 'separator' },
        {
          ...send('view:toggleThumbnails'),
          label: '썸네일 패널',
          accelerator: 'F4'
        },
        {
          ...send('view:toggleTheme'),
          label: '다크 모드 전환',
          accelerator: 'CmdOrCtrl+Shift+D'
        }
      ]
    },
    // 페이지(P)
    {
      label: '페이지(P)',
      submenu: [
        {
          ...send('page:rotateLeft'),
          label: '왼쪽으로 회전',
          accelerator: 'CmdOrCtrl+Shift+Left'
        },
        {
          ...send('page:rotateRight'),
          label: '오른쪽으로 회전',
          accelerator: 'CmdOrCtrl+Shift+Right'
        },
        { type: 'separator' },
        { ...send('page:delete'), label: '현재 페이지 삭제' },
        { ...send('page:extract'), label: '현재 페이지 추출...' },
        { type: 'separator' },
        {
          ...send('page:previous'),
          label: '이전 페이지',
          accelerator: 'Left'
        },
        { ...send('page:next'), label: '다음 페이지', accelerator: 'Right' }
      ]
    },
    // 도움말(H)
    {
      label: '도움말(H)',
      role: 'help',
      submenu: [
        { ...send('help:about'), label: 'PDF Workspace 정보' },
        { ...send('help:shortcuts'), label: '단축키...' },
        { type: 'separator' },
        {
          label: 'PDF.js (Mozilla)',
          click: () => shell.openExternal('https://mozilla.github.io/pdf.js/')
        },
        {
          label: 'pdf-lib',
          click: () => shell.openExternal('https://pdf-lib.js.org/')
        }
      ]
    }
  ]

  // macOS 의 앱 메뉴 추가
  if (isMac) {
    template.unshift({
      label: app.name,
      submenu: [
        { role: 'about', label: 'PDF Workspace 정보' },
        { type: 'separator' },
        { role: 'services', label: '서비스' },
        { type: 'separator' },
        { role: 'hide', label: '숨기기' },
        { role: 'hideOthers', label: '기타 숨기기' },
        { role: 'unhide', label: '모두 표시' },
        { type: 'separator' },
        { role: 'quit', label: '종료' }
      ]
    })
  }

  return Menu.buildFromTemplate(template)
}

/** 포커스된 창 기준으로 메뉴 이벤트 전달 */
export function attachMenu(): void {
  const onMenu = (event: string) => {
    const win = BrowserWindow.getFocusedWindow()
    if (win && !win.isDestroyed()) {
      win.webContents.send('menu:event', event)
    }
  }
  const menu = buildAppMenu(onMenu)
  Menu.setApplicationMenu(menu)
}
