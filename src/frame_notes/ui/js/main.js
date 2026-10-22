// Entry point: wires the bridge, the player, the region overlay, the note editor, the timeline, the notes
// panel, the header and the keyboard.

import { app, KIND_KEYS, clamp } from './app.js';
import { api, on, connect, isMock } from './bridge.js';
import { formatTimecode, formatClock } from './frames.js';
import { Player } from './player.js';
import { Overlay } from './overlay.js';
import { Composer } from './composer.js';
import { Timeline } from './timeline.js';
import { NotesPanel } from './notes.js';
import { initHeader } from './header.js';
import { initUpdate } from './update.js';
import { initEmpty } from './empty.js';
import { initRecentMenu } from './recent.js';
import { initKeys } from './keys.js';
import { toast, showError, hideError, guard, initTimecodeCopy } from './ui.js';
import { ICONS } from './icons.js';

const $ = (id) => document.getElementById(id);

const video = $('video');
const stage = $('stage');
const player = new Player(video);
const overlay = new Overlay(stage, video, $('overlay'));
const composer = new Composer({ player, overlay, stage });

function pick(note) {
  app.select(note.id);
  player.seekFrame(note.frame);
}

const timeline = new Timeline({
  root: $('timeline'),
  player,
  onPick: pick,
  onEdit: (note) => composer.edit(note.id),
});
new NotesPanel({ root: $('panel'), composer, onPick: pick });

let lastVideoPath = null;

/** Windows paths compare without case. */
function samePath(a, b) {
  return String(a || '').toLowerCase() === String(b || '').toLowerCase();
}

const actions = {
  applyState(state) {
    if (!state || typeof state !== 'object') return;
    const path = state.video ? state.video.path : null;
    if (path !== lastVideoPath) {
      if (app.edit) composer.cancel();
      app.selectedId = null;
      lastVideoPath = path;
    }
    if (state.settings && typeof state.settings.notes_panel_open === 'boolean') {
      app.panelOpen = state.settings.notes_panel_open;
    }
    const prevFps = app.fps;
    app.setState(state);
    const v = state.video;
    if (v && prevFps !== app.fps && player.loaded && player.url === v.url && !app.playing) {
      // A new rate changes which frame the current time belongs to: snap to that frame's centre.
      player.seekFrame(player.currentFrame());
    }
    document.body.classList.toggle('has-video', !!v);
    document.body.classList.toggle('panel-closed', !app.panelOpen);
    document.title = v ? `Frame Notes - ${v.name}` : 'Frame Notes';
    player.load(v ? v.url : null, v ? v.last_frame || 0 : 0);
    renderTransport();
    refreshOverlay();
  },

  async open() {
    await this.openPath(null);
  },

  async openPath(path) {
    if (app.edit) await composer.commit();
    try {
      const st = await api.open_video(path);
      if (st) {
        hideError();
        this.applyState(st);
      }
    } catch (err) {
      showError(err.message);
    }
  },

  async copy() {
    if (!app.video) return;
    await guard(api.copy_notes_text());
  },

  async copyReadable() {
    if (!app.video) return;
    await guard(api.copy_readable_text());
  },

  revealRecent(path) {
    guard(api.reveal_recent(path));
  },

  async deleteNotes(path) {
    const v = app.video;
    if (v && samePath(v.path, path) && app.edit) composer.cancel();
    const st = await guard(api.delete_notes(path));
    if (st) this.applyState(st);
  },

  async forgetRecent(path) {
    const st = await guard(api.forget_recent(path));
    if (st) this.applyState(st);
  },

  togglePanel() {
    app.panelOpen = !app.panelOpen;
    document.body.classList.toggle('panel-closed', !app.panelOpen);
    app.emit('panel');
    api.set_setting('notes_panel_open', app.panelOpen).catch((err) => showError(err.message));
  },

  helpOpen() {
    return !$('keys-help').hidden;
  },

  toggleHelp(force) {
    const box = $('keys-help');
    box.hidden = force === undefined ? !box.hidden : !force;
  },

  selectNeighbour(dir) {
    const notes = app.notes;
    if (!notes.length) return;
    const i = notes.findIndex((n) => n.id === app.selectedId);
    let next;
    if (i >= 0) next = notes[clamp(i + dir, 0, notes.length - 1)];
    else if (dir > 0) next = notes.find((n) => n.frame >= app.frame) || notes[notes.length - 1];
    else next = [...notes].reverse().find((n) => n.frame <= app.frame) || notes[0];
    if (next) pick(next);
  },

  editSelected() {
    let id = app.selectedId;
    if (!id) {
      const here = app.notes.find((n) => n.frame === app.frame);
      if (here) id = here.id;
    }
    if (id) composer.edit(id);
  },

  async removeSelected() {
    const id = app.selectedId;
    if (!id) return;
    const notes = app.notes;
    const i = notes.findIndex((n) => n.id === id);
    const neighbour = notes[i + 1] || notes[i - 1] || null;
    try {
      await api.remove_note(id);
    } catch (err) {
      showError(err.message);
      return;
    }
    toast('Note removed');
    app.select(neighbour && neighbour.id !== id ? neighbour.id : null);
    refreshOverlay();
  },
};

