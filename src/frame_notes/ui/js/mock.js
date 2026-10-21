// Browser mock of the shell (?mock=1): answers every call of docs/BRIDGE_API.md with the same shapes, keeps
// notes in memory, builds timecodes with js/frames.js and returns images as data URLs.
// URL parameters: video=<url relative to the page>, fps=<rate>|unknown (default 25), update=available.

import { timecodeBase, formatTimecode, formatFps, durationFrames } from './frames.js';

const KINDS = ['FIX', 'ADD', 'REMOVE'];
const VERSION = '1.0.0';
const LATENCY = 25;

export function createMock(dispatch) {
  const params = new URLSearchParams(location.search);
  const fpsParam = params.get('fps');
  const probedFps = fpsParam === 'unknown' ? null : Number(fpsParam) > 0 ? Number(fpsParam) : 25;
  let override = null;
  let video = null;           // {path, name, dir, url, width, height, duration}
  let notes = [];
  let order = 0;
  let lastFrame = 0;
  let fileExists = false;     // the mock "notes file" exists after the first note
  const settings = { auto_download_updates: true, notes_panel_open: true };
  const recent = [];
  const sample = params.get('video');
  if (sample) recent.push({ path: sample, name: nameOf(sample), last_frame: 0, exists: true });
  recent.push({ path: 'D:\\clips\\promo_v2.mp4', name: 'promo_v2.mp4', last_frame: 120, exists: false });

  const later = (fn, ms = 0) => setTimeout(fn, ms);
  const reply = (value) => new Promise((resolve) => later(() => resolve(value), LATENCY));
  const fail = (message) => new Promise((resolve, reject) => later(() => reject(new Error(message)), LATENCY));
  const emitState = () => later(() => dispatch('state', payload()));

  function fps() {
    return override || probedFps;
  }
  function tb() {
    return fps() ? timecodeBase(fps()) : null;
  }
  function tcOf(frame) {
    const b = tb();
    return b ? formatTimecode(frame, b) : `${(frame / 30).toFixed(3)}`;
  }

  function sorted() {
    return [...notes].sort((a, b) => a.frame - b.frame || a.order - b.order);
  }

  function notePayload(n) {
    return {
      id: n.id, frame: n.frame, timecode: tcOf(n.frame), kind: n.kind, text: n.text,
      region: n.region ? { ...n.region } : null,
      image: n.image, image_path: n.image ? `${video.dir}\\${stem(video.name)}_notes\\${n.image}` : null,
      image_url: n.imageUrl || null, order: n.order,
    };
  }

  function payload() {
    const counts = { FIX: 0, ADD: 0, REMOVE: 0 };
    for (const n of notes) counts[n.kind] += 1;
    const b = tb();
    let v = null;
    if (video) {
      const f = fps();
      v = {
        path: video.path, name: video.name, dir: video.dir, width: video.width, height: video.height,
        duration: video.duration, duration_frames: f ? durationFrames(video.duration, b.fps) : null,
        fps: f, fps_text: f ? formatFps(f) : null,
        fps_source: override ? 'override' : probedFps ? 'file' : 'unknown',
        notes_path: `${video.dir}\\${stem(video.name)}_notes.txt`,
        images_dir: `${video.dir}\\${stem(video.name)}_notes`,
        last_frame: lastFrame, url: video.url,
      };
    }
    return {
      version: VERSION,
      video: v,
      timecode: b ? { base: b.base, drop: b.drop, digits: b.digits, separator: b.separator } : null,
      notes: sorted().map(notePayload),
      counts,
      recent: recent.map((r) => ({ ...r })),
      settings: { ...settings },
    };
  }

  function probe(url) {
    return new Promise((resolve, reject) => {
      const el = document.createElement('video');
      el.preload = 'metadata';
      el.muted = true;
      el.onloadedmetadata = () => {
        resolve({ width: el.videoWidth, height: el.videoHeight, duration: el.duration });
        el.removeAttribute('src');
        el.load();
      };
      el.onerror = () => reject(new Error('File not found.'));
      el.src = url;
    });
  }

  function pickFile() {
    return new Promise((resolve) => {
      const input = document.createElement('input');
      input.type = 'file';
      input.accept = 'video/*,.mkv';
      input.addEventListener('change', () => {
        const f = input.files && input.files[0];
        resolve(f ? `${URL.createObjectURL(f)}#${encodeURIComponent(f.name)}` : null);
      });
      input.addEventListener('cancel', () => resolve(null));
      input.click();
    });
  }

  async function open(path) {
    const known = recent.find((r) => r.path === path);
    if (known && known.exists === false) throw new Error('File not found.');
    const hash = path.indexOf('#');
    const url = hash >= 0 ? path.slice(0, hash) : path;
    const name = hash >= 0 ? decodeURIComponent(path.slice(hash + 1)) : nameOf(path);
    const info = await probe(url);
    video = { path, name, dir: 'D:\\mock', url, ...info };
    notes = [];
    order = 0;
    fileExists = false;
    lastFrame = known ? known.last_frame : 0;
    override = null;
    const i = recent.findIndex((r) => r.path === path);
    if (i >= 0) recent.splice(i, 1);
    recent.unshift({ path, name, last_frame: lastFrame, exists: true });
    return payload();
  }

  function validRegion(r) {
    if (r == null) return true;
    if (typeof r !== 'object' || !['rect', 'point'].includes(r.shape)) return false;
    return ['x0', 'y0', 'x1', 'y1'].every((k) => typeof r[k] === 'number' && r[k] >= 0 && r[k] <= 1);
  }

  function imageName(n) {
    const base = `${tcOf(n.frame).replace(/[:;]/g, '-')}_${n.kind}`;
    const taken = new Set(notes.filter((m) => m !== n && m.image).map((m) => m.image));
    let name = `${base}.png`;
    for (let i = 2; taken.has(name); i++) name = `${base}_${i}.png`;
    return name;
  }

  function oneLine(text) {
    return String(text || '').replace(/[\t\r\n]+/g, ' ').trim();
  }

  function regionText(r) {
    const f = (v) => v.toFixed(3);
    return r.shape === 'point' ? `point ${f(r.x0)},${f(r.y0)}` : `rect ${f(r.x0)},${f(r.y0)} ${f(r.x1)},${f(r.y1)}`;
  }

  function notesText() {
    if (!video) return '';
    const b = tb();
    const lines = ['# Frame Notes 1', `# source: ${video.name}`, `# fps: ${fps() ? formatFps(fps()) : ''}`];
    if (b) lines.push(`# duration: ${formatTimecode(durationFrames(video.duration, b.fps), b)}`);
    for (const n of sorted()) {
      const cols = [tcOf(n.frame), n.kind, n.text];
      if (n.region) {
        cols.push(regionText(n.region));
        if (n.image) cols.push(n.image);
      }
      lines.push(cols.join('\t'));
    }
    return `${lines.join('\n')}\n`;
  }

  // Self-update simulation.
  const wantUpdate = params.get('update') === 'available';
  const RELEASE_NOTES = '## Changes\n\n- Timeline markers keep their labels when zoomed out.\n- The notes file keeps '
    + 'hand-written lines in their original order.\n\n## Fixes\n\n- Stepping at 59.94 fps lands on every frame.';
  let update = {
    stage: 'up_to_date', current: VERSION, latest: VERSION, notes: null, size: null, published_at: null,
    progress: 0, message: 'Frame Notes is up to date', checked_at: Date.now() / 1000, can_apply: false,
  };
  let downloading = false;
  const setUpdate = (patch) => {
    update = { ...update, ...patch };
    dispatch('update', { ...update });
  };
  function check() {
    if (update.stage === 'checking' || downloading) return;
    setUpdate({ stage: 'checking', message: 'Checking for updates' });
    later(() => {
      if (wantUpdate) {
        setUpdate({ stage: 'available', latest: '1.1.0', notes: RELEASE_NOTES, size: 12_640_000,
          published_at: Date.now() / 1000 - 86400, message: 'Frame Notes 1.1.0 is available',
          checked_at: Date.now() / 1000 });
        if (settings.auto_download_updates) download();
      } else {
        setUpdate({ stage: 'up_to_date', message: 'Frame Notes is up to date', checked_at: Date.now() / 1000 });
      }
    }, 700);
  }
  function download() {
    if (downloading || update.stage !== 'available') return;
    downloading = true;
    const t0 = performance.now();
    const tick = () => {
      const p = Math.min(1, (performance.now() - t0) / 3000);
      if (p < 1) {
        setUpdate({ stage: 'downloading', progress: p,
          message: `Downloading Frame Notes ${update.latest}… ${Math.round(p * 100)}%` });
        later(tick, 150);
      } else {
        downloading = false;
        setUpdate({ stage: 'ready', progress: 1, can_apply: true,
          message: `Frame Notes ${update.latest} is ready. Restart to update.` });
      }
    };
    tick();
  }

  return {
    async ready() {
      if (sample) {
        try {
          await open(sample);
        } catch (err) {
          later(() => dispatch('error', { message: `Could not open ${sample}.` }));
        }
      }
      if (wantUpdate) later(check, 1200);
      return reply(payload());
    },
    async open_video(path) {
      if (path == null) {
        const picked = await pickFile();
        if (!picked) return reply(null);
        path = picked;
      }
      try {
        return reply(await open(path));
      } catch (err) {
        return fail(err.message);
      }
    },
    get_state() {
      return reply(payload());
    },
    set_fps(value) {
      if (!video) return fail('No video is open.');
      if (value == null || Number(value) === probedFps) override = null;
      else if (Number(value) > 0) override = Number(value);
      else return fail('Invalid frame rate.');
      emitState();
      return reply(payload());
    },
    set_last_frame(frame) {
      lastFrame = Math.max(0, Math.floor(frame));
      const r = video && recent.find((x) => x.path === video.path);
      if (r) r.last_frame = lastFrame;
      return reply(null);
    },
    add_note(note) {
      if (!video) return fail('No video is open.');
      if (!fps()) return fail('Set the frame rate before adding notes.');
      if (!note || !KINDS.includes(note.kind) || !validRegion(note.region) || !(note.frame >= 0)) {
        return fail('Invalid note.');
      }
      const n = {
        id: Math.floor(Math.random() * 0xffffffff).toString(16).padStart(8, '0'),
        frame: Math.floor(note.frame), kind: note.kind, text: oneLine(note.text),
        region: note.region || null, image: null, imageUrl: null, order: ++order,
      };
      if (n.region && note.image_png) {
        n.image = imageName(n);
        n.imageUrl = `data:image/png;base64,${note.image_png}`;
      }
      notes.push(n);
      fileExists = true;
      emitState();
      return reply(notePayload(n));
    },
    update_note(patch) {
      const n = patch && notes.find((x) => x.id === patch.id);
      if (!n) return fail('Note not found.');
      if ('region' in patch && !validRegion(patch.region)) return fail('Invalid note.');
      if ('text' in patch && patch.text != null) n.text = oneLine(patch.text);
      if ('region' in patch) {
        n.region = patch.region || null;
        if (!n.region) {
          n.image = null;
          n.imageUrl = null;
        }
      }
      if (n.region && patch.image_png) {
        n.image = n.image || imageName(n);
        n.imageUrl = `data:image/png;base64,${patch.image_png}`;
      }
      emitState();
      return reply(notePayload(n));
    },
    remove_note(id) {
      const i = notes.findIndex((x) => x.id === id);
      if (i < 0) return fail('Note not found.');
      notes.splice(i, 1);
      emitState();
      return reply(null);
    },
    notes_text() {
      return reply(notesText());
    },
    async copy_notes_text() {
      try {
        await navigator.clipboard.writeText(notesText());
      } catch {
        console.info(notesText());
      }
      later(() => dispatch('toast', { message: 'Notes copied' }));
      return reply(null);
    },
    reveal(which) {
      if (!video) return fail('No video is open.');
      if (which === 'notes' && !fileExists) return fail('No notes file yet.');
      if (which === 'images' && !notes.some((n) => n.image)) return fail('No images yet.');
      later(() => dispatch('toast', { message: `Reveal ${which}: not available in the browser mock` }));
      return reply(null);
    },
    set_setting(key, value) {
      if (!['auto_download_updates', 'notes_panel_open'].includes(key)) return fail('Unknown setting.');
      settings[key] = !!value;
      emitState();
      if (key === 'auto_download_updates' && value && update.stage === 'available') download();
      return reply(payload());
    },
    log(level, message) {
      console.debug(`[${level}] ui: ${message}`);
      return reply(null);
    },
    update_status() {
      return reply({ ...update });
    },
    check_updates() {
      check();
      return reply({ ...update });
    },
    download_update() {
      download();
      return reply({ ...update });
    },
    apply_update() {
      return reply(update.stage === 'ready' && update.can_apply);
    },
  };
}

function nameOf(path) {
  const clean = path.split(/[?#]/)[0];
  const i = Math.max(clean.lastIndexOf('/'), clean.lastIndexOf('\\'));
  return decodeURIComponent(i >= 0 ? clean.slice(i + 1) : clean);
}

function stem(name) {
  const i = name.lastIndexOf('.');
  return i > 0 ? name.slice(0, i) : name;
}
