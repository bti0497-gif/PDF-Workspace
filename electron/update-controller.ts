import type { UpdateCheckResult } from './shared/types'

interface Updater {
  autoDownload: boolean
  autoInstallOnAppQuit: boolean
  allowDowngrade: boolean
  allowPrerelease: boolean
  on(event: string, listener: (...args: any[]) => void): unknown
  checkForUpdates(): Promise<unknown>
  quitAndInstall(isSilent?: boolean, isForceRunAfter?: boolean): void
}
interface Options {
  updater: Updater
  isPackaged: () => boolean
  emit: (state: UpdateCheckResult) => void
  canInstall: () => Promise<boolean>
  confirmInstall: () => Promise<boolean>
  lockInteraction: (locked: boolean) => void
  checkTimeoutMs?: number
}

/** Download automatically; never replace an app with unsaved work or on ordinary quit. */
export function createUpdateController(options: Options) {
  const { updater } = options
  updater.autoDownload = true
  updater.autoInstallOnAppQuit = false
  updater.allowDowngrade = false
  updater.allowPrerelease = false
  let state: UpdateCheckResult = { status: 'idle' }
  let downloaded = false
  let installing = false
  let checkPromise: Promise<UpdateCheckResult> | null = null
  let installTimer: ReturnType<typeof setTimeout> | undefined
  const emit = (next: UpdateCheckResult) => {
    state = { ...next, readyToInstall: downloaded }
    options.emit({ ...state })
    return { ...state }
  }
  const unlock = () => {
    if (installTimer) clearTimeout(installTimer)
    installing = false
    options.lockInteraction(false)
  }
  updater.on('checking-for-update', () => { if (!downloaded) emit({ status: 'checking' }) })
  updater.on('update-available', (info) => emit({ status: 'available', version: info.version }))
  updater.on('update-not-available', (info) => {
    if (!downloaded) emit({ status: 'not-available', version: info.version })
  })
  updater.on('download-progress', (progress) => emit({
    status: 'downloading', version: state.version, percent: Math.round(progress.percent)
  }))
  updater.on('update-downloaded', (info) => {
    downloaded = true
    emit({ status: 'downloaded', version: info.version })
  })
  updater.on('error', (error) => {
    if (installing) unlock()
    emit({ status: 'error', version: state.version, message: error?.message || String(error) })
  })
  const getStatus = () => options.isPackaged()
    ? { ...state, readyToInstall: downloaded }
    : { status: 'dev', message: '개발 모드에서는 업데이트를 설치하지 않습니다.', readyToInstall: false }

  function check(): Promise<UpdateCheckResult> {
    if (!options.isPackaged()) return Promise.resolve(getStatus())
    if (downloaded) return Promise.resolve(emit({ status: 'downloaded', version: state.version }))
    if (checkPromise) return checkPromise
    if (state.status === 'available' || state.status === 'downloading' || installing) return Promise.resolve(getStatus())
    emit({ status: 'checking' })
    let timeout: ReturnType<typeof setTimeout>
    const deadline = new Promise<never>((_, reject) => {
      timeout = setTimeout(() => reject(new Error('업데이트 서버 응답 시간이 초과되었습니다. 잠시 후 다시 확인해 주세요.')), options.checkTimeoutMs ?? 30000)
    })
    checkPromise = Promise.race([Promise.resolve().then(() => updater.checkForUpdates()), deadline])
      .then(() => getStatus())
      .catch((error) => emit({ status: 'error', message: error?.message || String(error) }))
      .finally(() => { clearTimeout(timeout); checkPromise = null })
    return checkPromise
  }

  async function install(): Promise<UpdateCheckResult> {
    if (!options.isPackaged()) return getStatus()
    if (installing) return { ...getStatus(), status: 'busy', message: '업데이트 적용을 준비하고 있습니다.' }
    if (!downloaded) return { ...getStatus(), status: 'blocked', message: '업데이트 다운로드가 완료되지 않았습니다.' }
    installing = true
    try {
      if (!await options.canInstall()) return {
        ...getStatus(), status: 'blocked', message: '편집 중인 문서를 저장하고 진행 중인 작업이 끝난 뒤 다시 시도해 주세요.'
      }
      if (!await options.confirmInstall()) return { ...getStatus(), status: 'cancelled', message: '업데이트를 나중에 적용합니다.' }
      // Disable native input, then request fresh renderer state. Timeout/unknown is unsafe.
      options.lockInteraction(true)
      if (!await options.canInstall()) return {
        ...getStatus(), status: 'blocked', message: '저장하지 않은 변경 또는 진행 중인 작업이 있어 업데이트를 중단했습니다.'
      }
      emit({ status: 'restarting', version: state.version })
      updater.quitAndInstall(false, true)
      installTimer = setTimeout(() => {
        unlock()
        emit({ status: 'error', version: state.version, message: '업데이트 재시작을 확인하지 못했습니다. 다시 시도해 주세요.' })
      }, 15000)
      installTimer.unref?.()
      return getStatus()
    } catch (error: any) {
      return emit({ status: 'error', version: state.version, message: error?.message || String(error) })
    } finally {
      if (state.status !== 'restarting') unlock()
    }
  }
  return { check, install, getStatus }
}
