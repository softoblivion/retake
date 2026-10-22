// Self-update UI (UpdateStatus of docs/BRIDGE_API.md): the version chip at the right of the header, its popover
// (check, download, automatic download switch) and the update sheet, a modal notice that opens by itself the first
// time a newer version is known. Behaviour of the sheet: docs/UI.md, "Update sheet".

import { app } from './app.js';
import { api } from './bridge.js';
import { guard, popover } from './ui.js';

const NEWER_STAGES = ['available', 'downloading', 'ready'];

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

  const popCtl = popover(chip, pop, { onOpen: render });

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

  // ---- update sheet --------------------------------------------------------------------------------------------
  const sheet = $('update-sheet');
  const panel = $('us-panel');
  const usTitle = $('us-title');
  const usMeta = $('us-meta');
  const usNotes = $('us-notes');
  const usProgress = $('us-progress');
  const usBar = usProgress.querySelector('.us-bar');
  const usPct = $('us-pct');
  const usError = $('us-error');
  const btnLater = $('us-later');
  const btnInstall = $('us-install');

  const announced = new Set(); // versions the sheet was opened for in this run: it never opens by itself twice
  let sheetOpen = false;
  let armed = false;           // Install was pressed: restart as soon as the download is verified
  let sheetError = '';         // a refused call, shown in the sheet
  let retryDownload = false;   // the last failure was a download, so "Try again" downloads instead of checking
  let shownNotes = null;

  const isNewer = (u) => !!u && !!u.latest && NEWER_STAGES.includes(u.stage);

  function openSheet() {
    popCtl.close();
    sheetOpen = true;
    if (app.update && app.update.latest) announced.add(app.update.latest);
    sheet.hidden = false;
    renderSheet();
    panel.focus();
  }

  /** Later: close, disarm a pending install; the version is already in `announced`, so nothing reopens it. */
  function closeSheet() {
    if (!sheetOpen) return;
    sheetOpen = false;
    armed = false;
    sheetError = '';
    sheet.hidden = true;
    chip.focus({ preventScroll: true });
  }

  /** First report of a newer version in this run: open the sheet at once (after a note being typed is done). */
  function maybeAutoOpen() {
    const u = app.update;
    if (sheetOpen || !isNewer(u) || announced.has(u.latest) || app.edit) return;
    openSheet();
  }

  async function startDownload() {
    try {
      apply(await api.download_update());
    } catch (err) {
      armed = false;
      sheetError = err && err.message ? err.message : String(err);
      renderSheet();
    }
  }

  async function installNow() {
    closing = true;
    renderSheet();
    try {
      const ok = await api.apply_update();
      if (ok !== true) {
        closing = false;
        armed = false;
        const u = app.update;
        sheetError = (u && u.message) || 'Nothing to install.';
      }
    } catch (err) {
      closing = false;
      armed = false;
      sheetError = err && err.message ? err.message : String(err);
    }
    render();
  }

  async function onInstall() {
    const u = app.update;
    if (!u || closing) return;
    sheetError = '';
    if (u.stage === 'available') {
      armed = true;
      retryDownload = true;
      renderSheet();
      await startDownload();
    } else if (u.stage === 'ready' && u.can_apply) {
      armed = true;
      await installNow();
    } else if (u.stage === 'error') {
      if (retryDownload && u.latest) {
        armed = true;
        await startDownload();
      } else {
        apply(await guard(api.check_updates()));
      }
    }
  }

  btnInstall.addEventListener('click', onInstall);
  btnLater.addEventListener('click', closeSheet);
  let pressedScrim = false;
  sheet.addEventListener('pointerdown', (e) => {
    pressedScrim = e.target === sheet;
  });
  sheet.addEventListener('click', (e) => {
    if (e.target === sheet && pressedScrim) closeSheet();
  });
  usNotes.addEventListener('click', (e) => {
    const link = e.target instanceof Element ? e.target.closest('a[data-url]') : null;
    if (!link) return;
    e.preventDefault();
    guard(api.open_url(link.dataset.url));
  });

  // The sheet is modal: Esc is Later, Tab stays inside, no other key reaches the shortcuts behind it.
  document.addEventListener('keydown', (e) => {
    if (!sheetOpen) return;
    if (e.key === 'Escape') {
      e.preventDefault();
      closeSheet();
    } else if (e.key === 'Tab') {
      const items = Array.from(panel.querySelectorAll('button:not(:disabled), [tabindex="0"], a[data-url]'));
      const at = items.indexOf(document.activeElement);
      if (items.length) {
        e.preventDefault();
        const step = e.shiftKey ? -1 : 1;
        items[(at < 0 ? (step > 0 ? 0 : items.length - 1) : (at + step + items.length) % items.length)].focus();
      }
    }
    e.stopPropagation();
  }, true);

  // While a newer version is known the chip opens the sheet; otherwise the popover.
  chip.addEventListener('click', (e) => {
    if (!isNewer(app.update)) return;
    e.stopImmediatePropagation();
    e.preventDefault();
    if (!sheetOpen) openSheet();
  }, true);

  app.on('edit', () => {
    if (!app.edit) maybeAutoOpen();
  });

  function renderSheet() {
    const u = app.update;
    if (!u) return;
    const stage = u.stage;
    usTitle.textContent = u.latest ? `Frame Notes ${u.latest}` : 'Frame Notes update';
    const parts = [];
    if (u.current) parts.push(`Installed v${u.current}`);
    if (u.size) parts.push(formatBytes(u.size));
    if (u.published_at) parts.push(formatDate(u.published_at));
    usMeta.textContent = parts.join(' · ');
    usMeta.hidden = !parts.length;

    const body = u.notes || '';
    if (body !== shownNotes) {
      shownNotes = body;
      usNotes.classList.toggle('is-empty', !body);
      if (body) usNotes.innerHTML = renderMarkdown(body, typeof api.open_url === 'function');
      else usNotes.textContent = 'No release notes for this version.';
      usNotes.scrollTop = 0;
    }

    const downloading = stage === 'downloading';
    const p = Math.min(1, Math.max(0, u.progress || 0));
    usProgress.hidden = !downloading;
    if (downloading) {
      usProgress.style.setProperty('--p', String(p));
      usBar.setAttribute('aria-valuenow', String(Math.round(p * 100)));
      usPct.textContent = `${Math.round(p * 100)}%${u.size ? ` · ${formatBytes(u.size * p)} of ${formatBytes(u.size)}` : ''}`;
    }

    const error = sheetError || (stage === 'error' ? u.message || 'The update failed.' : '');
    usError.textContent = error;
    usError.hidden = !error;

    let label = 'Install';
    let disabled = false;
    let tip = '';
    if (closing) {
      label = 'Restarting…';
      disabled = true;
    } else if (stage === 'downloading' || (armed && stage === 'available')) {
      label = `Downloading… ${Math.round(p * 100)}%`;
      disabled = true;
    } else if (stage === 'ready') {
      label = 'Restart to update';
      disabled = !u.can_apply;
      if (disabled) tip = u.message || 'The update can be installed by the packaged app only';
    } else if (stage === 'error') {
      label = 'Try again';
    } else if (stage === 'checking') {
      label = 'Checking…';
      disabled = true;
    } else if (stage !== 'available') {
      disabled = true;
    }
    btnInstall.textContent = label;
    btnInstall.disabled = disabled;
    btnInstall.title = tip;
  }

  // ---- chip and popover ----------------------------------------------------------------------------------------
  function setStatus(status) {
    app.update = status;
    if (status.stage === 'downloading') retryDownload = true;
    render();
    if (sheetOpen) {
      if (isNewer(status) || status.stage === 'error' || status.stage === 'checking') renderSheet();
      else closeSheet(); // no newer version any more (checked again, nothing found)
    }
    if (armed && status.stage === 'ready' && !closing) {
      if (status.can_apply) installNow();
      else armed = false;
    }
    maybeAutoOpen();
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
    const body = u && u.notes && NEWER_STAGES.includes(stage) ? u.notes : '';
    notes.textContent = body;
    notes.hidden = !body;
    const busy = stage === 'checking' || stage === 'downloading' || closing;
    btnCheck.disabled = busy;
    btnDownload.disabled = closing || stage !== 'available';
    btnRestart.disabled = closing || !(stage === 'ready' && u.can_apply);
    const settings = (app.state && app.state.settings) || {};
    auto.checked = !!settings.auto_download_updates;
  }

  app.on('state', render);
  return { setStatus, render };
}

