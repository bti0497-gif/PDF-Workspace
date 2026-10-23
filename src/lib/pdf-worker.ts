/**
 * PDF.js 워커 설정
 * - Vite 의 ?url 기능을 통해 개발(HMR) 및 프로덕션(file:// 프로토콜) 모두 상대경로로 안전하게 자동 번들링
 */
import { GlobalWorkerOptions, version } from 'pdfjs-dist'
import pdfWorkerUrl from 'pdfjs-dist/build/pdf.worker.min.mjs?url'

GlobalWorkerOptions.workerSrc = pdfWorkerUrl

export const pdfjsVersion = version

