// IBRA Download Manager — runs inside the page itself (not the extension's own copy of it), so it can see sounds
// that are played by code without being put on the page (music sites like Suno play songs this way: new Audio()).
// It only reports "a sound is playing: its address and its name" to the page button (content.js). It changes nothing.
(() => {
  if (window.__ibdmMediaHook) return;
  window.__ibdmMediaHook = true;
  const watched = new WeakSet();
  let seq = 0;

  function songName() {
    try {
      const m = navigator.mediaSession && navigator.mediaSession.metadata;
      if (m && m.title) return { title: String(m.title), artist: m.artist ? String(m.artist) : '' };
    } catch (e) { }
    return { title: '', artist: '' };
  }

  function report(el, state) {
    try {
      // Videos big enough to have the button on them are handled by content.js; this is for sounds.
      if (el.tagName === 'VIDEO' && el.isConnected) {
        const r = el.getBoundingClientRect();
        if (r.width >= 200 && r.height >= 120) return;
      }
      if (!el.__ibdmId) el.__ibdmId = 'm' + (++seq);
      const name = songName();
      const detail = JSON.stringify({
        id: el.__ibdmId, state,
        src: el.currentSrc || el.src || '',
        duration: isFinite(el.duration) ? el.duration : (el.duration === Infinity ? -1 : 0),
        paused: el.paused, title: name.title, artist: name.artist,
      });
      document.dispatchEvent(new CustomEvent('__ibdm_media', { detail }));
    } catch (e) { }
  }

  function watch(el) {
    if (!el || watched.has(el)) return;
    watched.add(el);
    for (const ev of ['playing', 'pause', 'ended', 'emptied', 'durationchange'])
      el.addEventListener(ev, () => report(el, ev === 'durationchange' ? (el.paused ? 'pause' : 'playing') : ev));
  }

  // Sounds started by code (also ones never added to the page)…
  try {
    const play = HTMLMediaElement.prototype.play;
    HTMLMediaElement.prototype.play = function () {
      try { watch(this); } catch (e) { }
      return play.apply(this, arguments);
    };
  } catch (e) { }
  // …and players on the page started with their own controls or autoplay.
  document.addEventListener('play', e => { if (e.target instanceof HTMLMediaElement) { watch(e.target); } }, true);
  document.addEventListener('playing', e => { if (e.target instanceof HTMLMediaElement) { watch(e.target); report(e.target, 'playing'); } }, true);

  // The site may set the song's name a moment after it starts: tell content.js again.
  setInterval(() => {
    try {
      const m = navigator.mediaSession && navigator.mediaSession.metadata;
      const key = m ? (m.title || '') + '|' + (m.artist || '') : '';
      if (key !== window.__ibdmLastMeta) {
        window.__ibdmLastMeta = key;
        document.dispatchEvent(new CustomEvent('__ibdm_meta', { detail: JSON.stringify(songName()) }));
      }
    } catch (e) { }
  }, 1500);
})();