// ---- formatting --------------------------------------------------------------------------------------------------

/** Byte count with 1024-based units: 9_542_041 -> "9.1 MB". */
function formatBytes(b) {
  if (b == null || !Number.isFinite(b)) return '';
  const fixed = (v, digits) => v.toLocaleString('en-US', { minimumFractionDigits: digits, maximumFractionDigits: digits });
  if (Math.abs(b) < 1024) return `${Math.round(b)} B`;
  if (Math.abs(b) < 1024 ** 2) return `${fixed(b / 1024, 0)} KB`;
  if (Math.abs(b) < 1024 ** 3) return `${fixed(b / 1024 ** 2, 1)} MB`;
  return `${fixed(b / 1024 ** 3, 2)} GB`;
}

/** Date of an epoch in seconds: "Oct 21, 2026". */
function formatDate(epoch) {
  return new Date(epoch * 1000).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
}

// ---- release notes ------------------------------------------------------------------------------------------------
// Minimal, safe Markdown for release notes: headings, bullets, paragraphs, **bold**, `code`, `---` rules and
// [text](https://...) links. Everything is HTML-escaped first; nothing else is parsed. Mirrors `renderMarkdown` of
// the shared web kit (liquid_ui, js/update.js) with two differences: a `---` line becomes <hr> (release notes in the
// workspace layout use it) and a link is only clickable when the bridge has `open_url`; without it the link text is
// followed by the address as plain text.

