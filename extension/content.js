// IBDM Pro — the "تحميل هذا الفيديو" button that appears on videos when the mouse is over them (like IDM).
(() => {
  // After the extension updates itself, the copy already running in an open page can no longer reach the extension.
  // The new copy is injected into open pages: it removes the dead one and takes over (no need to reload the page).
  const alive = () => { try { return !!(api.runtime && api.runtime.id); } catch (e) { return false; } };
  if (window.__ibdmPro && typeof window.__ibdmPro.alive === 'function' && window.__ibdmPro.alive()) return;
  try { if (window.__ibdmPro && window.__ibdmPro.destroy) window.__ibdmPro.destroy(); } catch (e) { }
  const stop = new AbortController();
  const timers = [];
  function destroy() {
    stop.abort();
    timers.forEach(clearInterval);
    try { if (host) host.remove(); } catch (e) { }
    if (window.__ibdmPro && window.__ibdmPro.destroy === destroy) window.__ibdmPro = null;
  }
  window.__ibdmPro = { alive, destroy };
  const on = { signal: stop.signal };

  const MIN_W = 200, MIN_H = 120;
  let current = null;      // the video the button belongs to
  let hideTimer = 0;
  let host, button, label;
  let sending = false;
  // Where the user put the button on the video (fractions of the free space, 1,0 = top-right) and its size.
  let pos = { fx: 1, fy: 0 }, scale = 1, dragging = false;
  // A song playing on a music site (Suno, SoundCloud…) has no video to sit on: its button sits over the page,
  // just above the player bar at the bottom (centred by default; it can be dragged too).
  let apos = { fx: 0.5, fy: 1 };
  const AUDIO = {
    isConnected: true,
    getBoundingClientRect() { const h = Math.max(140, innerHeight - 96); return { left: 0, top: 0, width: innerWidth, height: h, right: innerWidth, bottom: h }; },
  };
  const P = () => current === AUDIO ? apos : pos;
  const setP = v => { if (current === AUDIO) apos = v; else pos = v; };
  try {
    api.storage.local.get({ btnPos: null, btnScale: 1, audioPos: null }).then(v => {
      if (v.btnPos && isFinite(v.btnPos.fx) && isFinite(v.btnPos.fy)) pos = v.btnPos;
      if (v.audioPos && isFinite(v.audioPos.fx) && isFinite(v.audioPos.fy)) apos = v.audioPos;
      scale = Math.min(1.8, Math.max(0.6, +v.btnScale || 1));
      applyScale(); place();
    });
  } catch (e) { }
  function savePlace() { try { api.storage.local.set({ btnPos: pos, btnScale: scale, audioPos: apos }); } catch (e) { } }
  function applyScale() { if (button) button.style.zoom = scale; }

  function makeButton() {
    host = document.createElement('div');
    host.style.cssText = 'position:fixed;top:0;left:0;z-index:2147483647;display:none;';
    const root = host.attachShadow({ mode: 'closed' });
    root.innerHTML = `
      <style>
        /* The three IBDM Pro styles; the app tells which one is in use. */
        .b{--bg:linear-gradient(180deg,#FFFFFF 0%,#EEF0F5 55%,#E2E5EC 100%);--bgh:linear-gradient(180deg,#FFFFFF,#E9ECF2);
           --fg:#16161A;--ac:#D7141F;--bd:rgba(215,20,31,.85);--mu:#7A7F8A;--sh:0 6px 22px rgba(160,10,18,.28),inset 0 1px 0 #fff}
        .b.dark{--bg:linear-gradient(180deg,#1E1E23,#0F0F11);--bgh:linear-gradient(180deg,#2A2A31,#16161A);
           --fg:#F2F2F4;--ac:#D4AF37;--bd:#D4AF37;--mu:#9C9CA6;--sh:0 4px 18px rgba(0,0,0,.45)}
        .b.gdark{--bg:linear-gradient(180deg,#2A2A31 0%,#16161A 60%,#0B0B0E 100%);--bgh:linear-gradient(180deg,#34343C,#1C1C22);
           --fg:#F2F2F4;--ac:#F0434C;--bd:rgba(215,20,31,.9);--mu:#A0A0AA;--sh:0 6px 22px rgba(160,10,18,.45),inset 0 1px 0 rgba(255,255,255,.12)}
        .b.light{--bg:linear-gradient(180deg,#FFFFFF,#F3F3F5);--bgh:linear-gradient(180deg,#FFFFFF,#ECECF0);
           --fg:#1B1B1F;--ac:#A87F0A;--bd:#C49A1C;--mu:#6B6B75;--sh:0 4px 16px rgba(0,0,0,.18)}
        .b{display:flex;align-items:center;gap:8px;direction:rtl;font:600 13px/1 "Segoe UI",Tahoma,sans-serif;
           background:var(--bg);color:var(--fg);border:1.5px solid var(--bd);border-radius:10px;
           padding:6px 11px 6px 8px;cursor:pointer;box-shadow:var(--sh);user-select:none;transition:transform .12s;
           backdrop-filter:blur(8px)}
        .b:hover{transform:translateY(-1px);background:var(--bgh)}
        .i{width:22px;height:22px;flex:none;border-radius:5px;display:block}
        .s{color:var(--ac);font-weight:700}
        .x{margin-inline-start:4px;color:var(--mu);font-weight:400;padding:0 3px}
        .x:hover{color:var(--ac)}
        .b{position:relative}
        .g{color:var(--mu);cursor:grab;padding:0 2px;font-size:15px;letter-spacing:-2px;touch-action:none}
        .g:hover{color:var(--ac)} .g:active{cursor:grabbing}
        .r{position:absolute;right:-1px;bottom:-1px;width:14px;height:14px;cursor:nwse-resize;touch-action:none;
           background:linear-gradient(135deg,transparent 58%,var(--bd) 58%,var(--bd) 70%,transparent 70%);opacity:.8}
        .r:hover{opacity:1}
      </style>
      <div class="b" title="${t('IBDM Pro — اختر الجودة وحمّل')}">
        <span class="g" title="${t('اسحب لتغيير المكان — نقرتين للرجوع للمكان الأصلي')}">⠿</span>
        <img class="i" alt="" src="${api.runtime.getURL('icons/icon48.png')}">
        <span class="t">${t('تحميل هذا الفيديو')}</span><span class="s">IBRA Download Manager</span><span class="x" title="${t('إخفاء')}">✕</span>
        <span class="r" title="${t('اسحب لتكبير أو تصغير الزر')}"></span>
      </div>`;
    button = root.querySelector('.b');
    applyTheme();
    label = root.querySelector('.t');
    root.querySelector('.x').addEventListener('click', e => {
      e.stopPropagation();
      if (current === AUDIO) { if (audio) closedSounds.add(audio.id + '|' + audio.src); current = null; host.style.display = 'none'; return; }
      if (current) current.__ibdmHidden = true;
      hide();
    });
    button.addEventListener('click', e => {
      e.stopPropagation();
      e.preventDefault();
      if (e.target.closest('.g,.r')) return;
      download();
    });
    applyScale();
    // Move: drag the ⠿ grip anywhere over the video.
    const grip = root.querySelector('.g');
    grip.addEventListener('pointerdown', e => {
      e.preventDefault(); e.stopPropagation();
      if (!current) return;
      grip.setPointerCapture(e.pointerId);
      dragging = true;
      const hr = host.getBoundingClientRect();
      const ox = e.clientX - hr.left, oy = e.clientY - hr.top;
      const move = ev => {
        if (!current) return;
        const r = current.getBoundingClientRect(), b = button.getBoundingClientRect();
        const freeX = Math.max(1, r.width - b.width - 20), freeY = Math.max(1, r.height - b.height - 20);
        setP({ fx: Math.min(1, Math.max(0, (ev.clientX - ox - r.left - 10) / freeX)), fy: Math.min(1, Math.max(0, (ev.clientY - oy - r.top - 10) / freeY)) });
        place();
      };
      const up = () => { dragging = false; grip.removeEventListener('pointermove', move); grip.removeEventListener('pointerup', up); grip.removeEventListener('pointercancel', up); savePlace(); };
      grip.addEventListener('pointermove', move);
      grip.addEventListener('pointerup', up);
      grip.addEventListener('pointercancel', up);
    });
    grip.addEventListener('dblclick', e => { e.stopPropagation(); e.preventDefault(); setP(current === AUDIO ? { fx: 0.5, fy: 1 } : { fx: 1, fy: 0 }); scale = 1; applyScale(); place(); savePlace(); });
    // Resize: drag the corner.
    const grow = root.querySelector('.r');
    grow.addEventListener('pointerdown', e => {
      e.preventDefault(); e.stopPropagation();
      grow.setPointerCapture(e.pointerId);
      dragging = true;
      const sx = e.clientX, sy = e.clientY, s0 = scale, w0 = button.getBoundingClientRect().width || 200;
      const move = ev => {
        const d = ((ev.clientX - sx) + (ev.clientY - sy) * 2) / 2;
        scale = Math.min(1.8, Math.max(0.6, s0 * (w0 + d) / w0));
        applyScale(); place();
      };
      const up = () => { dragging = false; grow.removeEventListener('pointermove', move); grow.removeEventListener('pointerup', up); grow.removeEventListener('pointercancel', up); savePlace(); };
      grow.addEventListener('pointermove', move);
      grow.addEventListener('pointerup', up);
      grow.addEventListener('pointercancel', up);
    });
    host.addEventListener('mouseenter', () => clearTimeout(hideTimer));
    host.addEventListener('mouseleave', () => scheduleHide());
    (document.fullscreenElement || document.documentElement).appendChild(host);
  }

  // Follow the app's style (red glass / dark / light); remembered so the button is right before the app answers.
  let theme = 'glass';
  function applyTheme() {
    if (!button) return;
    button.classList.toggle('dark', theme === 'dark');
    button.classList.toggle('light', theme === 'light');
    button.classList.toggle('gdark', theme === 'glass-dark');
    button.style.direction = IBDM_LANG === 'en' ? 'ltr' : 'rtl';
    button.title = t('IBDM Pro — اختر الجودة وحمّل');
    if (label && !sending) label.textContent = labelText();
  }
  function loadTheme() {
    try {
      api.storage.local.get({ theme: 'glass', lang: 'ar' }).then(v => { theme = v.theme || 'glass'; IBDM_LANG = v.lang; applyTheme(); });
      api.runtime.sendMessage({ cmd: 'ping' }).catch(() => null).then(res => {
        if (!res || !res.app || !res.app.theme) return;
        theme = res.app.theme;
        if (res.app.lang) IBDM_LANG = res.app.lang;
        api.storage.local.set({ theme, lang: IBDM_LANG });
        applyTheme();
      });
    } catch (e) { }
  }
  loadTheme();
  timers.push(setInterval(() => { if (!alive()) destroy(); else loadTheme(); }, 60000));

  function videoAt(x, y) {
    for (const v of document.querySelectorAll('video')) {
      if (v.__ibdmHidden) continue;
      const r = v.getBoundingClientRect();
      if (r.width < MIN_W || r.height < MIN_H) continue;
      if (x >= r.left && x <= r.right && y >= r.top && y <= r.bottom) return v;
    }
    return null;
  }

  function place() {
    if (!current || !host) return;
    const r = current.getBoundingClientRect();
    if (current !== AUDIO && (r.width < MIN_W || r.height < MIN_H || r.bottom < 0 || r.top > innerHeight)) { hide(); return; }
    const pos = P();
    // where the user put it on the video (top-right by default), kept on screen
    const b = button.getBoundingClientRect();
    const w = b.width || 220, h = b.height || 36;
    const left = Math.max(6, Math.min(innerWidth - w - 6, r.left + 10 + pos.fx * Math.max(0, r.width - w - 20)));
    const top = Math.max(6, Math.min(innerHeight - h - 6, r.top + 10 + pos.fy * Math.max(0, r.height - h - 20)));
    host.style.left = left + 'px';
    host.style.top = top + 'px';
  }

  function show(v) {
    if (!host) makeButton();
    const fsParent = document.fullscreenElement || document.documentElement;
    if (host.parentNode !== fsParent) fsParent.appendChild(host);
    current = v;
    if (!sending) label.textContent = labelText();
    button.title = v === AUDIO ? (soundName(audio) || t('IBDM Pro — اختر الجودة وحمّل')) : t('IBDM Pro — اختر الجودة وحمّل');
    host.style.display = 'block';
    place();
    clearTimeout(hideTimer);
  }

  function hide() {
    current = null;
    // Back to the song's button when the mouse leaves a video and a song is still on.
    if (soundShown()) { show(AUDIO); return; }
    if (host) host.style.display = 'none';
  }

  // ---------- songs and sounds (reported by media-hook.js, which runs inside the page) ----------

  let audio = null;                 // {id, src, title, duration, playing}
  const closedSounds = new Set();   // songs whose button the user closed (✕)
  let metaTitle = '';
  const soundShown = () => !!audio && !closedSounds.has(audio.id + '|' + audio.src) && /^(https?|blob):/i.test(audio.src)
    && (audio.duration >= 15 || audio.duration === -1) && !document.fullscreenElement;

  document.addEventListener('__ibdm_media', e => {
    let m; try { m = JSON.parse(e.detail); } catch (x) { return; }
    if (m.state === 'emptied') { if (audio && audio.id === m.id) { audio = null; if (current === AUDIO) hide(); } return; }
    if (m.state !== 'playing' && (!audio || audio.id !== m.id)) return; // only the song that is (or was last) playing
    const changed = !audio || audio.src !== m.src;
    audio = { id: m.id, src: m.src, title: m.title || (changed ? '' : audio.title), duration: m.duration, playing: m.state === 'playing' };
    if (m.title) metaTitle = m.title;
    else if (changed) metaTitle = '';
    if (current === AUDIO) { if (soundShown()) show(AUDIO); else hide(); }
    else if (!current && soundShown()) show(AUDIO);
  }, on);
  document.addEventListener('__ibdm_meta', e => {
    try { const m = JSON.parse(e.detail); metaTitle = m.title || ''; if (audio && m.title) audio.title = m.title; } catch (x) { }
    if (current === AUDIO) show(AUDIO);
  }, on);

  // The name written on the song: the name the site gives the player (shown on the lock screen / media keys),
  // else the link to the song on the page (on Suno the song's address has the same id as its sound file),
  // else the page title.
  function soundName(a) {
    if (!a) return '';
    if (a.title) return a.title.trim();
    const id = soundId(a.src);
    if (id) {
      try {
        for (const link of document.querySelectorAll('a[href*="' + CSS.escape(id) + '"]')) {
          const text = (link.innerText || link.textContent || '').trim().split('\n')[0].trim();
          if (text.length > 0 && text.length <= 150) return text;
        }
      } catch (e) { }
    }
    let title = (document.title || '').trim();
    const site = location.hostname.replace(/^www\./, '').split('.')[0];
    if (site.length >= 3) title = title.replace(new RegExp('\\s*[|\\-–—•·]\\s*[^|\\-–—•·]*' + site.replace(/[^\w]/g, '') + '[^|\\-–—•·]*$', 'i'), '').trim();
    return title;
  }
  function soundId(src) {
    try {
      const u = new URL(src);
      const q = u.searchParams.get('item_id') || u.searchParams.get('id');
      if (q && q.length >= 8) return q;
      const last = u.pathname.split('/').filter(Boolean).pop() || '';
      const id = last.replace(/\.[a-z0-9]{2,5}$/i, '');
      return id.length >= 8 ? id : '';
    } catch (e) { return ''; }
  }
  // Suno: every song is a plain MP3 at cdn1.suno.ai/<song id>.mp3. The id comes from the sound's address, else from
  // the link to the song with the same name (the player bar), else from the song page the user is on.
  function sunoFile(a, name) {
    if (!/(^|\.)suno\.(com|ai)$/i.test(location.hostname)) return '';
    const uuid = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i;
    let m = uuid.exec(a.src || '');
    if (!m && name) {
      for (const link of document.querySelectorAll('a[href*="/song/"]')) {
        const text = cleanName((link.innerText || link.textContent || '').trim().split('\n')[0]);
        if (text && text === name) { m = uuid.exec(link.href); if (m) break; }
      }
    }
    if (!m && /\/song\//.test(location.pathname)) m = uuid.exec(location.pathname);
    return m ? 'https://cdn1.suno.ai/' + m[0].toLowerCase() + '.mp3' : '';
  }
  function soundExt(src) {
    try { const m = /\.(mp3|m4a|aac|ogg|oga|opus|wav|flac|weba|webm|mp4)$/i.exec(new URL(src).pathname); return m ? '.' + m[1].toLowerCase() : ''; }
    catch (e) { return ''; }
  }
  const cleanName = n => n.replace(/[\\/:*?"<>|\u0000-\u001f]+/g, ' ').replace(/\s+/g, ' ').trim().slice(0, 150).replace(/[. ]+$/, '');

  function labelText() {
    if (current !== AUDIO) return t('تحميل هذا الفيديو');
    const n = soundName(audio);
    if (!n) return t('تحميل هذه الأغنية');
    return t('تحميل') + ': \u2068' + (n.length > 38 ? n.slice(0, 36) + '…' : n) + '\u2069'; // the name keeps its own direction
  }

  function scheduleHide() {
    clearTimeout(hideTimer);
    if (dragging) return;
    hideTimer = setTimeout(hide, 2500);
  }

  // In a feed (Instagram home, Facebook, X, TikTok…) the page address is the feed, not the video: find the link of
  // the post the video sits in by looking for a post link in the boxes around the video, from the closest outwards.
  const POST_LINK = [
    /instagram\.com\/(?:[\w.]+\/)?(?:p|reel|reels|tv)\/[\w-]+/i,
    /facebook\.com\/(?:.*\/videos\/\d+|reel\/\d+|watch\/?\?v=\d+|share\/[rv]\/\w+)/i,
    /(?:twitter|x)\.com\/\w+\/status\/\d+/i,
    /tiktok\.com\/@[\w.-]+\/video\/\d+/i,
    /youtube\.com\/(?:watch\?v=|shorts\/)[\w-]+/i,
    /(?:vimeo\.com\/\d+|dailymotion\.com\/video\/\w+|reddit\.com\/r\/\w+\/comments\/\w+|pinterest\.[\w.]+\/pin\/\d+|threads\.(?:net|com)\/@[\w.]+\/post\/[\w-]+)/i,
  ];
  const isPost = u => POST_LINK.some(r => r.test(u));
  function postUrlFor(v) {
    if (isPost(location.href)) return location.href; // already on the post's own page
    let el = v;
    for (let depth = 0; el && el !== document.body && depth < 25; depth++, el = el.parentElement) {
      const links = el.querySelectorAll ? el.querySelectorAll('a[href]') : [];
      for (const a of links) {
        const href = a.href || '';
        if (!isPost(href)) continue;
        // Instagram/Facebook comment links (…/c/123) and "liked by" lists are not the post.
        return href.replace(/\/(?:c|liked_by|comments?)\/.*$/i, '/').split('#')[0];
      }
    }
    return '';
  }

  function download() {
    const v = current;
    if (!v) return;
    if (v === AUDIO) { downloadSound(); return; }
    let src = v.currentSrc || v.src || '';
    if (!src) { const s = v.querySelector('source[src]'); if (s) src = s.src; }
    // blob:/MediaSource players (YouTube and most big sites): the page address is what can be downloaded.
    if (!/^https?:/i.test(src)) src = '';
    const post = postUrlFor(v);
    // A direct file link only counts on the post's own page; in a feed send the post (yt-dlp finds every quality).
    if (post && post !== location.href) src = '';
    sending = true;
    label.textContent = t('جاري الإرسال…');
    const done = text => { label.textContent = text; setTimeout(() => { sending = false; if (label) label.textContent = t('تحميل هذا الفيديو'); }, 3500); };
    // Never stay on "sending…": no answer in 15 seconds means something went wrong.
    const guard = setTimeout(() => done(t('لم يصل رد — حدّث الصفحة وحاول مرة ثانية')), 15000);
    if (!alive()) { clearTimeout(guard); done(t('أعد تحميل الصفحة')); return; }
    try { api.runtime.sendMessage({ cmd: 'media', src, pageUrl: post || location.href, post: !!post, title: document.title }).then(res => {
      clearTimeout(guard);
      done(res && res.ok ? t('✓ تم — اختر الجودة بالبرنامج') : t('شغّل برنامج IBDM Pro أولاً'));
    }).catch(() => { clearTimeout(guard); done(t('أعد تحميل الصفحة')); });
    } catch (e) { clearTimeout(guard); done(t('أعد تحميل الصفحة')); }
  }

  // The song's own file when the player has a normal address (named after the song); otherwise (a stream) the page.
  function downloadSound() {
    const a = audio;
    if (!a) return;
    const name = cleanName(soundName(a) || '');
    const suno = sunoFile(a, name);
    const src = suno || (/^https?:/i.test(a.src) ? a.src : '');
    sending = true;
    label.textContent = t('جاري الإرسال…');
    const done = text => { label.textContent = text; setTimeout(() => { sending = false; if (label) label.textContent = labelText(); }, 3500); };
    const guard = setTimeout(() => done(t('لم يصل رد — حدّث الصفحة وحاول مرة ثانية')), 15000);
    if (!alive()) { clearTimeout(guard); done(t('أعد تحميل الصفحة')); return; }
    try { api.runtime.sendMessage({ cmd: 'media', kind: src ? 'audio' : 'media', src, pageUrl: location.href, title: name || document.title,
      fileName: name && src ? name + soundExt(src) : '' }).then(res => {
      clearTimeout(guard);
      done(res && res.ok ? t('✓ تم') : t('شغّل برنامج IBDM Pro أولاً'));
    }).catch(() => { clearTimeout(guard); done(t('أعد تحميل الصفحة')); });
    } catch (e) { clearTimeout(guard); done(t('أعد تحميل الصفحة')); }
  }

  let lastMove = 0;
  document.addEventListener('mousemove', e => {
    if (!alive()) { destroy(); return; }
    const now = Date.now();
    if (now - lastMove < 120) return;
    lastMove = now;
    const v = videoAt(e.clientX, e.clientY);
    if (dragging) return;
    if (v) { if (v !== current) show(v); else clearTimeout(hideTimer); }
    else if (current && current !== AUDIO && host && !host.matches(':hover')) scheduleHide();
  }, { passive: true, signal: stop.signal });

  addEventListener('scroll', place, { passive: true, capture: true, signal: stop.signal });
  addEventListener('resize', place, { passive: true, signal: stop.signal });
  document.addEventListener('fullscreenchange', () => { if (current === AUDIO && !soundShown()) hide(); else if (current) show(current); else if (soundShown()) show(AUDIO); }, on);
  timers.push(setInterval(() => { if (current && !current.isConnected) hide(); else place(); }, 800));
})();
