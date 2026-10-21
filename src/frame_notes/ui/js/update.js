// Self-update chip at the right of the header and its popover (UpdateStatus of docs/BRIDGE_API.md).

import { app } from './app.js';
import { api } from './bridge.js';
import { guard, popover } from './ui.js';

export function initUpdate({ actions }) {
  const $ = (id) => document.getElementById(id);
  const chip = $('version');
  const chipText = $('version-text');
  const pop = $('update-pop');
  const msg = $('up-message');
  const notes = $('up-notes');
  const btnCheck = $('up-check');
  const btnDownload = $('up-download');
  const btnRestart = $('up-restart');
  const auto = $('up-auto');
  let closing = false;

  popover(chip, pop, { onOpen: render });

  const apply = (status) => {
    if (status && typeof status === 'object' && status.stage) setStatus(status);
  };
  btnCheck.addEventListener('click', async () => apply(await guard(api.check_updates())));
  btnDownload.addEventListener('click', async () => apply(await guard(api.download_update())));
  btnRestart.addEventListener('click', async () => {
    const ok = await guard(api.apply_update());
    if (ok === true) {
      closing = true;
      render();
    }
  });
  auto.addEventListener('change', async () => {
    const st = await guard(api.set_setting('auto_download_updates', auto.checked));
    if (st) actions.applyState(st);
  });

  function setStatus(status) {
    app.update = status;
    render();
  }

  function render() {
    const u = app.update;
    const current = (u && u.current) || (app.state && app.state.version) || '';
    const stage = u ? u.stage : 'idle';
    chip.className = 'version-chip';
    chip.title = '';
    let text = current ? `v${current}` : '';
    if (stage === 'available') {
      text = `v${u.latest} available`;
      chip.classList.add('accent');
    } else if (stage === 'downloading') {
      text = `Downloading ${Math.round((u.progress || 0) * 100)}%`;
      chip.classList.add('accent');
    } else if (stage === 'ready' && u.can_apply) {
      text = 'Restart to update';
      chip.classList.add('primary');
    } else if (stage === 'ready') {
      text = `v${u.latest} downloaded`;
      chip.classList.add('accent');
    } else if (stage === 'error') {
      chip.classList.add('warn');
      chip.title = u.message || '';
    } else if (stage === 'checking') {
      chip.classList.add('busy');
    }
    chipText.textContent = text;

    msg.textContent = (u && u.message) || (current ? `Frame Notes v${current}` : '');
    const body = u && u.notes && ['available', 'downloading', 'ready'].includes(stage) ? u.notes : '';
    notes.textContent = body;
    notes.hidden = !body;
    const busy = stage === 'checking' || stage === 'downloading' || closing;
    btnCheck.disabled = busy;
    btnDownload.disabled = closing || stage !== 'available';
    btnRestart.disabled = closing || !(stage === 'ready' && u.can_apply);
    auto.checked = !!(app.state && app.state.settings && app.state.settings.auto_download_updates);
  }

  app.on('state', render);
  return { setStatus, render };
}
