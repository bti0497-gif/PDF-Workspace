/**
 * 앱 시작 초기화 스플래시 화면 (Splash Screen)
 * - 회사 로고 카드 및 초기화 프로그래스 바 표시
 * - 백그라운드 PDF 엔진/워커/캐시 사전 가열 진행 상태 연동
 * - 100% 완료 시 부드러운 페이드아웃 효과 후 메인 작업 화면으로 전환
 */
import { useEffect, useState } from 'react'
import splashBrand from '../assets/splash-brand.png'
import splashTitle from '../assets/splash-title.png'
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
      <SplashCard progress={progress} message={message} />
    </div>
  )
}

/** 초기화와 미리보기에서 같은 카드 마크업을 사용합니다. */
export function SplashCard({ progress, message }: { progress: number; message: string }) {
  const value = Math.max(0, Math.min(100, progress))
  return (
    <div className="splash-screen__content">
      <div className="splash-screen__card-wrapper">
        <img src={splashBrand} alt="회사 로고" className="splash-screen__brand" />
        <img src={splashTitle} alt="더존 PDF 편집기" className="splash-screen__title" />
        <div className="splash-screen__progress-box">
          <div className="splash-screen__message" role="status" title={message}>
            {value >= 100 ? '초기화 완료' : '초기화 중...'}
          </div>
          <div
            className="splash-screen__track"
            role="progressbar"
            aria-label="앱 초기화 진행률"
            aria-valuemin={0}
            aria-valuemax={100}
            aria-valuenow={value}
            aria-valuetext={`${message} ${value}%`}
          >
            <div className="splash-screen__bar" style={{ width: `${value}%` }} />
          </div>
        </div>
      </div>
    </div>
  )
}
