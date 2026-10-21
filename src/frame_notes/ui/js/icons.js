// Inline SVG icons of the transport row (16 px grid, currentColor).

const svg = (body) => `<svg viewBox="0 0 16 16" width="16" height="16" aria-hidden="true" fill="currentColor">${body}</svg>`;

export const ICONS = {
  first: svg('<rect x="2.5" y="3" width="1.8" height="10" rx=".6"/><path d="M13 3.6v8.8a.6.6 0 0 1-.93.5L5.6 8.5a.6.6 0 0 1 0-1l6.47-4.4a.6.6 0 0 1 .93.5z"/>'),
  prev: svg('<rect x="3" y="3" width="1.8" height="10" rx=".6"/><path d="M12.5 4.2v7.6a.5.5 0 0 1-.78.42L6.2 8.42a.5.5 0 0 1 0-.84l5.52-3.8a.5.5 0 0 1 .78.42z"/>'),
  play: svg('<path d="M4.5 2.9v10.2a.7.7 0 0 0 1.06.6l8.2-5.1a.7.7 0 0 0 0-1.2l-8.2-5.1a.7.7 0 0 0-1.06.6z"/>'),
  pause: svg('<rect x="3.5" y="2.8" width="3" height="10.4" rx=".8"/><rect x="9.5" y="2.8" width="3" height="10.4" rx=".8"/>'),
  next: svg('<rect x="11.2" y="3" width="1.8" height="10" rx=".6"/><path d="M3.5 4.2v7.6a.5.5 0 0 0 .78.42L9.8 8.42a.5.5 0 0 0 0-.84L4.28 3.78a.5.5 0 0 0-.78.42z"/>'),
  last: svg('<rect x="11.7" y="3" width="1.8" height="10" rx=".6"/><path d="M3 3.6v8.8a.6.6 0 0 0 .93.5L10.4 8.5a.6.6 0 0 0 0-1L3.93 3.1A.6.6 0 0 0 3 3.6z"/>'),
};
