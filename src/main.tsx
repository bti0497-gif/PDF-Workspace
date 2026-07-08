/**
 * 렌더러 프로세스 React 진입점
 */
import React from 'react'
import { createRoot } from 'react-dom/client'
import App from './App'
import './styles/app.css'

const container = document.getElementById('root')
if (!container) throw new Error('루트 컨테이너를 찾을 수 없습니다.')

createRoot(container).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
)
