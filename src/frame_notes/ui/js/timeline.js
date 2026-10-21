// Timeline: ruler with timecode labels at a step chosen from the zoom, a track with one marker per note
// (stacked when several share a frame), the playhead, a scroll bar while zoomed, and a hover card.
// Wheel zooms around the pointer, Shift + wheel pans, 0 resets; drag in the ruler or the empty track scrubs.

import { app, el, clamp, shortText } from './app.js';
import { formatTimecode, formatClock } from './frames.js';

const ROW_H = 18;
const ROW_TOP = 6;
const MAX_ROWS = 3;
const MIN_TAG_PX = 30;
const SECOND_STEPS = [0.1, 0.2, 0.5, 1, 2, 5, 10, 30, 60, 120, 300, 600, 1800, 3600];

export class Timeline {
  constructor({ root, player, onPick, onEdit }) {
    this.root = root;
    this.player = player;
    this.onPick = onPick;      // (note) => select and seek
    this.onEdit = onEdit;      // (note) => edit
    this.body = root.querySelector('.tl-body');
    this.canvas = root.querySelector('.tl-ruler');
    this.track = root.querySelector('.tl-track');
    this.playhead = root.querySelector('.tl-playhead');
    this.scroll = root.querySelector('.tl-scroll');
    this.thumb = root.querySelector('.tl-thumb');
    this.card = root.querySelector('.tl-card');
    this.view = { start: 0, span: 0 };
    this.width = 0;
    this.tags = new Map();     // note id -> tag element
    this.colors = null;
    this.cardTimer = 0;

    new ResizeObserver(() => this.render()).observe(this.body);
    this.body.addEventListener('wheel', (e) => this.onWheel(e), { passive: false });
    this.scroll.addEventListener('wheel', (e) => this.onWheel(e), { passive: false });
    this.body.addEventListener('pointerdown', (e) => this.onScrubStart(e));
    this.thumb.addEventListener('pointerdown', (e) => this.onThumbStart(e));
    this.scroll.addEventListener('pointerdown', (e) => {
      if (e.target !== this.scroll) return;
      const r = this.scroll.getBoundingClientRect();
      const t = ((e.clientX - r.left) / r.width) * this.duration;
      this.setView(t - this.view.span / 2, this.view.span);
    });

    app.on('frame', () => this.onFrame());
    app.on('state', () => this.render());
    app.on('metadata', () => {
      this.resetZoom();
    });
    app.on('select', () => this.markSelection());
  }

  get duration() {
    return this.player.duration || 0;
  }

  get rate() {
    return app.fps || 0;
  }

  resetZoom() {
    this.view = { start: 0, span: this.duration };
    this.render();
  }

  setView(start, span) {
    const dur = this.duration;
    if (!dur) return;
    const minSpan = Math.min(dur, 12 / (this.rate || 30));
    span = clamp(span, minSpan, dur);
    start = clamp(start, 0, dur - span);
    this.view = { start, span };
    this.render();
  }

  x(t) {
    return ((t - this.view.start) / this.view.span) * this.width;
  }

  timeAt(px) {
    return this.view.start + (px / this.width) * this.view.span;
  }

  frameTime(frame) {
    return this.rate ? frame / this.rate : 0;
  }

  playheadTime() {
    if (!this.rate) return this.player.v.currentTime || 0;
    return this.frameTime(app.frame);
  }

  render() {
    this.width = this.body.clientWidth;
    const dur = this.duration;
    const hasVideo = !!app.video && dur > 0;
    this.root.classList.toggle('empty', !hasVideo);
    if (!hasVideo) {
      this.track.replaceChildren();
      this.tags.clear();
      this.clearRuler();
      this.playhead.style.display = 'none';
      this.scroll.hidden = true;
      return;
    }
    if (!this.view.span || this.view.span > dur || this.view.start + this.view.span > dur + 1e-9) {
      this.view = { start: 0, span: dur };
    }
    this.renderRuler();
    this.renderMarkers();
    this.renderPlayhead();
    this.renderScroll();
  }

  readColors() {
    const cs = getComputedStyle(document.documentElement);
    const v = (name) => cs.getPropertyValue(name).trim();
    this.colors = { tick: v('--line-2'), tickMinor: v('--line'), label: v('--ink-3'), mono: v('--mono') };
  }

