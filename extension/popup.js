// IBDM Pro popup: app status, pause capturing, and the videos found on this tab.
const $ = id => document.getElementById(id);

function fmtSize(b) {
  if (!(b > 0)) return '';
  const u = ['B', 'KB', 'MB', 'GB'];
  let i = 0;
  while (b >= 1024 && i < u.length - 1) { b /= 1024; i++; }
  return `${b.toFixed(i ? 1 : 0)} ${u[i]}`;
}

function shortName(url) {
  try {
    const u = new URL(url);
    const last = decodeURIComponent(u.pathname.split('/').pop() || '');
    return last || u.hostname;
  } catch (e) { return url; }
}

async function load() {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  const st = await chrome.runtime.sendMessage({ cmd: 'status', tabId: tab ? tab.id : -1 });
  $('ver').textContent = 'v' + st.version;
  const box = $('status');
  if (st.app) {
    box.className = 'status on';
    $('statusText').textContent = `متصل ببرنامج IBDM Pro ${st.app.version}` + (st.app.version !== st.version ? ' (حدّث الإضافة من صفحة الإضافات ⟳)' : '');
  } else {
    box.className = 'status off';
    $('statusText').textContent = 'برنامج IBDM Pro غير مشغّل — شغّله من قائمة ابدأ';
  }
  $('capture').checked = !st.paused && !!(st.app && st.app.capture);
  $('capture').disabled = !st.app;

  const list = $('media');
  list.innerHTML = '';
  if (!st.media.length) {
    const p = document.createElement('div');
    p.className = 'empty';
    p.textContent = 'لم يتم رصد ملفات فيديو بعد — شغّل الفيديو أو استخدم زر «تحميل فيديو هذه الصفحة».';
    list.appendChild(p);
  }
  for (const m of st.media.slice().reverse()) {
    const row = document.createElement('div');
    row.className = 'item';
    const n = document.createElement('div');
    n.className = 'n';
    const name = document.createElement('div');
    name.textContent = shortName(m.url);
    name.title = m.url;
    const meta = document.createElement('small');
    meta.textContent = (m.type === 'stream' ? 'بث (m3u8)' : m.type === 'audio' ? 'صوت' : 'فيديو') + (m.size > 0 ? ' — ' + fmtSize(m.size) : '');
    n.append(name, meta);
    const b = document.createElement('button');
    b.textContent = 'تحميل';
    b.onclick = () => send({ src: m.url, kind: 'media' }, tab, b);
    row.append(n, b);
    list.appendChild(row);
  }
  $('pageVideo').onclick = () => send({ src: '', kind: 'media' }, tab, $('pageVideo'));
}

function send(opts, tab, btn) {
  chrome.runtime.sendMessage({ cmd: 'media', src: opts.src, kind: opts.kind, pageUrl: tab.url, tabUrl: tab.url, title: tab.title }, res => {
    btn.textContent = res && res.ok ? '✓ تم' : 'البرنامج غير مشغّل';
    if (res && res.ok) setTimeout(() => window.close(), 700);
  });
}

$('capture').addEventListener('change', async e => {
  await chrome.runtime.sendMessage({ cmd: 'pause', paused: !e.target.checked });
});

load();