export function renderMarkdown(md, canOpen = false) {
  const lines = String(md || '').replace(/\r\n?/g, '\n').split('\n');
  let html = '';
  let para = [];
  let inList = false;
  const flush = () => {
    if (para.length) html += `<p>${inline(para.join(' '), canOpen)}</p>`;
    para = [];
  };
  const closeList = () => {
    if (inList) html += '</ul>';
    inList = false;
  };
  for (const raw of lines) {
    const line = raw.replace(/\s+$/, '');
    let m;
    if (!line.trim()) {
      flush();
      closeList();
      continue;
    }
    if (/^ {0,3}(-{3,}|\*{3,}|_{3,})$/.test(line)) {
      flush();
      closeList();
      html += '<hr>';
      continue;
    }
    if ((m = line.match(/^ {0,3}(#{1,6})\s+(.*?)\s*#*$/))) {
      flush();
      closeList();
      html += `<h4${m[1].length <= 2 ? ' class="h-top"' : ''}>${inline(m[2], canOpen)}</h4>`;
      continue;
    }
    if ((m = line.match(/^\s*[-*+]\s+(.*)$/))) {
      flush();
      if (!inList) html += '<ul>';
      inList = true;
      html += `<li>${inline(m[1], canOpen)}</li>`;
      continue;
    }
    if (inList && /^\s{2,}\S/.test(line)) {
      html = html.replace(/<\/li>$/, ` ${inline(line.trim(), canOpen)}</li>`);
      continue;
    }
    closeList();
    para.push(line.trim());
  }
  flush();
  closeList();
  return html;
}

function escapeHtml(s) {
  return String(s ?? '').replace(/[&<>"']/g, (ch) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[ch]);
}

function inline(s, canOpen) {
  const codes = [];
  let t = escapeHtml(s).replace(/`([^`]+)`/g, (_, c) => {
    codes.push(c);
    return `\u0001${codes.length - 1}\u0001`;
  });
  t = t.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>').replace(/__(.+?)__/g, '<strong>$1</strong>');
  t = t.replace(/\[([^\]]+)\]\((https:\/\/[^\s)"]+)\)/g, (_, text, url) => (canOpen
    ? `<a href="#" data-url="${url}" title="${url}">${text}</a>`
    : `${text} <span class="us-url">(${url})</span>`));
  return t.replace(/\u0001(\d+)\u0001/g, (_, i) => `<code>${codes[Number(i)]}</code>`);
}
