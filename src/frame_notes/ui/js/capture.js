// Frame image of a note: the current video frame at native size with the region drawn on it the way the
// overlay shows it. The canvas is filled synchronously (grab), the PNG is encoded afterwards (encode), so a
// click that seeks elsewhere right after cannot change the captured frame.

import { drawRegionCanvas } from './region.js';

/** Draws the current frame and the region into a new canvas. Throws when the video cannot be read. */
export function grabFrame(video, region, kind, displayWidth) {
  const w = video.videoWidth;
  const h = video.videoHeight;
  if (!w || !h) throw new Error('The video frame is not available.');
  // OffscreenCanvas.convertToBlob encodes right away; HTMLCanvasElement.toBlob may wait for idle time.
  let canvas;
  if (typeof OffscreenCanvas === 'function') {
    canvas = new OffscreenCanvas(w, h);
  } else {
    canvas = document.createElement('canvas');
    canvas.width = w;
    canvas.height = h;
  }
  const ctx = canvas.getContext('2d');
  ctx.drawImage(video, 0, 0, w, h);
  drawRegionCanvas(ctx, region, kind, w, h, displayWidth > 0 ? w / displayWidth : 1);
  return canvas;
}

/** PNG of a canvas as a base64 string (no data URL prefix). */
export async function encodePng(canvas) {
  let blob;
  try {
    if (typeof canvas.convertToBlob === 'function') {
      blob = await canvas.convertToBlob({ type: 'image/png' });
    } else {
      blob = await new Promise((resolve, reject) => {
        canvas.toBlob((b) => (b ? resolve(b) : reject(new Error('empty'))), 'image/png');
      });
    }
  } catch (err) {
    throw new Error('The frame image could not be created.');
  }
  const dataUrl = await new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result);
    reader.onerror = () => reject(new Error('The frame image could not be created.'));
    reader.readAsDataURL(blob);
  });
  return String(dataUrl).slice(String(dataUrl).indexOf(',') + 1);
}
