import { app, BrowserWindow } from 'electron'

app.whenReady().then(() => {
  console.log('[TestGUI] App ready!')
  const win = new BrowserWindow({
    width: 600,
    height: 400,
    show: true,
    title: 'TEST GUI WINDOW'
  })
  win.loadURL('data:text/html,<h1>TEST GUI WINDOW</h1>')
  console.log('[TestGUI] Window created, isVisible:', win.isVisible(), 'Bounds:', win.getBounds())
  setTimeout(() => {
    console.log('[TestGUI] Test closing...')
    win.close()
    app.quit()
  }, 4000)
})
