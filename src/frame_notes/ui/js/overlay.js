// Region layer over the video: an SVG sized and positioned to the displayed frame rectangle (object-fit:
// contain), recomputed on resize and on loadedmetadata. In drawing mode a drag draws a rect and a click puts
// a point; regions are reported in fractions of the frame.

import { app, clamp } from './app.js';
import { drawRegionSvg, makePoint, makeRect } from './region.js';

const DRAG_PX = 4;

export class Overlay {
  constructor(stage, video, svg) {
    this.stage = stage;
    this.video = video;
    this.svg = svg;
    this.box = null;          // displayed frame rect inside the stage {x, y, w, h}
    this.region = null;
    this.kind = null;
    this.onRegion = null;     // set while drawing
    this.drag = null;

    new ResizeObserver(() => this.layout()).observe(stage);
    video.addEventListener('loadedmetadata', () => this.layout());
    video.addEventListener('resize', () => this.layout());

    stage.addEventListener('pointerdown', (e) => this.down(e));
    stage.addEventListener('pointermove', (e) => this.move(e));
    stage.addEventListener('pointerup', (e) => this.up(e));
    stage.addEventListener('pointercancel', () => {
      this.drag = null;
      this.draw();
    });
  }

  layout() {
    const vw = this.video.videoWidth;
    const vh = this.video.videoHeight;
    const cw = this.stage.clientWidth;
    const ch = this.stage.clientHeight;
    if (!vw || !vh || !cw || !ch || !app.video) {
      this.box = null;
      this.svg.style.display = 'none';
      app.emit('layout', null);
      return;
    }
    const s = Math.min(cw / vw, ch / vh);
    const w = vw * s;
    const h = vh * s;
    this.box = { x: (cw - w) / 2, y: (ch - h) / 2, w, h };
    const st = this.svg.style;
    st.display = 'block';
    st.left = `${this.box.x}px`;
    st.top = `${this.box.y}px`;
    st.width = `${w}px`;
    st.height = `${h}px`;
    this.svg.setAttribute('viewBox', `0 0 ${w} ${h}`);
    this.draw();
    app.emit('layout', this.box);
  }

  /** Shows a region (or nothing) in the colour of `kind`. */
  show(region, kind) {
    this.region = region || null;
    this.kind = kind || null;
    this.draw();
  }

  draw() {
    if (!this.box) return;
    const region = this.drag && this.drag.moved ? this.drag.region : this.region;
    drawRegionSvg(this.svg, region, this.kind, this.box.w, this.box.h);
  }

  /** Drawing mode on (callback gets each finished region) or off (null). */
  setDrawing(onRegion) {
    this.onRegion = onRegion;
    this.drag = null;
    this.stage.classList.toggle('drawing', !!onRegion);
  }

  toFraction(e) {
    const r = this.stage.getBoundingClientRect();
    const b = this.box;
    return {
      x: clamp((e.clientX - r.left - b.x) / b.w, 0, 1),
      y: clamp((e.clientY - r.top - b.y) / b.h, 0, 1),
      px: e.clientX,
      py: e.clientY,
    };
  }

  down(e) {
    if (!this.onRegion || !this.box || e.button !== 0) return;
    if (e.target.closest('#note-editor, #hint, button, input')) return;
    e.preventDefault();
    this.stage.setPointerCapture(e.pointerId);
    this.drag = { start: this.toFraction(e), moved: false, region: null };
  }

  move(e) {
    const d = this.drag;
    if (!d) return;
    const p = this.toFraction(e);
    if (!d.moved && Math.hypot(p.px - d.start.px, p.py - d.start.py) < DRAG_PX) return;
    d.moved = true;
    d.region = makeRect(d.start, p);
    this.draw();
  }

  up(e) {
    const d = this.drag;
    if (!d) return;
    this.drag = null;
    if (this.stage.hasPointerCapture(e.pointerId)) this.stage.releasePointerCapture(e.pointerId);
    let region;
    if (d.moved) {
      const p = this.toFraction(e);
      region = makeRect(d.start, p);
      if (region.x0 === region.x1 || region.y0 === region.y1) region = makePoint(d.start);
    } else {
      region = makePoint(d.start);
    }
    if (this.onRegion) this.onRegion(region);
  }
}
