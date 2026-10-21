// Region drawing shared by the overlay (SVG, screen pixels) and the frame capture (canvas, native pixels),
// so the saved image shows the region exactly as it looked on screen, scaled to the native frame size.

import { KIND_COLORS, clamp } from './app.js';

export const STROKE = 2;          // px on screen
export const DIM = 0.3;           // opacity of the dimming outside a rect (on screen and in the saved frame image)
export const POINT_R = 8;         // ring radius of a point marker, px on screen
export const TAIL = 12;           // length of the point marker's tail, px on screen
const HALO = 'rgba(0, 0, 0, 0.55)';

const SVG_NS = 'http://www.w3.org/2000/svg';

/** Normalised region from two points in fractions of the frame; a point when both are equal. */
export function makeRect(a, b) {
  return {
    shape: 'rect',
    x0: round3(Math.min(a.x, b.x)), y0: round3(Math.min(a.y, b.y)),
    x1: round3(Math.max(a.x, b.x)), y1: round3(Math.max(a.y, b.y)),
  };
}

export function makePoint(p) {
  const x = round3(p.x);
  const y = round3(p.y);
  return { shape: 'point', x0: x, y0: y, x1: x, y1: y };
}

function round3(v) {
  return Math.round(clamp(v, 0, 1) * 1000) / 1000;
}

/** Bounding box of a region in pixels of a w x h frame. */
export function regionBox(region, w, h) {
  return { x0: region.x0 * w, y0: region.y0 * h, x1: region.x1 * w, y1: region.y1 * h };
}

function svgEl(tag, attrs) {
  const node = document.createElementNS(SVG_NS, tag);
  for (const [k, v] of Object.entries(attrs)) node.setAttribute(k, v);
  return node;
}

/** Draws a region into an SVG whose viewBox is the displayed frame (w x h px). */
export function drawRegionSvg(svg, region, kind, w, h) {
  svg.replaceChildren();
  if (!region) return;
  const color = KIND_COLORS[kind] || '#ffffff';
  const b = regionBox(region, w, h);
  if (region.shape === 'rect') {
    const d = `M0 0H${w}V${h}H0Z M${b.x0} ${b.y0}V${b.y1}H${b.x1}V${b.y0}Z`;
    svg.append(svgEl('path', { d, fill: `rgba(0,0,0,${DIM})`, 'fill-rule': 'evenodd' }));
    const r = { x: b.x0, y: b.y0, width: Math.max(1, b.x1 - b.x0), height: Math.max(1, b.y1 - b.y0), fill: 'none' };
    svg.append(svgEl('rect', { ...r, stroke: color, 'stroke-width': STROKE }));
  } else {
    const { x0: x, y0: y } = b;
    const parts = pointParts(x, y, 1);
    svg.append(svgEl('circle', { cx: x, cy: y, r: parts.r, fill: 'none', stroke: HALO, 'stroke-width': STROKE + 2 }));
    svg.append(svgEl('line', { ...parts.tail, stroke: HALO, 'stroke-width': STROKE + 2, 'stroke-linecap': 'round' }));
    svg.append(svgEl('circle', { cx: x, cy: y, r: parts.r, fill: 'none', stroke: color, 'stroke-width': STROKE }));
    svg.append(svgEl('line', { ...parts.tail, stroke: color, 'stroke-width': STROKE, 'stroke-linecap': 'round' }));
    svg.append(svgEl('circle', { cx: x, cy: y, r: parts.dot, fill: color }));
  }
}

function pointParts(x, y, s) {
  const r = POINT_R * s;
  const k = Math.SQRT1_2;
  return {
    r,
    dot: 2 * s,
    tail: { x1: x + r * k, y1: y - r * k, x2: x + (r + TAIL * s) * k, y2: y - (r + TAIL * s) * k },
  };
}

/** Draws a region onto a canvas holding the native frame (w x h); `scale` = native px per screen px. */
export function drawRegionCanvas(ctx, region, kind, w, h, scale) {
  if (!region) return;
  const color = KIND_COLORS[kind] || '#ffffff';
  const b = regionBox(region, w, h);
  ctx.save();
  if (region.shape === 'rect') {
    ctx.fillStyle = `rgba(0,0,0,${DIM})`;
    ctx.beginPath();
    ctx.rect(0, 0, w, h);
    ctx.rect(b.x0, b.y0, b.x1 - b.x0, b.y1 - b.y0);
    ctx.fill('evenodd');
    ctx.strokeStyle = color;
    ctx.lineWidth = STROKE * scale;
    ctx.strokeRect(b.x0, b.y0, Math.max(scale, b.x1 - b.x0), Math.max(scale, b.y1 - b.y0));
  } else {
    const p = pointParts(b.x0, b.y0, scale);
    ctx.lineCap = 'round';
    for (const [stroke, width] of [[HALO, (STROKE + 2) * scale], [color, STROKE * scale]]) {
      ctx.strokeStyle = stroke;
      ctx.lineWidth = width;
      ctx.beginPath();
      ctx.arc(b.x0, b.y0, p.r, 0, Math.PI * 2);
      ctx.stroke();
      ctx.beginPath();
      ctx.moveTo(p.tail.x1, p.tail.y1);
      ctx.lineTo(p.tail.x2, p.tail.y2);
      ctx.stroke();
    }
    ctx.fillStyle = color;
    ctx.beginPath();
    ctx.arc(b.x0, b.y0, p.dot, 0, Math.PI * 2);
    ctx.fill();
  }
  ctx.restore();
}
