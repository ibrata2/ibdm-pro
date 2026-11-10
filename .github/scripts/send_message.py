"""Adds (or removes) a message in messages.json. Run by the "Send a message" action; inputs come in as env vars."""
import json, os, datetime

path = "messages.json"
try:
    msgs = json.load(open(path, encoding="utf-8"))
except Exception:
    msgs = []

action = os.environ.get("ACTION", "send")
now = datetime.datetime.now(datetime.timezone.utc)
if action == "delete-last":
    if msgs:
        removed = max(msgs, key=lambda m: m.get("id", ""))
        msgs.remove(removed)
        print("removed:", removed.get("title"))
elif action == "delete-all":
    msgs = []
    print("all messages removed")
else:
    title = os.environ.get("TITLE", "").strip()
    text = os.environ.get("TEXT", "").strip().replace("\\n", "\n")
    link = os.environ.get("LINK", "").strip()
    if link and not link.startswith(("http://", "https://")):
        link = "https://" + link
    days = int((os.environ.get("DAYS") or "0").strip() or 0)
    if not title and not text:
        raise SystemExit("write a title or a text")
    msg = {
        "id": now.strftime("%Y%m%d%H%M%S"),
        "title": title,
        "text": text,
        "url": link or None,
        "date": now.strftime("%Y-%m-%d %H:%M"),
        "expires": (now + datetime.timedelta(days=days)).strftime("%Y-%m-%d") if days > 0 else None,
        "lang": os.environ.get("LANG_FOR", "all") or "all",
        "kind": os.environ.get("KIND", "message") or "message",
    }
    msgs.append(msg)
    print("added:", json.dumps(msg, ensure_ascii=False))
    # Keep the file small: drop expired messages.
    today = now.strftime("%Y-%m-%d")
    msgs = [m for m in msgs if not m.get("expires") or m["expires"] >= today]

with open(path, "w", encoding="utf-8") as f:
    json.dump(msgs, f, ensure_ascii=False, indent=1)
    f.write("\n")
