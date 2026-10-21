// Notes panel on the right: one row per note (timecode, type, text, thumbnail), selection, inline edit.

import { app, el } from './app.js';

export class NotesPanel {
  constructor({ root, composer, onPick }) {
    this.root = root;
    this.list = root.querySelector('.np-list');
    this.count = root.querySelector('.np-count');
    this.empty = root.querySelector('.np-empty');
    this.composer = composer;
    this.onPick = onPick;      // (note) => select and seek
    this.rows = new Map();

    this.list.addEventListener('click', (e) => {
      const row = e.target.closest('.np-row');
      if (!row || e.target === composer.input) return;
      const n = app.note(row.dataset.id);
      if (n) onPick(n);
    });
    this.list.addEventListener('dblclick', (e) => {
      const row = e.target.closest('.np-row');
      if (!row || e.target === composer.input) return;
      composer.edit(row.dataset.id, 'row');
    });

    app.on('state', () => this.render());
    app.on('edit', () => this.render());
    app.on('select', () => this.markSelection(true));
  }

  render() {
    const notes = app.notes;
    const editId = app.edit && app.edit.mode === 'edit' && app.edit.host === 'row' ? app.edit.id : null;
    this.count.textContent = notes.length ? String(notes.length) : '';
    this.empty.hidden = notes.length > 0 || !app.video;
    this.rows.clear();
    const frag = document.createDocumentFragment();
    for (const n of notes) {
      const row = el('li', `np-row k-${n.kind}`);
      row.dataset.id = n.id;
      row.setAttribute('role', 'option');
      const head = el('div', 'np-head');
      head.append(el('span', 'np-tc mono', n.timecode || ''), el('span', `badge k-${n.kind}`, n.kind));
      if (n.region && !n.image_url) head.append(el('span', 'np-region muted', n.region.shape));
      row.append(head);
      if (n.id === editId) {
        row.classList.add('editing');
        const wrap = el('div', 'np-edit');
        wrap.append(this.composer.input);
        row.append(wrap);
      } else {
        const text = el('div', 'np-text', n.text || 'No text');
        if (!n.text) text.classList.add('muted');
        row.append(text);
      }
      if (n.image_url) {
        const img = el('img', 'np-thumb');
        img.loading = 'lazy';
        img.alt = '';
        img.src = n.image_url;
        row.append(img);
        row.classList.add('has-thumb');
      }
      this.rows.set(n.id, row);
      frag.append(row);
    }
    this.list.replaceChildren(frag);
    this.markSelection(false);
    if (editId) this.composer.focus();
  }

  markSelection(scroll) {
    for (const [id, row] of this.rows) {
      const sel = id === app.selectedId;
      row.classList.toggle('sel', sel);
      row.setAttribute('aria-selected', sel ? 'true' : 'false');
      if (sel && scroll) row.scrollIntoView({ block: 'nearest' });
    }
  }
}
