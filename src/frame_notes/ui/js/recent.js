// Recent entries, shared by the empty state list and the header Recent menu, and the entry context menu
// (docs/UI.md section 2a): Open, Reveal folder, Delete notes (inline confirmation), Remove from recent.

import { el, clamp } from './app.js';

const CONFIRM_RESET_MS = 3000;

function plural(n, word) {
  return `${n} ${word}${n === 1 ? '' : 's'}`;
}

/** `3 notes · 2 frames`, `1 note`, `no notes`; '' when the video has no notes folder. */
export function countsText(r) {
  if (r.notes_count == null) return '';
  if (!r.notes_count && !r.frames_count) return 'no notes';
  const parts = [plural(r.notes_count, 'note')];
  if (r.frames_count) parts.push(plural(r.frames_count, 'frame'));
  return parts.join(' · ');
}

/** Question on the armed Delete item: `Delete 3 notes and 2 frames?`. */
export function deleteQuestion(r) {
  const notes = r.notes_count || 0;
  const frames = r.frames_count || 0;
  if (!notes && !frames) return 'Delete the empty notes folder?';
  const parts = [plural(notes, 'note')];
  if (frames) parts.push(plural(frames, 'frame'));
  return `Delete ${parts.join(' and ')}?`;
}

function dirOf(path) {
  const i = Math.max(path.lastIndexOf('\\'), path.lastIndexOf('/'));
  return i > 0 ? path.slice(0, i) : '';
}

/** Fills `list` (a <ul>) with one row per recent entry. */
export function renderRecent(list, recent) {
  list.replaceChildren();
  for (const r of recent) {
    const item = el('li', 'recent-item');
    item.dataset.path = r.path;
    item.title = r.exists === false ? `${r.path} (not found)` : r.path;
    if (r.exists === false) item.classList.add('missing');
    if (r.is_open) item.classList.add('is-open');
    item.tabIndex = 0;
    const main = el('div', 'ri-main');
    const name = el('span', 'ri-name', r.name);
    if (r.is_open) name.append(el('span', 'ri-open', 'open'));
    main.append(name, el('span', 'ri-path muted', dirOf(r.path)));
    const meta = el('div', 'ri-meta');
    if (r.last_frame) meta.append(el('span', 'ri-pos mono muted', `frame ${r.last_frame}`));
    const counts = countsText(r);
    if (counts) meta.append(el('span', 'ri-counts muted', counts));
    item.append(main, meta);
    list.append(item);
  }
}

/**
 * Context menu of recent entries. `entries()` returns the current recent list; `actions` provides
 * openPath, revealRecent, deleteNotes and forgetRecent. Returns {attach(list, {beforeOpen}), close}.
 */
