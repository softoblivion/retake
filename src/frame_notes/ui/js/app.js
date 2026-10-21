// UI state hub. `app.state` is the last State the shell sent and is never edited here; everything else is
// what the UI keeps on its own: playhead, playback flag, selection, the note being created or edited, zoom.

import { timecodeBase, formatTimecode, formatClock } from './frames.js';

export const KINDS = ['FIX', 'ADD', 'REMOVE'];
export const KIND_COLORS = { FIX: '#EF9F27', ADD: '#639922', REMOVE: '#E24B4A' };
export const KIND_KEYS = { 1: 'FIX', 2: 'ADD', 3: 'REMOVE' };

const listeners = new Map();

export const app = {
  state: null,        // State from the shell (docs/BRIDGE_API.md section 3)
  tb: null,           // timecode base of the effective frame rate, null while the rate is unknown
  fps: null,          // exact rate for frame <-> seconds, null while unknown
  frame: 0,           // current frame (zero-based)
  time: 0,            // current time in seconds (display while the rate is unknown)
  playing: false,
  selectedId: null,
  edit: null,         // note being created or edited, see js/composer.js
  panelOpen: true,
  update: null,       // last UpdateStatus

  on(name, fn) {
    if (!listeners.has(name)) listeners.set(name, new Set());
    listeners.get(name).add(fn);
  },
  emit(name, data) {
    const set = listeners.get(name);
    if (!set) return;
    for (const fn of Array.from(set)) {
      try {
        fn(data);
      } catch (err) {
        console.error(`UI listener "${name}" failed`, err);
      }
    }
  },

  get video() {
    return this.state ? this.state.video : null;
  },
  get notes() {
    return (this.state && this.state.notes) || [];
  },
  note(id) {
    return this.notes.find((n) => n.id === id) || null;
  },

  setState(state) {
    const prevFps = this.fps;
    this.state = state;
    const v = state && state.video;
    if (v && v.fps && v.fps_source !== 'unknown') {
      this.tb = timecodeBase(v.fps);
      this.fps = this.tb.fps;
    } else {
      this.tb = null;
      this.fps = null;
    }
    if (this.selectedId && !this.note(this.selectedId)) this.selectedId = null;
    this.emit('state', { fpsChanged: prevFps !== this.fps });
  },

  setFrame(frame, time) {
    const changed = frame !== this.frame || (this.fps == null && time !== this.time);
    this.frame = frame;
    if (time != null) this.time = time;
    if (changed) this.emit('frame', frame);
  },

  select(id) {
    if (id === this.selectedId) return;
    this.selectedId = id;
    this.emit('select', id);
  },

  /** Live display of a frame: AE timecode when the rate is known, else a clock of `time`. */
  format(frame, time) {
    if (this.tb) return formatTimecode(frame, this.tb);
    return formatClock(time != null ? time : 0);
  },
};

/** First words of a note text for labels. */
export function shortText(text, words = 6) {
  const parts = String(text || '').trim().split(/\s+/).filter(Boolean);
  if (!parts.length) return '';
  return parts.slice(0, words).join(' ') + (parts.length > words ? '…' : '');
}

export function el(tag, cls, text) {
  const node = document.createElement(tag);
  if (cls) node.className = cls;
  if (text != null) node.textContent = text;
  return node;
}

export function clamp(v, lo, hi) {
  return Math.min(hi, Math.max(lo, v));
}
