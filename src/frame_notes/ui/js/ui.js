// Toasts (bottom centre, 2.5 s), the closable error banner at the top, and small popover helpers.

import { el } from './app.js';

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
