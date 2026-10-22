// Keyboard map (docs/UI.md, "Keyboard"). Active when no text field has focus; keys never scroll the page.

import { app, KIND_KEYS } from './app.js';

const NAV_KEYS = new Set([' ', 'ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End', 'PageUp',
  'PageDown']);

function isTyping(target) {
  return target instanceof Element && !!target.closest('input, textarea, select, [contenteditable="true"]');
}

function hasTextSelection() {
  const s = window.getSelection();
  return !!s && !s.isCollapsed && String(s).trim() !== '';
}

/** Letter shortcuts match the physical key (any layout) or, without a code, the character. */
function isKey(e, code, char) {
  return e.code ? e.code === code : String(e.key).toLowerCase() === char;
}

export function initKeys({ player, composer, timeline, actions }) {
  document.addEventListener('keydown', (e) => {
    if (e.defaultPrevented || isTyping(e.target)) return;
    // A focused timecode control copies on Enter / Space; those keys are not shortcuts there.
    if ((e.key === 'Enter' || e.key === ' ') && e.target instanceof Element && e.target.closest('.tc')) return;

    if (actions.helpOpen()) {
      if (e.key === 'Escape' || e.key === '?') {
        e.preventDefault();
        actions.toggleHelp(false);
        return;
      }
    }

    if (app.edit) {
      if (e.key === 'Enter') composer.commit();
      else if (e.key === 'Escape') composer.cancel();
      if (NAV_KEYS.has(e.key) || e.key === 'Enter' || e.key === 'Escape') e.preventDefault();
      return;
    }

    const ctrl = e.ctrlKey || e.metaKey;
    if (ctrl && !e.altKey) {
      if (isKey(e, 'KeyO', 'o')) {
        e.preventDefault();
        actions.open();
      } else if (isKey(e, 'KeyC', 'c') && !hasTextSelection()) {
        e.preventDefault();
        if (e.shiftKey) actions.copyReadable();
        else actions.copy();
      }
      return;
    }
    if (e.altKey) return;

    if (e.key === '?' || (e.code === 'Slash' && e.shiftKey)) {
      e.preventDefault();
      actions.toggleHelp();
      return;
    }

    const hasVideo = !!app.video && player.loaded;
    if (NAV_KEYS.has(e.key)) e.preventDefault();
    if (!hasVideo) return;

    switch (e.key) {
      case ' ':
        player.toggle();
        break;
      case 'ArrowLeft':
        player.step(e.shiftKey ? -10 : -1);
        break;
      case 'ArrowRight':
        player.step(e.shiftKey ? 10 : 1);
        break;
      case 'Home':
        player.seekFrame(0);
        break;
      case 'End':
        player.seekFrame(player.lastFrame);
        break;
      case 'ArrowUp':
        actions.selectNeighbour(-1);
        break;
      case 'ArrowDown':
        actions.selectNeighbour(1);
        break;
      case '1':
      case '2':
      case '3':
        e.preventDefault();
        composer.create(KIND_KEYS[e.key]);
        break;
      case 'F2':
        e.preventDefault();
        actions.editSelected();
        break;
      case 'Delete':
        e.preventDefault();
        actions.removeSelected();
        break;
      case '0':
        e.preventDefault();
        timeline.resetZoom();
        break;
      default:
        break;
    }
  });
}
