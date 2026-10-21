// Empty state (drop target, Open video, recent files) and drag and drop of a file onto the window.

import { app } from './app.js';
import { isMock } from './bridge.js';
import { renderRecent } from './recent.js';

const VIDEO_EXT = /\.(mp4|m4v|mov|webm|mkv|ogv|ogg)$/i;

export function initEmpty({ actions, recentMenu }) {
  const $ = (id) => document.getElementById(id);
  const list = $('recent-list');
  const recentBox = $('recent');
  const veil = $('drop-veil');

  $('empty-open').addEventListener('click', () => actions.open());

  if (isMock) {
    const pick = $('mock-file');
    pick.hidden = false;
    pick.querySelector('input').addEventListener('change', (e) => {
      const f = e.target.files && e.target.files[0];
      if (f) actions.openPath(mockUrl(f));
      e.target.value = '';
    });
  }

  recentMenu.attach(list);

  function render() {
    const recent = (app.state && app.state.recent) || [];
    recentBox.hidden = recent.length === 0;
    renderRecent(list, recent);
  }

  // Drag and drop anywhere on the window.
  let depth = 0;
  const hasFiles = (e) => e.dataTransfer && Array.from(e.dataTransfer.types || []).includes('Files');
  window.addEventListener('dragenter', (e) => {
    if (!hasFiles(e)) return;
    e.preventDefault();
    depth += 1;
    veil.hidden = false;
  });
  window.addEventListener('dragleave', (e) => {
    if (!hasFiles(e)) return;
    depth = Math.max(0, depth - 1);
    if (!depth) veil.hidden = true;
  });
  window.addEventListener('dragover', (e) => {
    if (!hasFiles(e)) return;
    e.preventDefault();
    e.dataTransfer.dropEffect = 'copy';
  });
  window.addEventListener('drop', (e) => {
    e.preventDefault();
    depth = 0;
    veil.hidden = true;
    const files = Array.from((e.dataTransfer && e.dataTransfer.files) || []);
    if (!files.length) return;
    const file = files.find((f) => VIDEO_EXT.test(f.name)) || files[0];
    if (file.pywebviewFullPath) actions.openPath(file.pywebviewFullPath);
    else if (isMock) actions.openPath(mockUrl(file));
    // In the real window the shell reads the dropped path itself and sends `open_request` (BRIDGE_API.md, 5).
  });

  app.on('state', render);
  return { render };
}

/** Object URL of a local file for the mock; the name travels in the fragment. */
function mockUrl(file) {
  return `${URL.createObjectURL(file)}#${encodeURIComponent(file.name)}`;
}