  clearRuler() {
    const ctx = this.canvas.getContext('2d');
    ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);
  }

  pickStep(ctx, sample) {
    const minPx = ctx.measureText(sample).width + 22;
    const pxPerSec = this.width / this.view.span;
    if (this.rate) {
      const base = app.tb.base;
      const steps = [1, 2, 5, 10, base, 2 * base, 5 * base, 10 * base, 30 * base, 60 * base, 120 * base,
        300 * base, 600 * base, 1800 * base, 3600 * base];
      const pxPerFrame = pxPerSec / this.rate;
      return { frames: steps.find((s) => s * pxPerFrame >= minPx) || steps[steps.length - 1] };
    }
    return { seconds: SECOND_STEPS.find((s) => s * pxPerSec >= minPx) || 3600 };
  }

  renderRuler() {
    if (!this.colors) this.readColors();
    const c = this.colors;
    const dpr = window.devicePixelRatio || 1;
    const cssW = this.width;
    const cssH = this.canvas.clientHeight;
    if (this.canvas.width !== Math.round(cssW * dpr) || this.canvas.height !== Math.round(cssH * dpr)) {
      this.canvas.width = Math.round(cssW * dpr);
      this.canvas.height = Math.round(cssH * dpr);
    }
    const ctx = this.canvas.getContext('2d');
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, cssW, cssH);
    ctx.font = `10.5px ${c.mono}`;
    ctx.textBaseline = 'middle';
    const end = this.view.start + this.view.span;

    let ticks;   // [{t, label}]
    let minor = [];
    if (this.rate) {
      const sample = formatTimecode(Math.round(end * this.rate), app.tb);
      const { frames: step } = this.pickStep(ctx, sample);
      const f0 = Math.floor((this.view.start * this.rate) / step) * step;
      const f1 = Math.ceil((end * this.rate) / step) * step;
      ticks = [];
      for (let f = f0; f <= f1; f += step) ticks.push({ t: f / this.rate, label: formatTimecode(f, app.tb) });
      const sub = step % 5 === 0 ? step / 5 : step % 2 === 0 ? step / 2 : 0;
      if (sub && (sub / this.rate) * (this.width / this.view.span) >= 6) {
        for (let f = f0; f <= f1; f += sub) if (f % step) minor.push(f / this.rate);
      }
    } else {
      const { seconds: step } = this.pickStep(ctx, formatClock(end));
      const k0 = Math.floor(this.view.start / step);
      const k1 = Math.ceil(end / step);
      ticks = [];
      for (let k = k0; k <= k1; k++) ticks.push({ t: k * step, label: formatClock(k * step) });
      const sub = step / 5;
      if (sub * (this.width / this.view.span) >= 6) {
        for (let k = k0 * 5; k <= k1 * 5; k++) if (k % 5) minor.push(k * sub);
      }
    }

    ctx.strokeStyle = c.tickMinor;
    ctx.lineWidth = 1;
    ctx.beginPath();
    for (const t of minor) {
      const x = Math.round(this.x(t)) + 0.5;
      ctx.moveTo(x, cssH - 5);
      ctx.lineTo(x, cssH);
    }
    ctx.stroke();
    ctx.strokeStyle = c.tick;
    ctx.beginPath();
    for (const { t } of ticks) {
      const x = Math.round(this.x(t)) + 0.5;
      ctx.moveTo(x, cssH - 10);
      ctx.lineTo(x, cssH);
    }
    ctx.stroke();
    ctx.fillStyle = c.label;
    for (const { t, label } of ticks) {
      const x = this.x(t);
      if (x < -200 || x > cssW + 10) continue;
      ctx.fillText(label, Math.round(x) + 4, Math.round(cssH / 2) - 2);
    }
  }

  renderMarkers() {
    this.track.replaceChildren();
    this.tags.clear();
    if (!this.rate) return;
    const groups = [];
    for (const n of app.notes) {
      const last = groups[groups.length - 1];
      if (last && last.frame === n.frame) last.notes.push(n);
      else groups.push({ frame: n.frame, notes: [n] });
    }
    const xs = groups.map((g) => this.x(this.frameTime(g.frame)));
    groups.forEach((g, gi) => {
      const x = xs[gi];
      const nextX = gi + 1 < groups.length ? xs[gi + 1] : this.width + 400;
      if (nextX < -2 || x > this.width + 2) return;
      const avail = Math.max(0, nextX - x - 6);
      const stem = el('div', `tl-stem k-${g.notes[0].kind}`);
      stem.style.left = `${Math.round(x)}px`;
      this.track.append(stem);
      g.notes.slice(0, MAX_ROWS).forEach((n, i) => {
        const tag = el('div', `tl-tag k-${n.kind}`);
        tag.dataset.id = n.id;
        tag.style.left = `${Math.round(x)}px`;
        tag.style.top = `${ROW_TOP + i * ROW_H}px`;
        if (avail >= MIN_TAG_PX) {
          tag.style.maxWidth = `${Math.floor(avail)}px`;
          const more = i === MAX_ROWS - 1 && g.notes.length > MAX_ROWS ? ` +${g.notes.length - MAX_ROWS}` : '';
          tag.append(el('span', 'tl-tag-label', (shortText(n.text) || n.kind) + more));
        } else {
          tag.classList.add('compact');
        }
        if (n.id === app.selectedId) tag.classList.add('sel');
        tag.addEventListener('pointerenter', () => this.showCard(n, x));
        tag.addEventListener('pointerleave', () => this.hideCard());
        tag.addEventListener('pointerdown', (e) => e.stopPropagation());
        tag.addEventListener('click', () => this.onPick(n));
        tag.addEventListener('dblclick', () => this.onEdit(n));
        this.tags.set(n.id, tag);
        this.track.append(tag);
      });
    });
  }

  markSelection() {
    for (const [id, tag] of this.tags) tag.classList.toggle('sel', id === app.selectedId);
  }

  renderPlayhead() {
    const x = this.x(this.playheadTime());
    const visible = x >= -1 && x <= this.width + 1;
    this.playhead.style.display = visible ? 'block' : 'none';
    if (visible) this.playhead.style.transform = `translateX(${Math.round(x)}px)`;
  }

  renderScroll() {
    const dur = this.duration;
    const zoomed = this.view.span < dur - 1e-6;
    this.scroll.hidden = !zoomed;
    if (!zoomed) return;
    const w = this.scroll.clientWidth;
    const tw = Math.max(24, (this.view.span / dur) * w);
    const left = (this.view.start / (dur - this.view.span)) * (w - tw);
    this.thumb.style.width = `${tw}px`;
    this.thumb.style.transform = `translateX(${left}px)`;
  }

  onFrame() {
    if (!app.video || !this.view.span) return;
    const t = this.playheadTime();
    const { start, span } = this.view;
    if (span < this.duration && (t < start || t > start + span)) {
      // Page while playing, centre after a jump.
      if (app.playing) this.setView(t - span * 0.05, span);
      else this.setView(t - span / 2, span);
      return;
    }
    this.renderPlayhead();
  }

  onWheel(e) {
    if (!app.video || !this.duration) return;
    e.preventDefault();
    const pan = e.shiftKey || Math.abs(e.deltaX) > Math.abs(e.deltaY);
    const d = pan ? (e.deltaX || e.deltaY) : e.deltaY;
    const unit = e.deltaMode === 1 ? 33 : e.deltaMode === 2 ? 400 : 1;
    if (pan) {
      this.setView(this.view.start + ((d * unit) / 1000) * this.view.span, this.view.span);
    } else {
      const r = this.body.getBoundingClientRect();
      const t = this.timeAt(e.clientX - r.left);
      const factor = Math.exp(d * unit * 0.0015);
      const span = clamp(this.view.span * factor, Math.min(this.duration, 12 / (this.rate || 30)), this.duration);
      this.setView(t - (t - this.view.start) * (span / this.view.span), span);
    }
  }

  onScrubStart(e) {
    if (e.button !== 0 || !app.video || !this.player.loaded) return;
    if (e.target.closest('.tl-tag')) return;
    e.preventDefault();
    this.body.setPointerCapture(e.pointerId);
    this.root.classList.add('scrubbing');
    const r = this.body.getBoundingClientRect();
    let pending = null;
    let raf = 0;
    const seekTo = (clientX) => {
      pending = clamp(this.timeAt(clientX - r.left), 0, this.duration);
      if (raf) return;
      raf = requestAnimationFrame(() => {
        raf = 0;
        const rate = this.player.rate;
        this.player.seekFrame(Math.floor(pending * rate + 1e-4));
      });
    };
    seekTo(e.clientX);
    const move = (ev) => seekTo(ev.clientX);
    const up = (ev) => {
      this.body.removeEventListener('pointermove', move);
      this.body.removeEventListener('pointerup', up);
      this.body.removeEventListener('pointercancel', up);
      if (this.body.hasPointerCapture(ev.pointerId)) this.body.releasePointerCapture(ev.pointerId);
      this.root.classList.remove('scrubbing');
    };
    this.body.addEventListener('pointermove', move);
    this.body.addEventListener('pointerup', up);
    this.body.addEventListener('pointercancel', up);
  }

  onThumbStart(e) {
    e.preventDefault();
    e.stopPropagation();
    this.thumb.setPointerCapture(e.pointerId);
    const x0 = e.clientX;
    const start0 = this.view.start;
    const w = this.scroll.clientWidth;
    const tw = this.thumb.offsetWidth;
    const range = this.duration - this.view.span;
    const move = (ev) => {
      const dx = ev.clientX - x0;
      this.setView(start0 + (dx / Math.max(1, w - tw)) * range, this.view.span);
    };
    const up = () => {
      this.thumb.removeEventListener('pointermove', move);
      this.thumb.removeEventListener('pointerup', up);
    };
    this.thumb.addEventListener('pointermove', move);
    this.thumb.addEventListener('pointerup', up);
  }

  showCard(n, x) {
    clearTimeout(this.cardTimer);
    const c = this.card;
    c.querySelector('.tc-card-tc').textContent = n.timecode || '';
    const badge = c.querySelector('.badge');
    badge.textContent = n.kind;
    badge.className = `badge k-${n.kind}`;
    const text = c.querySelector('.tc-card-text');
    text.textContent = n.text || 'No text';
    text.classList.toggle('muted', !n.text);
    const img = c.querySelector('img');
    if (n.image_url) {
      img.src = n.image_url;
      img.hidden = false;
    } else {
      img.removeAttribute('src');
      img.hidden = true;
    }
    c.hidden = false;
    const cw = c.offsetWidth;
    const ax = x + this.body.offsetLeft;
    c.style.left = `${clamp(ax - 16, 8, Math.max(8, this.root.clientWidth - cw - 8))}px`;
  }

  hideCard() {
    clearTimeout(this.cardTimer);
    this.cardTimer = setTimeout(() => {
      this.card.hidden = true;
    }, 80);
  }
}
