/**
 * 렌더러 프로세스 React 진입점
 */
import React from 'react'
import { createRoot } from 'react-dom/client'
import { initWorkspaceFallback } from './lib/workspace-fallback'
import App from './App'
import './styles/app.css'

// 브라우저 환경에서 실행될 경우 폴백 API 초기화
initWorkspaceFallback()

const container = document.getElementById('root')
if (!container) throw new Error('루트 컨테이너를 찾을 수 없습니다.')

createRoot(container).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
)
