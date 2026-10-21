// Video element control: frame-accurate seeking and stepping, play / pause, the live frame counter.
// Seeks go to the frame centre and the frame is read back only after `seeked`; during playback the counter
// follows requestVideoFrameCallback (the frame actually presented), with a requestAnimationFrame fallback.

import { app, clamp } from './app.js';
import { frameAt, frameCentre, nominalRate } from './frames.js';

const FALLBACK_FPS = 30;   // stepping rate while the frame rate is unknown (display is a clock then)

export class Player {
  constructor(video) {
    this.v = video;
    this.url = null;
    this.startFrame = 0;
    this.target = null;      // frame of the seek in flight
    this.waiters = [];
    this.presented = null;   // last frame presented during playback
    this.loopId = 0;
    this.samples = [];       // presentation times for measuring an unknown rate
    this.hasRVFC = typeof HTMLVideoElement !== 'undefined'
      && 'requestVideoFrameCallback' in HTMLVideoElement.prototype;

    video.addEventListener('seeked', () => this.onSeeked(false));
    video.addEventListener('play', () => {
      app.playing = true;
      this.presented = null;
      app.emit('playing', true);
      this.startLoop();
    });
    video.addEventListener('pause', () => {
      app.playing = false;
      app.emit('playing', false);
    });
    video.addEventListener('ended', () => {
      this.seekFrame(this.lastFrame);
    });
    video.addEventListener('loadedmetadata', () => {
      app.emit('metadata');
      this.seekFrame(this.startFrame);
    });
    video.addEventListener('durationchange', () => app.emit('metadata'));
    video.addEventListener('error', () => {
      if (!this.url) return;
      app.emit('video-error', this.v.error);
    });
  }

  get rate() {
    return app.fps || FALLBACK_FPS;
  }

  get loaded() {
    return !!this.url && this.v.readyState >= 1;
  }

  get duration() {
    const d = this.v.duration;
    if (Number.isFinite(d) && d > 0) return d;
    const sv = app.video;
    return (sv && sv.duration) || 0;
  }

  get lastFrame() {
    const sv = app.video;
    if (app.fps && sv && sv.duration_frames) return Math.max(0, sv.duration_frames - 1);
    return Math.max(0, Math.round(this.duration * this.rate) - 1);
  }

  currentFrame() {
    return clamp(frameAt(this.v.currentTime, this.rate), 0, this.lastFrame);
  }

  load(url, startFrame) {
    if (url === this.url) return;
    this.url = url || null;
    this.startFrame = startFrame || 0;
    this.samples = [];
    this.target = null;
    this.flushWaiters(0);
    if (url) {
      this.v.src = url;
      this.v.load();
    } else {
      this.v.pause();
      this.v.removeAttribute('src');
      this.v.load();
    }
  }

  /** Seeks to a frame (pausing playback) and resolves with the frame read back after `seeked`. */
  seekFrame(frame) {
    if (!this.loaded) return Promise.resolve(app.frame);
    const f = clamp(Math.round(frame), 0, this.lastFrame);
    if (!this.v.paused) this.v.pause();
    this.target = f;
    app.setFrame(f, frameCentre(f, this.rate));
    return new Promise((resolve) => {
      this.waiters.push(resolve);
      this.v.currentTime = frameCentre(f, this.rate);
      clearTimeout(this.seekTimer);
      this.seekTimer = setTimeout(() => this.onSeeked(true), 2500);
    });
  }

  onSeeked(force) {
    if (this.v.seeking && !force) return;
    clearTimeout(this.seekTimer);
    if (!this.v.paused && !force) return;
    const f = this.currentFrame();
    this.target = null;
    app.setFrame(f, this.v.currentTime);
    this.flushWaiters(f);
  }

  flushWaiters(f) {
    const w = this.waiters;
    this.waiters = [];
    for (const resolve of w) resolve(f);
  }

  /** Resolves when no seek is in flight, with the settled frame. */
  settle() {
    if (this.target == null && !this.v.seeking) return Promise.resolve(app.frame);
    return new Promise((resolve) => this.waiters.push(resolve));
  }

  step(n) {
    const base = this.target != null ? this.target : app.frame;
    return this.seekFrame(base + n);
  }

  play() {
    if (!this.loaded) return;
    const start = () => this.v.play().catch((err) => {
      if (err && err.name !== 'AbortError') app.emit('error', 'The video could not be played.');
    });
    if (this.currentFrame() >= this.lastFrame) this.seekFrame(0).then(start);
    else start();
  }

  /** Pauses and snaps to the frame that was on screen. Resolves with that frame. */
  pause() {
    if (this.v.paused) return this.settle();
    const shown = this.presented != null ? this.presented : this.currentFrame();
    this.v.pause();
    return this.seekFrame(shown);
  }

  toggle() {
    if (this.v.paused) this.play();
    else this.pause();
  }

  startLoop() {
    const id = ++this.loopId;
    if (this.hasRVFC) {
      const cb = (now, meta) => {
        if (id !== this.loopId || this.v.paused) return;
        const f = clamp(frameAt(meta.mediaTime, this.rate), 0, this.lastFrame);
        this.presented = f;
        app.setFrame(f, meta.mediaTime);
        if (!app.fps) this.sample(meta.mediaTime);
        this.v.requestVideoFrameCallback(cb);
      };
      this.v.requestVideoFrameCallback(cb);
    } else {
      const tick = () => {
        if (id !== this.loopId || this.v.paused) return;
        const f = this.currentFrame();
        this.presented = f;
        app.setFrame(f, this.v.currentTime);
        requestAnimationFrame(tick);
      };
      requestAnimationFrame(tick);
    }
  }

  /** Measures the rate from presentation times while it is unknown (ARCHITECTURE section 7). */
  sample(mediaTime) {
    const s = this.samples;
    if (s.length && mediaTime <= s[s.length - 1]) {
      s.length = 0;
    }
    s.push(mediaTime);
    if (s.length < 25) return;
    const deltas = [];
    for (let i = 1; i < s.length; i++) deltas.push(s[i] - s[i - 1]);
    deltas.sort((a, b) => a - b);
    // The smallest regular delta is one frame; skipped frames only make deltas longer.
    const d = deltas[Math.floor(deltas.length * 0.25)];
    s.length = 0;
    if (d > 0) {
      try {
        app.emit('measured', nominalRate(1 / d));
      } catch {
        /* not a usable rate */
      }
    }
  }
}
