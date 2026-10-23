import fs from 'node:fs'
import path from 'node:path'
import { createCanvas, loadImage } from '@napi-rs/canvas'

const srcPath = 'C:/Users/ASUS/.gemini/antigravity/brain/80bd3065-96d8-48e2-9eb7-009c8a66fdbf/.user_uploaded/media_1792733021879_1f399d5b.png'

async function generate() {
  console.log('Loading source image:', srcPath)
  const img = await loadImage(srcPath)
  console.log(`Source dimensions: ${img.width}x${img.height}`)

  const sizes = [256, 128, 64, 48, 32, 16]
  const pngBuffers = []

  for (const size of sizes) {
    const canvas = createCanvas(size, size)
    const ctx = canvas.getContext('2d')

    // 부드러운 안티앨리어싱
    ctx.imageSmoothingEnabled = true
    ctx.imageSmoothingQuality = 'high'

    // 원본 비율 유지하면서 중앙 배치 (약간의 패딩 4% 부여)
    const padding = size * 0.04
    const availSize = size - padding * 2
    const scale = Math.min(availSize / img.width, availSize / img.height)
    const drawW = img.width * scale
    const drawH = img.height * scale
    const drawX = (size - drawW) / 2
    const drawY = (size - drawH) / 2

    ctx.drawImage(img, drawX, drawY, drawW, drawH)
    const buf = await canvas.encode('png')
    pngBuffers.push({ size, buffer: buf })
    console.log(`Generated ${size}x${size} PNG: ${buf.length} bytes`)
  }

  // 1. build 디렉터리 생성
  if (!fs.existsSync('build')) {
    fs.mkdirSync('build', { recursive: true })
  }
  if (!fs.existsSync('public')) {
    fs.mkdirSync('public', { recursive: true })
  }
  if (!fs.existsSync('src/assets')) {
    fs.mkdirSync('src/assets', { recursive: true })
  }

  // 2. build/icon.png 및 public/app-icon.png, src/assets/app-icon.png 저장 (256x256)
  const p256 = pngBuffers.find((p) => p.size === 256).buffer
  fs.writeFileSync('build/icon.png', p256)
  fs.writeFileSync('public/app-icon.png', p256)
  fs.writeFileSync('src/assets/app-icon.png', p256)
  console.log('Saved PNG icons to build/icon.png, public/app-icon.png, src/assets/app-icon.png')

  // 3. Multi-resolution ICO 파일 조립
  // Header: 6 bytes
  const count = pngBuffers.length
  const header = Buffer.alloc(6)
  header.writeUInt16LE(0, 0) // Reserved
  header.writeUInt16LE(1, 2) // Type 1 = ICO
  header.writeUInt16LE(count, 4) // Number of images

  // Entries: 16 bytes each
  const entries = []
  let offset = 6 + count * 16

  for (const item of pngBuffers) {
    const entry = Buffer.alloc(16)
    const s = item.size >= 256 ? 0 : item.size
    entry.writeUInt8(s, 0) // Width
    entry.writeUInt8(s, 1) // Height
    entry.writeUInt8(0, 2) // Color count
    entry.writeUInt8(0, 3) // Reserved
    entry.writeUInt16LE(1, 4) // Color planes
    entry.writeUInt16LE(32, 6) // Bits per pixel
    entry.writeUInt32LE(item.buffer.length, 8) // Size of image data
    entry.writeUInt32LE(offset, 12) // Offset of image data
    entries.push(entry)
    offset += item.buffer.length
  }

  const icoBuffer = Buffer.concat([
    header,
    ...entries,
    ...pngBuffers.map((p) => p.buffer)
  ])

  fs.writeFileSync('build/icon.ico', icoBuffer)
  console.log(`Saved build/icon.ico: ${icoBuffer.length} bytes (contains ${count} resolutions)`)
}

generate().catch(console.error)
