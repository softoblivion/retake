// The note being created (keys 1 / 2 / 3) or edited (F2, double click): one text field, the region tool on
// the video, save with Enter, cancel with Esc. A click outside the editor saves. app.edit holds:
//   {mode: "create" | "edit", id?, kind, frame, region, origRegion, origText, regionDirty, host: "video" | "row"}

import { app, el, clamp } from './app.js';
import { api } from './bridge.js';
import { grabFrame, encodePng } from './capture.js';
import { regionBox, POINT_R, TAIL } from './region.js';
import { showError } from './ui.js';

const HINT_CREATE = 'Drag to mark a region, click to put a point, or type the note';
const HINT_EDIT = 'Drag to replace the region, click to put a point';
const KEYS_CREATE = 'Enter save · Esc cancel';
const KEYS_EDIT = 'Backspace in an empty field removes the region · Enter save · Esc cancel';

function sameRegion(a, b) {
  if (!a || !b) return !a && !b;
  return a.shape === b.shape && a.x0 === b.x0 && a.y0 === b.y0 && a.x1 === b.x1 && a.y1 === b.y1;
}

export class Composer {
  constructor({ player, overlay, stage }) {
    this.player = player;
    this.overlay = overlay;
    this.stage = stage;
    this.saving = false;
    this.starting = false;

    this.input = el('input', 'note-input');
    this.input.type = 'text';
    this.input.placeholder = 'Note text';
    this.input.spellcheck = true;
    this.input.autocomplete = 'off';
    this.input.setAttribute('aria-label', 'Note text');
    this.input.addEventListener('keydown', (e) => this.onKey(e));

    this.box = document.getElementById('note-editor');
    this.badge = this.box.querySelector('.ne-kind');
    this.tc = this.box.querySelector('.ne-tc');
    this.slot = this.box.querySelector('.ne-slot');
    this.hint = document.getElementById('hint');
    this.hintText = this.hint.querySelector('.hint-text');
    this.hintKeys = this.hint.querySelector('.hint-keys');

    document.addEventListener('pointerdown', (e) => this.onOutside(e), true);
    app.on('layout', () => this.place());
    app.on('tc-copied', () => this.focus()); // copying a timecode must not end the typing
  }

  get active() {
    return !!app.edit;
  }

  /** Starts a new note of `kind` at the current frame (pauses first). */
  async create(kind) {
    if (app.edit || this.starting || this.saving) return;
    if (!app.video || !this.player.loaded) return;
    if (!app.fps) {
      app.emit('need-fps');
      return;
    }
    this.starting = true;
    const frame = await this.player.pause();
    this.starting = false;
    if (app.edit) return;
    app.select(null);
    this.begin({ mode: 'create', kind, frame, region: null, origRegion: null, origText: '', regionDirty: false,
      host: 'video' });
  }

  /** Edits an existing note; `host` "row" puts the field into the notes list, "video" over the video. */
  async edit(id, host) {
    if (this.starting || this.saving) return;
    if (app.edit) {
      if (app.edit.id === id) return;
      await this.commit();
    }
    const n = app.note(id);
    if (!n) return;
    app.select(id);
    this.starting = true;
    if (app.frame !== n.frame || app.playing || this.player.target != null) await this.player.seekFrame(n.frame);
    this.starting = false;
    const region = n.region || null;
    this.begin({ mode: 'edit', id, kind: n.kind, frame: n.frame, region, origRegion: region, origText: n.text || '',
      regionDirty: false, host: host || (app.panelOpen ? 'row' : 'video') });
  }

  begin(edit) {
    app.edit = edit;
    this.input.value = edit.origText;
    this.overlay.setDrawing((r) => this.setRegion(r));
    const create = edit.mode === 'create';
    this.hintText.textContent = create ? HINT_CREATE : HINT_EDIT;
    this.hintKeys.textContent = create ? KEYS_CREATE : KEYS_EDIT;
    this.hint.hidden = false;
    this.badge.textContent = edit.kind;
    this.badge.className = `ne-kind badge k-${edit.kind}`;
    this.tc.textContent = app.format(edit.frame, this.player.v.currentTime);
    this.box.dataset.kind = edit.kind;
    if (edit.host === 'video') {
      this.slot.append(this.input);
      this.box.hidden = false;
    } else {
      this.box.hidden = true;
    }
    app.emit('edit');
    this.place();
    this.focus();
  }

  focus() {
    if (!app.edit) return;
    requestAnimationFrame(() => {
      if (app.edit && this.input.isConnected) {
        this.input.focus({ preventScroll: true });
        const n = this.input.value.length;
        this.input.setSelectionRange(n, n);
      }
    });
  }

