// IBDM Pro — the "تحميل هذا الفيديو" button that appears on videos when the mouse is over them (like IDM).
(() => {
  if (window.__ibdmPro) return;
  window.__ibdmPro = true;

  const MIN_W = 200, MIN_H = 120;
  let current = null;      // the video the button belongs to
  let hideTimer = 0;
  let host, button, label;

  function makeButton() {
    host = document.createElement('div');
    host.style.cssText = 'position:fixed;top:0;left:0;z-index:2147483647;display:none;';
    const root = host.attachShadow({ mode: 'closed' });
    root.innerHTML = `
      <style>
        .b{display:flex;align-items:center;gap:7px;direction:rtl;font:600 13px/1 "Segoe UI",Tahoma,sans-serif;
           background:linear-gradient(180deg,#1E1E23,#0F0F11);color:#F2F2F4;border:1.5px solid #D4AF37;border-radius:9px;
           padding:7px 11px 7px 9px;cursor:pointer;box-shadow:0 4px 18px rgba(0,0,0,.45);user-select:none;transition:transform .12s}
        .b:hover{transform:translateY(-1px);background:linear-gradient(180deg,#2A2A31,#16161A)}
        .i{width:18px;height:18px;flex:none}
        .s{color:#D4AF37}
        .x{margin-inline-start:4px;color:#9C9CA6;font-weight:400;padding:0 3px}
        .x:hover{color:#fff}
      </style>
      <div class="b" title="IBDM Pro — اختر الجودة وحمّل">
        <svg class="i" viewBox="0 0 24 24"><rect x="1" y="1" width="22" height="22" rx="6" fill="#111" stroke="#D4AF37" stroke-width="1.5"/>
          <path d="M12 5v8m-4-3 4 4 4-4M7 17h10" stroke="#F0D06E" stroke-width="2.2" fill="none" stroke-linecap="round" stroke-linejoin="round"/></svg>
        <span class="t">تحميل هذا الفيديو</span><span class="s">IBDM Pro</span><span class="x" title="إخفاء">✕</span>
      </div>`;
    button = root.querySelector('.b');
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
    label.textContent = 'تحميل هذا الفيديو';
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
    label.textContent = 'جاري الإرسال…';
    chrome.runtime.sendMessage({ cmd: 'media', src, pageUrl: location.href, title: document.title }, res => {
      if (chrome.runtime.lastError) { label.textContent = 'أعد تحميل الصفحة'; return; }
      label.textContent = res && res.ok ? '✓ تم — اختر الجودة بالبرنامج' : 'شغّل برنامج IBDM Pro أولاً';
      setTimeout(() => { if (label) label.textContent = 'تحميل هذا الفيديو'; }, 3500);
    });
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
