/**
 * 앱 시작 초기화 스플래시 화면 (Splash Screen)
 * - 회사 로고 카드 및 초기화 프로그래스 바 표시
 * - 백그라운드 PDF 엔진/워커/캐시 사전 가열 진행 상태 연동
 * - 100% 완료 시 부드러운 페이드아웃 효과 후 메인 작업 화면으로 전환
 */
import { useEffect, useState } from 'react'
import splashLogo from '../assets/splash-logo.png'
import { warmupPdfEngine } from '../core/services/warmup.service'

interface Props {
  onComplete: () => void
}

export function SplashScreen({ onComplete }: Props) {
  const [progress, setProgress] = useState(10)
  const [message, setMessage] = useState('더존 PDF 편집기 시작 중...')
  const [isFading, setIsFading] = useState(false)

  useEffect(() => {
    let isCancelled = false

    const runWarmup = async () => {
      await warmupPdfEngine((percent, msg) => {
        if (isCancelled) return
        setProgress(percent)
        setMessage(msg)
      })

      if (isCancelled) return

      // 100% 완료 상태를 사용자가 충분히 인지할 수 있도록 550ms 대기 후 페이드아웃 시작
      setTimeout(() => {
        if (isCancelled) return
        setIsFading(true)

        // 페이드아웃 애니메이션(500ms) 완료 후 완전 언마운트
        setTimeout(() => {
          if (isCancelled) return
          onComplete()
        }, 500)
      }, 550)
    }

    runWarmup()

    return () => {
      isCancelled = true
    }
  }, [onComplete])

  return (
    <div
      className={`splash-screen ${isFading ? 'splash-screen--fading' : ''}`}
      aria-label="더존 PDF 편집기 초기화 화면"
    >
      <div className="splash-screen__content">
        {/* 회사 로고 카드 */}
        <div className="splash-screen__card-wrapper">
          <img
            src={splashLogo}
            alt="더존 PDF 편집기"
            className="splash-screen__logo-card"
          />
        </div>

        {/* 하단 프로그래스 바 및 진행 상태 */}
        <div className="splash-screen__progress-box">
          <div className="splash-screen__track">
            <div
              className="splash-screen__bar"
              style={{ width: `${progress}%` }}
            />
          </div>

          <div className="splash-screen__status-row">
            <span className="splash-screen__message">{message}</span>
            <span className="splash-screen__percent">{progress}%</span>
          </div>
        </div>
      </div>
    </div>
  )
}