const recentMenu = initRecentMenu({ entries: () => (app.state && app.state.recent) || [], actions });
initHeader({ actions, recentMenu });
const update = initUpdate({ actions });
initEmpty({ actions, recentMenu });
initKeys({ player, composer, timeline, actions });
initTimecodeCopy();

// Transport row.
const tcCurrent = $('tc-current');
const tcDuration = $('tc-duration');
const frameNum = $('frame-num');
const playBtn = document.querySelector('[data-act="play"]');
for (const btn of document.querySelectorAll('#transport [data-act]')) {
  const act = btn.dataset.act;
  if (ICONS[act]) btn.innerHTML = ICONS[act];
  btn.addEventListener('click', () => {
    if (!app.video || !player.loaded) return;
    if (act === 'first') player.seekFrame(0);
    else if (act === 'prev') player.step(-1);
    else if (act === 'play') player.toggle();
    else if (act === 'next') player.step(1);
    else if (act === 'last') player.seekFrame(player.lastFrame);
  });
}
for (const btn of document.querySelectorAll('[data-kind-key]')) {
  btn.addEventListener('click', () => composer.create(KIND_KEYS[btn.dataset.kindKey]));
}

function renderCounter() {
  tcCurrent.textContent = app.video ? app.format(app.frame, app.fps ? null : video.currentTime) : '';
  frameNum.textContent = app.video && app.fps ? `frame ${app.frame}` : '';
}

function renderTransport() {
  const v = app.video;
  if (!v) {
    tcDuration.textContent = '';
    renderCounter();
    return;
  }
  if (app.tb) {
    const frames = v.duration_frames || Math.round(player.duration * app.fps);
    tcDuration.textContent = formatTimecode(frames, app.tb);
  } else {
    tcDuration.textContent = formatClock(player.duration);
  }
  renderCounter();
}

function renderPlaying() {
  playBtn.innerHTML = app.playing ? ICONS.pause : ICONS.play;
  playBtn.title = app.playing ? 'Pause (Space)' : 'Play (Space)';
  playBtn.setAttribute('aria-label', app.playing ? 'Pause' : 'Play');
}

/** Region on the video: the note being edited, else the selected note while the playhead is on its frame. */
function refreshOverlay() {
  if (app.edit) {
    overlay.show(app.edit.region, app.edit.kind);
    return;
  }
  const n = app.note(app.selectedId);
  if (n && n.region && n.frame === app.frame && !app.playing) overlay.show(n.region, n.kind);
  else overlay.show(null, null);
}

let lastFrameTimer = 0;
app.on('frame', () => {
  renderCounter();
  refreshOverlay();
  if (!app.playing && app.video) {
    clearTimeout(lastFrameTimer);
    lastFrameTimer = setTimeout(() => {
      api.set_last_frame(app.frame).catch(() => {});
    }, 600);
  }
});
app.on('playing', () => {
  renderPlaying();
  refreshOverlay();
});
app.on('select', refreshOverlay);
app.on('edit', refreshOverlay);
app.on('edit-region', refreshOverlay);
app.on('metadata', () => {
  renderTransport();
  overlay.layout();
});
app.on('error', (message) => showError(message));
app.on('video-error', () => {
  showError('This video cannot be played. Supported: H.264 MP4, VP8, VP9 or AV1 WebM.');
});

$('banner-close').addEventListener('click', hideError);
document.querySelector('#panel .np-close').addEventListener('click', () => actions.togglePanel());
$('keys-help').addEventListener('click', (e) => {
  if (e.target === e.currentTarget || e.target.closest('.kh-close')) actions.toggleHelp(false);
});

// Buttons clicked with the mouse give focus back to the page, so Space and the arrows stay playback keys.
document.addEventListener('click', (e) => {
  const b = e.target instanceof Element ? e.target.closest('button') : null;
  if (b && e.detail > 0) b.blur();
});

on('state', (st) => actions.applyState(st));
on('error', (p) => showError((p && p.message) || 'Error'));
on('toast', (p) => p && p.message && toast(p.message));
on('open_request', (p) => p && p.path && actions.openPath(p.path));
on('update', (status) => update.setStatus(status));

// Script errors go to app.log through the bridge (ignored before the bridge is connected).
const logError = (message) => api.log('error', String(message).slice(0, 2000)).catch(() => {});
window.addEventListener('error', (e) => logError(`${e.message} (${e.filename}:${e.lineno})`));
window.addEventListener('unhandledrejection', (e) => logError(e.reason && e.reason.stack ? e.reason.stack : e.reason));

renderPlaying();
if (isMock) document.body.classList.add('mock');

(async () => {
  try {
    const { state, goLive } = await connect({
      onSlow: () => showError('Waiting for the app backend. In a browser, open the page with ?mock=1.'),
    });
    hideError();
    actions.applyState(state);
    goLive();
    const status = await api.update_status().catch(() => null);
    if (status && !app.update) update.setStatus(status);
  } catch (err) {
    showError(err && err.message ? err.message : String(err));
  }
})();
