// Toasts (bottom centre, 2.5 s), the closable error banner at the top, and small popover helpers.

import { app, el } from './app.js';
import { api } from './bridge.js';

const TOAST_MS = 2500;
const MAX_TOASTS = 3;

export function toast(message) {
  const box = document.getElementById('toasts');
  const node = el('div', 'toast', message);
  node.setAttribute('role', 'status');
  box.append(node);
  while (box.children.length > MAX_TOASTS) box.firstElementChild.remove();
  requestAnimationFrame(() => node.classList.add('in'));
  setTimeout(() => {
    node.classList.remove('in');
    setTimeout(() => node.remove(), 200);
  }, TOAST_MS);
}

export function showError(message) {
  const banner = document.getElementById('banner');
  document.getElementById('banner-text').textContent = message;
  banner.hidden = false;
}

export function hideError() {
  document.getElementById('banner').hidden = true;
}

/** Runs a bridge call and shows its rejection in the banner. Resolves with the result or undefined. */
export async function guard(promise) {
  try {
    return await promise;
  } catch (err) {
    showError(err && err.message ? err.message : String(err));
    return undefined;
  }
}

/** A timecode that copies itself on click (see `initTimecodeCopy`). Static ones in index.html use the same markup. */
export function timecodeButton(text, cls = '') {
  const b = el('button', `tc mono ${cls}`.trim(), text);
  b.type = 'button';
  b.title = 'Click to copy';
  return b;
}

const DRAG_PX = 4;

/** One delegated handler for every `.tc` control: a plain click copies the text as shown and says so in a toast. */
export function initTimecodeCopy() {
  let down = null;
  document.addEventListener('pointerdown', (e) => {
    down = { x: e.clientX, y: e.clientY };
  }, true);
  // Capture phase: runs before the row, timeline and transport handlers, which must not see the click.
  document.addEventListener('click', async (e) => {
    const b = e.target instanceof Element ? e.target.closest('.tc') : null;
    if (!b) return;
    e.stopPropagation();
    const dragged = e.detail > 0 && down && Math.hypot(e.clientX - down.x, e.clientY - down.y) > DRAG_PX;
    if (e.detail > 0) b.blur(); // keep Space as play / pause after a mouse click, like every other button
    const text = b.textContent.trim();
    if (dragged || !text) return;
    const ok = await guard(api.copy_text(text));
    if (ok !== undefined) {
      toast(`Copied ${text}`);
      app.emit('tc-copied');
    }
  }, true);
  document.addEventListener('dblclick', (e) => {
    if (e.target instanceof Element && e.target.closest('.tc')) e.stopPropagation();
  }, true);
}

/** Popover anchored under a button; closes on outside click and Esc. Returns {open, close, toggle}. */
export function popover(button, panel, { onOpen } = {}) {
  const close = () => {
    panel.hidden = true;
    button.setAttribute('aria-expanded', 'false');
  };
  const open = () => {
    panel.hidden = false;
    button.setAttribute('aria-expanded', 'true');
    if (onOpen) onOpen();
  };
  button.addEventListener('click', () => (panel.hidden ? open() : close()));
  document.addEventListener('pointerdown', (e) => {
    if (!panel.hidden && !panel.contains(e.target) && !button.contains(e.target)) close();
  });
  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape' && !panel.hidden) {
      close();
      e.stopPropagation();
    }
  }, true);
  return { open, close, toggle: () => (panel.hidden ? open() : close()), get isOpen() { return !panel.hidden; } };
}