export function initRecentMenu({ entries, actions }) {
  const menu = document.getElementById('ctx-menu');
  let current = null;       // the entry the menu is open for
  let resetTimer = 0;

  const find = (path) => entries().find((r) => r.path === path) || null;

  function item(label, act, disabled, title) {
    const b = el('button', null, label);
    b.type = 'button';
    b.setAttribute('role', 'menuitem');
    b.dataset.act = act;
    b.disabled = !!disabled;
    if (title) b.title = title;
    return b;
  }

  function build(r) {
    const missing = r.exists === false;
    const noNotes = r.notes_count == null;
    menu.replaceChildren(
      item('Open', 'open', missing),
      item('Reveal folder', 'reveal', missing && !r.notes_dir),
      item('Delete notes', 'delete', noNotes, noNotes ? 'This video has no notes folder' : ''),
      el('div', 'menu-sep'),
      item('Remove from recent', 'forget', r.is_open, r.is_open ? 'The open video stays in the list' : ''),
    );
  }

  function open(r, x, y) {
    current = r;
    build(r);
    menu.hidden = false;
    const w = menu.offsetWidth;
    const h = menu.offsetHeight;
    menu.style.left = `${clamp(x, 4, window.innerWidth - w - 4)}px`;
    menu.style.top = `${clamp(y, 4, window.innerHeight - h - 4)}px`;
    const first = menu.querySelector('button:not(:disabled)');
    if (first) first.focus({ preventScroll: true });
  }

  function close() {
    clearTimeout(resetTimer);
    if (menu.hidden) return;
    menu.hidden = true;
    current = null;
  }

  function disarm() {
    clearTimeout(resetTimer);
    const b = menu.querySelector('[data-act="delete"].armed');
    if (!b) return false;
    b.classList.remove('armed');
    b.textContent = 'Delete notes';
    return true;
  }

  menu.addEventListener('pointerdown', (e) => e.stopPropagation());
  menu.addEventListener('contextmenu', (e) => e.preventDefault());
  menu.addEventListener('click', async (e) => {
    const b = e.target.closest('button[data-act]');
    if (!b || b.disabled || !current) return;
    const r = current;
    const act = b.dataset.act;
    if (act === 'delete' && !b.classList.contains('armed')) {
      b.classList.add('armed');
      b.textContent = deleteQuestion(r);
      return;
    }
    close();
    if (act === 'open') actions.openPath(r.path);
    else if (act === 'reveal') actions.revealRecent(r.path);
    else if (act === 'delete') actions.deleteNotes(r.path);
    else if (act === 'forget') actions.forgetRecent(r.path);
  });
  menu.addEventListener('pointerover', (e) => {
    if (e.target.closest('[data-act="delete"]')) clearTimeout(resetTimer);
  });
  menu.addEventListener('pointerout', (e) => {
    const b = e.target.closest('[data-act="delete"].armed');
    if (b && !b.contains(e.relatedTarget)) {
      clearTimeout(resetTimer);
      resetTimer = setTimeout(disarm, CONFIRM_RESET_MS);
    }
  });

  // Window capture runs before the popover and keyboard handlers: Esc disarms, then closes, and goes no further.
  window.addEventListener('keydown', (e) => {
    if (menu.hidden) return;
    if (e.key === 'Escape') {
      e.preventDefault();
      e.stopPropagation();
      if (!disarm()) close();
    } else if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault();
      e.stopPropagation();
      const items = Array.from(menu.querySelectorAll('button:not(:disabled)'));
      const i = items.indexOf(document.activeElement);
      const next = items[(i + (e.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length];
      if (next) next.focus();
    } else if (e.key === 'Tab') {
      close();
    }
  }, true);
  document.addEventListener('pointerdown', (e) => {
    if (!menu.contains(e.target)) close();
  }, true);
  window.addEventListener('blur', close);
  window.addEventListener('resize', close);
  document.addEventListener('wheel', (e) => {
    if (!menu.contains(e.target)) close();
  }, { capture: true, passive: true });

  function attach(list, { beforeOpen } = {}) {
    list.addEventListener('click', (e) => {
      const row = e.target.closest('.recent-item');
      if (!row || row.classList.contains('missing')) return;
      if (beforeOpen) beforeOpen();
      actions.openPath(row.dataset.path);
    });
    list.addEventListener('keydown', (e) => {
      const row = e.target.closest('.recent-item');
      if (row && e.key === 'Enter') {
        e.preventDefault();
        row.click();
      }
    });
    list.addEventListener('contextmenu', (e) => {
      const row = e.target.closest('.recent-item');
      if (!row) return;
      e.preventDefault();
      e.stopPropagation();
      const r = find(row.dataset.path);
      if (!r) return;
      let { clientX: x, clientY: y } = e;
      if (e.button !== 2 || (x === 0 && y === 0)) {
        // Context menu key or Shift+F10: open at the row.
        const box = row.getBoundingClientRect();
        x = box.left + 24;
        y = box.bottom - 4;
      }
      row.focus({ preventScroll: true });
      open(r, x, y);
    });
  }

  return { attach, close };
}
