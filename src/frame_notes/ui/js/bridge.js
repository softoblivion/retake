// Bridge to the shell (docs/BRIDGE_API.md). `api.<call>()` returns a promise; `on(event, handler)`
// subscribes to Python -> JS events, which the shell delivers through window.frameNotes.dispatch().
// With ?mock=1 the backend is the in-browser fake of js/mock.js.

const CALLS = [
  'ready', 'open_video', 'get_state', 'set_fps', 'set_last_frame', 'add_note', 'update_note', 'remove_note',
  'notes_text', 'copy_notes_text', 'reveal', 'set_setting', 'log',
  'update_status', 'check_updates', 'download_update', 'apply_update',
];

export const isMock = new URLSearchParams(location.search).get('mock') === '1';

const handlers = new Map();
let early = [];          // events that arrive before connect() finished; null once live
let backend = null;

window.frameNotes = window.frameNotes || {};
window.frameNotes.dispatch = (name, payload) => {
  if (early) early.push([name, payload]);
  else emit(name, payload);
};

export function on(name, fn) {
  if (!handlers.has(name)) handlers.set(name, new Set());
  handlers.get(name).add(fn);
  return () => handlers.get(name).delete(fn);
}

function emit(name, payload) {
  const set = handlers.get(name);
  if (!set) return;
  for (const fn of Array.from(set)) {
    try {
      fn(payload);
    } catch (err) {
      console.error(`Handler for "${name}" failed`, err);
    }
  }
}

function toError(err) {
  if (err instanceof Error) return err;
  if (err && typeof err === 'object') return new Error(err.message || err.error || JSON.stringify(err));
  return new Error(String(err));
}

async function call(name, args) {
  if (!backend) throw new Error('The app is still starting.');
  const fn = backend[name];
  if (typeof fn !== 'function') throw new Error(`${name} is not available.`);
  try {
    return await fn.apply(backend, args);
  } catch (err) {
    throw toError(err);
  }
}

export const api = {};
for (const name of CALLS) api[name] = (...args) => call(name, args);

function waitForPywebview(onSlow) {
  return new Promise((resolve) => {
    const ok = () => window.pywebview && window.pywebview.api;
    if (ok()) return resolve();
    const slow = setTimeout(onSlow, 8000);
    const done = () => {
      clearTimeout(slow);
      clearInterval(poll);
      resolve();
    };
    window.addEventListener('pywebviewready', () => ok() && done(), { once: true });
    // pywebview may fill window.pywebview.api after the event; poll as a fallback.
    const poll = setInterval(() => ok() && done(), 100);
  });
}

/** Connects to the backend, calls ready() and returns the first State. Queued events follow it. */
export async function connect({ onSlow } = {}) {
  if (isMock) {
    const mock = await import('./mock.js');
    backend = mock.createMock((name, payload) => window.frameNotes.dispatch(name, payload));
  } else {
    await waitForPywebview(onSlow || (() => {}));
    backend = window.pywebview.api;
  }
  const state = await call('ready', []);
  return {
    state,
    goLive() {
      const queued = early;
      early = null;
      for (const [name, payload] of queued) emit(name, payload);
    },
  };
}
