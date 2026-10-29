import type { CropMargins } from '../core/domain/page'

export const ZERO_CROP: CropMargins = { top: 0, right: 0, bottom: 0, left: 0 }
export const MIN_CROP_SIZE = 10 // pt, never silently shrink below this limit
export function normalizeRotation(rotation: number): number { return ((rotation % 360) + 360) % 360 }
/** PDF unrotated edges -> displayed edges (PDF positive rotation is clockwise). */
export function displayMargins(m: CropMargins, rotation: number): CropMargins {
  switch (normalizeRotation(rotation)) {
    case 90: return { top: m.left, right: m.top, bottom: m.right, left: m.bottom }
    case 180: return { top: m.bottom, right: m.left, bottom: m.top, left: m.right }
    case 270: return { top: m.right, right: m.bottom, bottom: m.left, left: m.top }
    default: return { ...m }
  }
}
export function pdfMargins(m: CropMargins, rotation: number): CropMargins {
  return displayMargins(m, 360 - normalizeRotation(rotation))
}
export function validCrop(m: CropMargins, width: number, height: number): boolean {
  return Object.values(m).every(v => Number.isFinite(v) && v >= 0) &&
    width - m.left - m.right >= MIN_CROP_SIZE && height - m.top - m.bottom >= MIN_CROP_SIZE
}