  setRegion(region) {
    const e = app.edit;
    if (!e) return;
    e.region = region;
    e.regionDirty = !sameRegion(region, e.origRegion);
    app.emit('edit-region');
    this.place();
    this.focus();
  }

  onKey(e) {
    if (e.isComposing) return;
    if (e.key === 'Enter') {
      e.preventDefault();
      this.commit();
    } else if (e.key === 'Escape') {
      e.preventDefault();
      this.cancel();
    } else if (e.key === 'Backspace' && this.input.value === '' && app.edit && app.edit.region) {
      e.preventDefault();
      this.setRegion(null);
    }
  }

  onOutside(e) {
    if (!app.edit || this.saving || this.starting) return;
    const t = e.target;
    if (!(t instanceof Element)) return;
    if (this.box.contains(t) || t === this.input) return;
    if (this.stage.contains(t) && this.stage.classList.contains('drawing')) return;
    if (app.edit.host === 'row' && t.closest(`[data-id="${app.edit.id}"]`)) return;
    this.commit();
  }

  cancel() {
    this.close();
  }

  close() {
    app.edit = null;
    this.overlay.setDrawing(null);
    this.box.hidden = true;
    this.hint.hidden = true;
    if (document.activeElement === this.input) this.input.blur();
    this.input.remove();
    app.emit('edit');
  }

  /** Saves the note being created or edited. */
  async commit() {
    const e = app.edit;
    if (!e || this.saving) return;
    this.saving = true;
    const text = this.input.value;
    const needImage = !!e.region && (e.mode === 'create' || e.regionDirty);
    let canvas = null;
    let imageError = null;
    try {
      if (needImage) {
        if (app.frame !== e.frame || this.player.target != null || app.playing) await this.player.seekFrame(e.frame);
        const shown = this.overlay.box ? this.overlay.box.w : this.player.v.videoWidth;
        canvas = grabFrame(this.player.v, e.region, e.kind, shown);
      }
    } catch (err) {
      imageError = err;
    }
    this.close();
    try {
      let image_png = null;
      if (canvas) {
        try {
          image_png = await encodePng(canvas);
        } catch (err) {
          imageError = err;
        }
      }
      if (e.mode === 'create') {
        const note = await api.add_note({ frame: e.frame, kind: e.kind, text, region: e.region, image_png });
        if (note && note.id) app.select(note.id);
      } else {
        const patch = { id: e.id };
        let changed = false;
        if (text !== e.origText) {
          patch.text = text;
          changed = true;
        }
        if (e.regionDirty) {
          patch.region = e.region;
          if (e.region && image_png) patch.image_png = image_png;
          changed = true;
        }
        if (changed) await api.update_note(patch);
      }
      if (imageError) showError(`${imageError.message} The note was saved without an image.`);
    } catch (err) {
      showError(err && err.message ? err.message : String(err));
    } finally {
      this.saving = false;
    }
  }

  /** Positions the floating editor next to the region, or above the bottom of the frame without one. */
  place() {
    const e = app.edit;
    if (!e || e.host !== 'video' || this.box.hidden) return;
    const sw = this.stage.clientWidth;
    const sh = this.stage.clientHeight;
    const bw = this.box.offsetWidth;
    const bh = this.box.offsetHeight;
    const hintH = this.hint.hidden ? 0 : this.hint.offsetHeight;
    const gap = 12;
    const m = 8;
    const ob = this.overlay.box;
    let left;
    let top;
    if (e.region && ob) {
      const r = regionBox(e.region, ob.w, ob.h);
      let x0 = ob.x + r.x0;
      let y0 = ob.y + r.y0;
      let x1 = ob.x + r.x1;
      let y1 = ob.y + r.y1;
      if (e.region.shape === 'point') {
        const ext = POINT_R + TAIL;
        x0 -= POINT_R; y0 -= ext; x1 += ext; y1 += POINT_R;
      }
      if (x1 + gap + bw <= sw - m) {
        left = x1 + gap;
        top = y0;
      } else if (x0 - gap - bw >= m) {
        left = x0 - gap - bw;
        top = y0;
      } else if (y1 + gap + bh <= sh - hintH - m) {
        left = x0;
        top = y1 + gap;
      } else {
        left = x0;
        top = y0 - gap - bh;
      }
    } else {
      left = (sw - bw) / 2;
      top = (ob ? ob.y + ob.h : sh) - bh - hintH - 16;
    }
    left = clamp(left, m, Math.max(m, sw - bw - m));
    top = clamp(top, m, Math.max(m, sh - bh - hintH - m));
    this.box.style.left = `${Math.round(left)}px`;
    this.box.style.top = `${Math.round(top)}px`;
  }
}
