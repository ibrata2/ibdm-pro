import json, urllib.request, urllib.parse, secrets, http.cookiejar
H = {"Accept": "application/json, text/javascript, */*, q=0.01", "X-Requested-With": "XMLHttpRequest", "X-APP-VERSION": "a89153f",
     "X-Pinterest-AppState": "active", "X-Pinterest-PWS-Handler": "www/[username].js", "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36",
     "Cookie": "csrftoken=" + secrets.token_hex(16)}
def call(root, res, opts):
    q = urllib.parse.urlencode({"data": json.dumps({"options": opts}), "source_url": ""})
    req = urllib.request.Request(f"{root}/resource/{res}Resource/get/?{q}", headers=H)
    with urllib.request.urlopen(req, timeout=30) as r: return r.geturl(), json.load(r)
for root, user, slug in [("https://www.pinterest.ca", "mashal0407", "cool-diys"), ("https://www.pinterest.ca", "fudohub", "videos"), ("https://www.pinterest.com", "pinterest", "pinterest-presents")]:
    try:
        u, b = call(root, "Board", {"slug": slug, "username": user, "field_set_key": "detailed"})
        d = b["resource_response"]["data"]
        print("BOARD", u[:60], d.get("id"), d.get("name"), d.get("pin_count"), d.get("section_count"), d.get("url"))
        u, f = call(root, "BoardFeed", {"board_id": d["id"], "field_set_key": "react_grid_pin", "prepend": False, "bookmarks": None})
        pins = f["resource_response"]["data"]
        print(" pins page", len(pins), "bookmarks", f.get("resource", {}).get("options", {}).get("bookmarks"))
        for p in pins[:40]:
            if p.get("type") != "pin": print("  type", p.get("type")); continue
            v = p.get("videos"); s = p.get("story_pin_data"); c = p.get("carousel_data")
            print("  pin", p["id"], "title=", (p.get("title") or p.get("grid_title") or "")[:30], "orig=", (p.get("images") or {}).get("orig", {}).get("url"),
                  "video=", list((v or {}).get("video_list", {}).keys()) if v else None, "story=", bool(s), "carousel=", bool(c))
            if s:
                for pg in s.get("pages", [])[:3]:
                    for bl in pg.get("blocks", []): print("    block", bl.get("type"), list(bl.keys())[:8], json.dumps(bl.get("video", {}).get("video_list", {}) if bl.get("video") else bl.get("image", {}))[:300])
            if v: print("    ", json.dumps(v["video_list"])[:600])
    except Exception as e: print("ERR", root, user, slug, e)
