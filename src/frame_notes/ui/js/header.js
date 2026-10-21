// Header bar: open, file name and size, frame rate control, note counts, Copy split menu, Reveal, Recent, Keys,
// panel toggle.

import { app, el, KINDS } from './app.js';
import { api } from './bridge.js';
import { NOMINAL_RATES, formatFps } from './frames.js';
import { renderRecent } from './recent.js';
import { guard, popover, toast } from './ui.js';

export function initHeader({ actions, recentMenu }) {
  const $ = (id) => document.getElementById(id);
  const fpsBox = $('fps-box');
  const select = $('fps-select');
  const hint = $('fps-hint');
  const reset = $('fps-reset');
  const use = $('fps-use');
  let measured = null;

  $('btn-open').addEventListener('click', () => actions.open());
  $('btn-copy').addEventListener('click', () => actions.copy());
  const copyMenu = popover($('btn-copy-more'), $('copy-menu'));
  $('copy-menu').addEventListener('click', (e) => {
    const item = e.target.closest('[data-copy]');
    if (!item) return;
    copyMenu.close();
    if (item.dataset.copy === 'readable') actions.copyReadable();
    else actions.copy();
  });

  const recentList = $('recent-menu-list');
  const renderRecentMenu = () => {
    const recent = (app.state && app.state.recent) || [];
    $('recent-menu-empty').hidden = recent.length > 0;
    renderRecent(recentList, recent);
  };
  const recentPop = popover($('btn-recent'), $('recent-menu'), { onOpen: renderRecentMenu });
  recentMenu.attach(recentList, { beforeOpen: () => recentPop.close() });
  $('btn-keys').addEventListener('click', () => actions.toggleHelp());
  $('btn-panel').addEventListener('click', () => actions.togglePanel());

  const reveal = popover($('btn-reveal'), $('reveal-menu'));
  $('reveal-menu').addEventListener('click', (e) => {
    const item = e.target.closest('[data-reveal]');
    if (!item) return;
    reveal.close();
    guard(api.reveal(item.dataset.reveal));
  });

  select.addEventListener('change', async () => {
    const value = Number(select.value);
    select.blur();
    if (!(value > 0)) return;
    const st = await guard(api.set_fps(value));
    if (st) actions.applyState(st);
  });
  reset.addEventListener('click', async () => {
    const st = await guard(api.set_fps(null));
    if (st) actions.applyState(st);
  });
  use.addEventListener('click', async () => {
    if (!measured) return;
    const st = await guard(api.set_fps(measured));
    if (st) actions.applyState(st);
  });

  app.on('measured', (rate) => {
    if (app.fps) return;
    measured = rate;
    use.textContent = `Use ${formatFps(rate)}`;
    use.title = `Measured during playback: ${formatFps(rate)} fps`;
    use.hidden = false;
  });

  app.on('need-fps', () => {
    toast('Set the frame rate to add notes');
    fpsBox.classList.remove('flash');
    void fpsBox.offsetWidth;
    fpsBox.classList.add('flash');
    select.focus();
  });

  function renderFps(v) {
    const unknown = !v.fps || v.fps_source === 'unknown';
    const current = unknown ? '' : v.fps_text || formatFps(v.fps);
    const rates = NOMINAL_RATES.map(formatFps);
    if (current && !rates.includes(current)) rates.push(current);
    rates.sort((a, b) => Number(a) - Number(b));
    select.replaceChildren();
    if (unknown) {
      const ph = el('option', null, 'Set the frame rate');
      ph.value = '';
      ph.disabled = true;
      ph.selected = true;
      select.append(ph);
    }
    for (const r of rates) {
      const o = el('option', null, r);
      o.value = r;
      if (r === current) o.selected = true;
      select.append(o);
    }
    fpsBox.classList.toggle('attention', unknown);
    hint.textContent = unknown ? '' : v.fps_source === 'override' ? 'override' : 'from file';
    reset.hidden = v.fps_source !== 'override';
    if (!unknown) {
      measured = null;
      use.hidden = true;
    }
  }

  function render() {
    const v = app.video;
    const st = app.state;
    if (!v) {
      $('file-name').textContent = '';
      $('file-size').textContent = '';
      return;
    }
    $('file-name').textContent = v.name;
    $('file-name').title = v.path || v.name;
    $('file-size').textContent = v.width && v.height ? `${v.width}x${v.height}` : '';
    renderFps(v);
    const counts = (st && st.counts) || {};
    const box = $('counts');
    box.replaceChildren();
    for (const k of KINDS) {
      const c = el('span', `count k-${k}`);
      c.title = `${counts[k] || 0} ${k}`;
      c.append(el('i'), el('span', 'count-kind', k), el('b', null, String(counts[k] || 0)));
      box.append(c);
    }
    $('btn-panel').setAttribute('aria-pressed', app.panelOpen ? 'true' : 'false');
    if (recentPop.isOpen) renderRecentMenu();
  }

  app.on('state', render);
  app.on('panel', render);
  return { render };
}
