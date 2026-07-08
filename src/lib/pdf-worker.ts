/**
 * PDF.js 워커 설정
 * - 워커 파일은 public/pdf.worker.min.mjs 에 위치 (postinstall 스크립트로 복사됨)
 * - 개발/프로덕션 모두 동일 경로 사용
 */
import { GlobalWorkerOptions, version } from 'pdfjs-dist'

// 워커 파일 경로 설정 - Vite 가 public 폴더를 루트로 서빙
const workerPath = new URL('/pdf.worker.min.mjs', window.location.href).href
GlobalWorkerOptions.workerSrc = workerPath

export const pdfjsVersion = version
