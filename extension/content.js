// IBDM Pro — the "تحميل هذا الفيديو" button that appears on videos when the mouse is over them (like IDM).
(() => {
  if (window.__ibdmPro) return;
  window.__ibdmPro = true;

  const MIN_W = 200, MIN_H = 120;
  let current = null;      // the video the button belongs to
  let hideTimer = 0;
  let host, button, label;
  let sending = false;

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
      </style>
      <div class="b" title="${t('IBDM Pro — اختر الجودة وحمّل')}">
        <img class="i" alt="" src="${api.runtime.getURL('icons/icon48.png')}">
        <span class="t">${t('تحميل هذا الفيديو')}</span><span class="s">IBDM Pro</span><span class="x" title="${t('إخفاء')}">✕</span>
      </div>`;
    button = root.querySelector('.b');
    applyTheme();
    label = root.querySelector('.t');
    root.querySelector('.x').addEventListener('click', e => {
      e.stopPropagation();
      if (current) current.__ibdmHidden = true;
      hide();
    });
    button.addEventListener('click', e => {
      e.stopPropagation();
      e.preventDefault();
      download();
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
    button.style.direction = IBDM_LANG === 'en' ? 'ltr' : 'rtl';
    button.title = t('IBDM Pro — اختر الجودة وحمّل');
    if (label && !sending) label.textContent = t('تحميل هذا الفيديو');
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
  setInterval(loadTheme, 60000);

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
    if (r.width < MIN_W || r.height < MIN_H || r.bottom < 0 || r.top > innerHeight) { hide(); return; }
    // top-right corner of the video, kept on screen
    const w = button.offsetWidth || 220;
    const left = Math.max(6, Math.min(innerWidth - w - 6, r.right - w - 10));
    const top = Math.max(6, r.top + 10);
    host.style.left = left + 'px';
    host.style.top = top + 'px';
  }

  function show(v) {
    if (!host) makeButton();
    const fsParent = document.fullscreenElement || document.documentElement;
    if (host.parentNode !== fsParent) fsParent.appendChild(host);
    current = v;
    label.textContent = t('تحميل هذا الفيديو');
    host.style.display = 'block';
    place();
    clearTimeout(hideTimer);
  }

  function hide() {
    if (host) host.style.display = 'none';
    current = null;
  }

  function scheduleHide() {
    clearTimeout(hideTimer);
    hideTimer = setTimeout(hide, 2500);
  }

  function download() {
    const v = current;
    if (!v) return;
    let src = v.currentSrc || v.src || '';
    if (!src) { const s = v.querySelector('source[src]'); if (s) src = s.src; }
    // blob:/MediaSource players (YouTube and most big sites): the page address is what can be downloaded.
    if (!/^https?:/i.test(src)) src = '';
    sending = true;
    label.textContent = t('جاري الإرسال…');
    api.runtime.sendMessage({ cmd: 'media', src, pageUrl: location.href, title: document.title }).then(res => {
      label.textContent = res && res.ok ? t('✓ تم — اختر الجودة بالبرنامج') : t('شغّل برنامج IBDM Pro أولاً');
      setTimeout(() => { sending = false; if (label) label.textContent = t('تحميل هذا الفيديو'); }, 3500);
    }).catch(() => { label.textContent = t('أعد تحميل الصفحة'); });
  }

  let lastMove = 0;
  document.addEventListener('mousemove', e => {
    const now = Date.now();
    if (now - lastMove < 120) return;
    lastMove = now;
    const v = videoAt(e.clientX, e.clientY);
    if (v) { if (v !== current) show(v); else clearTimeout(hideTimer); }
    else if (current && host && !host.matches(':hover')) scheduleHide();
  }, { passive: true });

  addEventListener('scroll', place, { passive: true, capture: true });
  addEventListener('resize', place, { passive: true });
  document.addEventListener('fullscreenchange', () => { if (current) show(current); });
  setInterval(() => { if (current && !current.isConnected) hide(); else place(); }, 800);
})();
