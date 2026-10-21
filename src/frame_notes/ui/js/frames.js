// Frame math and live timecode formatting. Mirrors core/timecode.py (docs/CORE.md, docs/ARCHITECTURE.md
// section 5). The timecode saved with a note always comes from the backend payload; this module drives the
// live counter, the ruler labels and the browser mock only.

export const NOMINAL_RATES = [23.976, 24, 25, 29.97, 30, 48, 50, 59.94, 60, 100, 120];

const EXACT = new Map([
  [23.976, 24000 / 1001],
  [29.97, 30000 / 1001],
  [59.94, 60000 / 1001],
]);

/** Snaps fps to the nearest nominal rate when within 0.5 %, else returns it unchanged. */
export function nominalRate(fps) {
  if (!(fps > 0) || !Number.isFinite(fps)) throw new RangeError('Invalid frame rate');
  let best = fps;
  let bestErr = Infinity;
  for (const r of NOMINAL_RATES) {
    const err = Math.abs(fps - r) / r;
    if (err < bestErr) {
      best = r;
      bestErr = err;
    }
  }
  return bestErr <= 0.005 ? best : fps;
}

/**
 * Timecode base of a rate: `fps` is the exact rate used for frame <-> seconds (24000/1001 for 23.976),
 * `base` the frames per timecode second, `drop` true for 29.97 and 59.94 only.
 */
export function timecodeBase(fps) {
  const nominal = nominalRate(fps);
  const base = Math.round(nominal);
  const drop = nominal === 29.97 || nominal === 59.94;
  return {
    fps: EXACT.get(nominal) ?? nominal,
    nominal,
    base,
    drop,
    digits: base >= 100 ? 3 : 2,
    separator: drop ? ';' : ':',
  };
}

/** Zero-based frame shown at `seconds`. */
export function frameAt(seconds, fps) {
  return Math.max(0, Math.floor(seconds * fps + 1e-4));
}

/** Seek target of a frame: its centre. */
export function frameCentre(frame, fps) {
  return (frame + 0.5) / fps;
}

/** Start time of a frame (timeline geometry). */
export function frameStart(frame, fps) {
  return frame / fps;
}

export function durationFrames(seconds, fps) {
  return Math.round(seconds * fps);
}

const pad = (n, w) => String(n).padStart(w, '0');

/** After Effects style timecode `h:mm:ss:ff` (drop-frame `h;mm;ss;ff` at 29.97 and 59.94). */
export function formatTimecode(frame, tb) {
  let n = Math.max(0, Math.floor(frame));
  if (tb.drop) {
    const d = tb.base === 60 ? 4 : 2;
    const fpm = tb.base * 60 - d;
    const fp10 = fpm * 10 + d;
    const q = Math.floor(n / fp10);
    const m = n % fp10;
    n = n + 9 * d * q + (m > d ? d * Math.floor((m - d) / fpm) : 0);
  }
  const ff = n % tb.base;
  const total = Math.floor(n / tb.base);
  const ss = total % 60;
  const mm = Math.floor(total / 60) % 60;
  const hh = Math.floor(total / 3600);
  const s = tb.separator;
  return `${hh}${s}${pad(mm, 2)}${s}${pad(ss, 2)}${s}${pad(ff, tb.digits)}`;
}

/** Clock display used while the frame rate is unknown: `h:mm:ss.mmm`. */
export function formatClock(seconds) {
  const ms = Math.max(0, Math.round((seconds || 0) * 1000));
  const total = Math.floor(ms / 1000);
  const hh = Math.floor(total / 3600);
  const mm = Math.floor(total / 60) % 60;
  const ss = total % 60;
  return `${hh}:${pad(mm, 2)}:${pad(ss, 2)}.${pad(ms % 1000, 3)}`;
}

/** "25", "29.97", "23.976": up to three decimals, trailing zeros cut. */
export function formatFps(fps) {
  return String(Number(Number(fps).toFixed(3)));
}
