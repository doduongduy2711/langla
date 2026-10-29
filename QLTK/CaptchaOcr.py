"""Read the four-character code from a saved game screenshot. No game input."""
from __future__ import annotations

import argparse
import io
import json
import re
import sys
import unicodedata
from collections import Counter
from pathlib import Path


def parse_math(raw):
    """Parse one bounded integer operation; never execute OCR text."""
    text = re.sub(r'\s+', '', unicodedata.normalize('NFKC', str(raw))).upper()
    text = text.replace('×', '*').replace('÷', '/').replace('−', '-').replace(':', '/')
    match = re.fullmatch(r'([0-9O]{1,4})([+\-*/X])([0-9O]{1,4})(?:=\??|\?)?', text)
    if not match:
        return None
    left, operator, right = match.group(1, 2, 3)
    strong = operator != 'X' or '=' in text or '?' in text
    if not strong and ('O' in left or 'O' in right):
        return None
    a, b = int(left.replace('O', '0')), int(right.replace('O', '0'))
    operator = '*' if operator == 'X' else operator
    if operator == '/' and (b == 0 or a % b):
        return {'strong': strong, 'expression': f'{a} / {b}', 'result': None}
    value = {'+': lambda: a+b, '-': lambda: a-b, '*': lambda: a*b, '/': lambda: a//b}[operator]()
    result = str(value)
    return {'strong': strong, 'expression': f'{a} {operator} {b}', 'result': result if len(result) <= 7 else None}


def resolve_candidates(candidates):
    parsed = [parse_math(c.get('raw', c.get('text', ''))) for c in candidates]
    if any(parsed):
        votes = Counter((m['expression'], m['result']) for m in parsed if m and m['result'] is not None)
        ranked = votes.most_common()
        (expression, result), count = ranked[0] if ranked else ((None, None), 0)
        strong = any(m and m['strong'] for m in parsed)
        expressions = {m['expression'] for m in parsed if m}
        accepted = strong and count >= 2 and len(expressions) == 1 and len(ranked) == 1
        for c, m in zip(candidates, parsed):
            c['valid'] = False
            if m:
                c.update(expression=m['expression'], result=m['result'])
        return {'kind': 'math' if strong else 'ambiguous', 'expression': expression,
                'math_result': result, 'code': result if accepted else None,
                'votes': count, 'status': 'ok' if accepted else 'needs_review'}
    ranked = Counter(c['text'] for c in candidates if c.get('valid')).most_common()
    top, count = ranked[0] if ranked else (None, 0)
    accepted = bool(top and count >= 2 and len(ranked) == 1)
    return {'kind': 'code', 'code': top if accepted else None,
            'votes': count, 'status': 'ok' if accepted else 'needs_review'}


def find_region(rgb, cv2, np):
    """Locate the pale blue panel used by the supplied game screenshot."""
    a = rgb.astype(np.int16)
    mask = (
        (a[:, :, 2] >= 190)
        & (a[:, :, 1] >= 150)
        & (a[:, :, 0] >= 140)
        & (a[:, :, 2] - a[:, :, 0] >= 8)
        & (a.max(axis=2) - a.min(axis=2) < 70)
    ).astype(np.uint8)
    mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, np.ones((9, 17), np.uint8))
    _, _, stats, _ = cv2.connectedComponentsWithStats(mask, connectivity=8)
    height, width = rgb.shape[:2]
    panels = [
        s for s in stats[1:]
        if s[2] >= max(100, width * 0.4)
        and s[3] >= 40 and s[4] >= 1000
        and s[2] > s[3] * 1.5
    ]
    if not panels:
        raise ValueError("Khong tim thay khung ma. Hay chi dinh --crop X Y W H.")
    x, y, w, h, _ = map(int, max(panels, key=lambda s: s[4]))
    # Leave the dialog and lower border out of the OCR area.
    return x + int(w * .12), y + int(h * .15), int(w * .76), int(h * .57)


def recognize(path: Path, crop=None, length=4, debug_dir=None, keep_case=False, engine=None, native_frame=False):
    import cv2
    import ddddocr
    import numpy as np
    from PIL import Image, ImageOps

    with Image.open(path) as source:
        image = ImageOps.exif_transpose(source).convert("RGB")
    rgb = np.asarray(image)
    # Native captures already contain only the captcha image; its background varies.
    region = tuple(crop) if crop else ((0, 0, image.width, image.height) if native_frame else find_region(rgb, cv2, np))
    x, y, w, h = region
    if x < 0 or y < 0 or w < 1 or h < 1 or x + w > image.width or y + h > image.height:
        raise ValueError("Vung --crop nam ngoai anh hoac co kich thuoc khong hop le.")
    cut = rgb[y:y + h, x:x + w]
    hsv = cv2.cvtColor(cut, cv2.COLOR_RGB2HSV)
    colored = ((hsv[:, :, 1] > 65) & (hsv[:, :, 2] < 210)).astype(np.uint8) * 255
    opened = cv2.morphologyEx(colored, cv2.MORPH_OPEN, np.ones((3, 2), np.uint8))
    variants = {
        "original": cut,
        "colored_text": 255 - colored,
        "grayscale": cv2.cvtColor(cut, cv2.COLOR_RGB2GRAY),
        "thin_lines_removed": 255 - opened,
    }
    # Initialize once for all preprocessing variants. The model runs locally.
    engine = engine or ddddocr.DdddOcr(show_ad=False)
    candidates = []
    if debug_dir:
        debug_dir.mkdir(parents=True, exist_ok=True)
        image.crop((x, y, x + w, y + h)).save(debug_dir / "region.png")
    for name, pixels in variants.items():
        prepared = ImageOps.expand(Image.fromarray(pixels), border=8, fill="white")
        buffer = io.BytesIO()
        prepared.save(buffer, format="PNG")
        raw = engine.classification(buffer.getvalue()).strip()
        text = raw if keep_case else raw.upper()
        # Never fill missing characters or turn punctuation into a guessed digit.
        valid = re.fullmatch(r"[A-Za-z0-9]{%d}" % length, text) is not None
        candidates.append({"method": name, "raw": raw, "text": text, "valid": valid})
        if debug_dir:
            prepared.save(debug_dir / (name + ".png"))
    return {
        **resolve_candidates(candidates),
        "region": list(region),
        "attempts": len(candidates),
        "candidates": candidates,
    }


def main():
    parser = argparse.ArgumentParser(description="Doc ma chu/so tu anh TheGioiLangLa.")
    parser.add_argument("image", type=Path, help="Duong dan anh PNG/JPG")
    parser.add_argument("--crop", type=int, nargs=4, metavar=("X", "Y", "W", "H"))
    parser.add_argument("--length", type=int, default=4)
    parser.add_argument("--keep-case", action="store_true", help="Giu hoa/thuong do OCR doc")
    parser.add_argument("--json", action="store_true", help="Xuat ket qua va cac phuong an")
    parser.add_argument("--debug-dir", type=Path, help="Luu anh cat va cac ban loc de doi chieu")
    args = parser.parse_args()
    if not 1 <= args.length <= 16:
        parser.error("--length phai tu 1 den 16")
    try:
        result = recognize(args.image, args.crop, args.length, args.debug_dir, args.keep_case)
    except ImportError as exc:
        print("Thieu thu vien. Chay: python -m pip install -r requirements.txt", file=sys.stderr)
        print(str(exc), file=sys.stderr)
        return 1
    except (OSError, ValueError) as exc:
        print("Loi: " + str(exc), file=sys.stderr)
        return 1
    if args.json:
        print(json.dumps(result, ensure_ascii=False, indent=2))
    elif result["code"]:
        print(result["code"])
    else:
        print("Chua doc chac chan. Chay them --json --debug-dir debug de doi chieu.", file=sys.stderr)
    return 0 if result["code"] else 2


def worker():
    import os
    os.environ["OMP_NUM_THREADS"] = "1"
    import cv2
    import onnxruntime as ort
    cv2.setNumThreads(1)
    original_session = ort.InferenceSession
    def session(*args, **kwargs):
        options = ort.SessionOptions()
        options.intra_op_num_threads = 1
        options.inter_op_num_threads = 1
        kwargs.setdefault("sess_options", options)
        return original_session(*args, **kwargs)
    ort.InferenceSession = session
    import ddddocr
    engine = ddddocr.DdddOcr(show_ad=False)
    sys.stdin.reconfigure(encoding="utf-8")
    sys.stdout.reconfigure(encoding="utf-8")
    print("READY", flush=True)
    for line in sys.stdin:
        try:
            request = json.loads(line)
            path = Path(request["image"])
            result = recognize(path, engine=engine, native_frame=request.get("native_frame") is True)
            if result["code"]:
                print("OK\t" + result["code"], flush=True)
            else:
                valid = [c for c in result["candidates"] if c["valid"]]
                print("REVIEW\tvalid=%d; distinct=%d; votes=%d" % (len(valid), len({c["text"] for c in valid}), result["votes"]), flush=True)
        except Exception as error:
            message = str(error).replace("\t", " ").replace("\r", " ").replace("\n", " ")[:200]
            print("ERROR\t" + type(error).__name__ + ": " + message, flush=True)


# Local-only review of archived OCR samples; no game control.

import argparse
import hashlib
import io
import json
import os
import re
import secrets
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlsplit

HTML = r'''<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>QLTK · Kiểm tra OCR</title>
<style>
[hidden]{display:none!important}*{box-sizing:border-box}body{margin:0;background:#f3f5f8;color:#182234;font:15px system-ui,sans-serif}header{padding:24px 32px;background:#14233c;color:white;display:flex;justify-content:space-between;align-items:center}h1{font-size:24px;margin:3px 0}small,.muted{color:#68778d}header small{color:#aebed6}button,input,select{font:inherit;border:1px solid #cbd4df;border-radius:8px;padding:10px 13px;background:white;color:#182234}button{cursor:pointer}button:disabled{opacity:.5;cursor:default}.primary{background:#2463eb;color:white;border-color:#2463eb}.toolbar{display:flex;gap:12px;align-items:center;padding:18px 32px;flex-wrap:wrap}.toolbar input{flex:1;min-width:200px}main{display:grid;grid-template-columns:330px 1fr;gap:20px;padding:0 32px 32px;max-width:1400px;margin:auto}.panel{background:white;border:1px solid #e0e6ed;border-radius:14px;padding:22px}.sidebar{padding:12px;max-height:76vh;overflow:auto}.item{display:block;width:100%;text-align:left;border:0;border-bottom:1px solid #edf0f4;border-radius:6px;padding:14px 12px}.item.active{background:#eaf1ff;box-shadow:inset 3px 0 #2463eb}.item span{display:block;font-size:12px;color:#68778d;margin-top:5px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.badge{display:inline-block;padding:4px 9px;border-radius:5px;background:#edf2f8;font-size:12px}.stage{min-height:210px;background:#eaf0f7;border:1px solid #dce4ef;border-radius:10px;display:flex;align-items:center;justify-content:center;margin:20px 0}.stage img{max-width:96%;max-height:300px;image-rendering:auto}h2{font-size:18px;margin:0}.code{font-size:38px;letter-spacing:8px;font-weight:700;margin:12px 0}.actions{display:flex;gap:10px;flex-wrap:wrap;margin:18px 0}.edit input{font-size:24px;letter-spacing:5px;width:190px;text-transform:uppercase}.edit{display:flex;gap:10px;align-items:center;flex-wrap:wrap}.note{font-size:13px;line-height:1.6}.notice{min-height:25px;color:#2463eb;margin-top:12px}.candidate{padding:8px 0;font-size:13px;border-bottom:1px solid #edf0f4}footer{padding:0 32px 24px;font-size:12px;color:#68778d}.pages{display:flex;justify-content:space-between;padding-top:12px}.error{color:#b42318}label.upload{background:white;border-radius:8px;border:1px solid #cbd4df;padding:10px 13px;cursor:pointer}@media(max-width:760px){main{grid-template-columns:1fr;padding:0 14px 20px}.sidebar{max-height:260px}header,.toolbar{padding:18px}.stage{min-height:150px}}
</style>
<header><div><small>QLTK / ẢNH ĐÃ LƯU</small><h1>Kiểm tra kết quả OCR</h1></div><span id="counts">Đang kết nối…</span></header>
<div class="toolbar"><input id="search" placeholder="Tìm mã OCR hoặc tên ảnh" aria-label="Tìm ảnh"><select id="filter" aria-label="Lọc ảnh"><option value="all">Tất cả ảnh</option><option value="review">OCR chưa đọc được</option><option value="unverified">Chưa duyệt</option><option value="done">Đã duyệt</option></select><label class="upload">Thêm ảnh<input type="file" id="upload" accept="image/png,image/jpeg,image/bmp" multiple hidden></label></div>
<main><aside class="panel sidebar"><div id="list"></div><div class="pages"><button id="prev">← Trước</button><span id="page"></span><button id="next">Sau →</button></div></aside><section class="panel"><div style="display:flex;justify-content:space-between;gap:10px"><h2 id="title">Chọn một ảnh</h2><span class="badge" id="state">Tự đọc ảnh mới</span></div><div class="actions"><button id="imagePrev" disabled>← Ảnh trước</button><span id="imageIndex" class="muted" style="align-self:center"></span><button id="imageNext" class="primary" disabled>Ảnh tiếp theo →</button></div><div class="stage"><img id="image" alt="Ảnh cần kiểm tra OCR" hidden></div><small id="resultKind">KẾT QUẢ OCR</small><div class="code" id="code">—</div><p class="muted" id="detail">Ảnh mới sẽ tự được đọc, không cần nhập trước.</p><div class="actions"><button class="primary" id="correct" disabled>Đúng · lưu nhãn</button><button id="edit" disabled>Sửa kết quả</button><button id="retry" disabled>Đọc lại OCR</button></div><div class="edit" id="editor" hidden><input id="label" maxlength="7" autocomplete="off" placeholder="Kết quả đúng" aria-label="Mã bốn ký tự hoặc kết quả phép tính"><button class="primary" id="save">Lưu mã đúng</button></div><div id="notice" class="notice" role="status"></div><details><summary>Các kết quả nhận diện</summary><div id="candidates"></div></details><p class="note muted">Bạn chỉ cần nhập khi OCR đọc sai hoặc chưa đọc được. Với ảnh phép tính, nhập kết quả số (ví dụ: 4). Kết quả OCR được lưu riêng; chỉ mã đã được bạn xác nhận mới thành nhãn dùng cho train. Portal chạy trên máy này và không gửi thao tác vào game.</p></section></main><footer id="connection">Chỉ truy cập qua 127.0.0.1 · Không tải dữ liệu lên mạng</footer>
<script>
const TOKEN=__TOKEN__;let rows=[],selected='',page=0,lastSignature='',detailSignature='',busy=false;const $=id=>document.getElementById(id);const names={pending:'Đợi OCR',reading:'Đang đọc',ok:'OCR đã đọc',needs_review:'Cần duyệt',error:'Lỗi OCR',verified:'Đã duyệt'};
async function api(path,body){let options=body===undefined?{}:{method:'POST',headers:{'Content-Type':'application/json','X-Portal-Token':TOKEN},body:JSON.stringify(body)};let r=await fetch(path,options),j=await r.json();if(!r.ok)throw Error(r.status===403?'Portal vừa cập nhật. Nhấn F5 rồi lưu lại mã.':(j.error||'Không xử lý được yêu cầu'));return j}
function message(t,error=false){$('notice').textContent=t;$('notice').className='notice'+(error?' error':'')}
function filtered(){let q=$('search').value.toLowerCase(),f=$('filter').value;return rows.filter(r=>(r.name+' '+(r.code||r.suggestion||'')+' '+(r.label||'')).toLowerCase().includes(q)&&(f==='all'||f==='review'&&['needs_review','error'].includes(r.status)||f==='done'&&r.label||f==='unverified'&&!r.label))}
function showList(){let all=filtered(),pos=all.findIndex(r=>r.name===selected);$('imagePrev').disabled=!all.length||pos===0;$('imageNext').disabled=!all.length||pos===all.length-1;$('imageIndex').textContent=all.length?(pos<0?'Chọn ảnh':(pos+1)+' / '+all.length):'Không có ảnh';page=Math.min(page,Math.max(0,Math.ceil(all.length/50)-1));let box=$('list');box.replaceChildren();for(let r of all.slice(page*50,page*50+50)){let b=document.createElement('button');b.className='item'+(r.name===selected?' active':'');b.textContent=(r.label||r.code||r.suggestion||'Chưa có mã')+' · '+(names[r.label?'verified':r.status]||r.status);let s=document.createElement('span');s.textContent=r.name;b.append(s);b.onclick=()=>choose(r.name);box.append(b)}if(!all.length)box.textContent='Chưa có ảnh phù hợp.';$('page').textContent=(all.length?page+1:0)+' / '+Math.ceil(all.length/50);$('prev').disabled=page===0;$('next').disabled=(page+1)*50>=all.length}
function choose(name){if(selected!==name&&$('label').value&& !confirm('Bỏ mã đang nhập để chuyển ảnh?'))return;selected=name;let position=filtered().findIndex(r=>r.name===name);if(position>=0)page=Math.floor(position/50);detailSignature='';$('editor').hidden=true;$('label').value='';message('');showList();showDetail()}
async function showDetail(){if(!selected)return;try{let r=await api('/api/item?name='+encodeURIComponent(selected));if(r.name!==selected)return;let sig=JSON.stringify(r);if(sig===detailSignature)return;detailSignature=sig;$('title').textContent='Ảnh '+r.name.slice(0,24)+'…';$('image').hidden=false;$('image').src='/image?name='+encodeURIComponent(r.name);$('state').textContent=names[r.label?'verified':r.status]||r.status;$('code').textContent=r.display_code||r.label||r.code||r.suggestion||'—';$('resultKind').textContent=r.label?'KẾT QUẢ ĐÃ DUYỆT':'KẾT QUẢ OCR';$('detail').textContent=r.expression?('Phép tính: '+r.expression+(r.math_result?' = '+r.math_result:''))+(r.label?' · Nhãn đã duyệt: '+r.label:' · Kiểm tra kết quả trước khi duyệt'):r.label?('Đã lưu nhãn đúng: '+r.label+((r.code||r.suggestion)&&r.label!==(r.code||r.suggestion)?' · OCR ban đầu: '+(r.code||r.suggestion):'')):(r.error||(!r.code?(r.suggestion?'Gợi ý từ kết quả nhận diện; chưa đủ đồng thuận. Bạn kiểm tra và bấm Đúng để lưu nhãn.':'OCR chưa có kết quả thống nhất. Bạn có thể nhập mã đúng.'):'Bạn kiểm tra lại ảnh và mã trước khi duyệt.'));$('correct').disabled=!!r.label||!(r.code||r.suggestion)||busy;$('edit').disabled=busy;$('retry').disabled=['reading','pending'].includes(r.status)||busy;if(!r.code&&!r.suggestion&&!$('label').value)$('editor').hidden=false;let c=$('candidates');c.replaceChildren();for(let v of r.candidates||[]){let e=document.createElement('div');e.className='candidate';e.textContent=(v.method||'')+' → '+(v.text||'(trống)');c.append(e)}}catch(e){message(e.message,true)}}
async function save(code){if(!selected)return;busy=true;try{await api('/api/label',{name:selected,code});$('label').value='';$('editor').hidden=true;message('Đã lưu nhãn đúng: '+code.toUpperCase());detailSignature='';await refresh()}catch(e){message(e.message,true)}finally{busy=false;showDetail()}}
async function refresh(){try{let j=await api('/api/items');rows=j.items;$('counts').textContent=j.total+' ảnh · '+j.verified+' đã duyệt';let sig=JSON.stringify(rows)+$('filter').value+$('search').value;if(sig!==lastSignature){lastSignature=sig;showList()}if(!selected&&rows.length)choose(rows[0].name);await showDetail();$('connection').textContent='Đang kết nối · Tự đọc ảnh mới · Dữ liệu chỉ lưu trên máy này'}catch(e){$('connection').textContent='Chưa kết nối portal: '+e.message}}
function moveImage(delta){let all=filtered(),pos=all.findIndex(r=>r.name===selected),next=pos<0?(delta>0?0:all.length-1):pos+delta;if(next>=0&&next<all.length)choose(all[next].name)}
$('imagePrev').onclick=()=>moveImage(-1);$('imageNext').onclick=()=>moveImage(1);
$('filter').onchange=$('search').oninput=()=>{page=0;showList()};$('prev').onclick=()=>{page--;showList()};$('next').onclick=()=>{page++;showList()};$('edit').onclick=()=>{$('editor').hidden=false;$('label').focus()};$('save').onclick=()=>save($('label').value);$('label').onkeydown=e=>{if(e.key==='Enter')save($('label').value)};$('correct').onclick=()=>{let r=rows.find(r=>r.name===selected);if(!r?.label)save(r?.code||r?.suggestion||'')};$('retry').onclick=async()=>{try{await api('/api/retry',{name:selected});detailSignature='';message('Đã xếp lượt đọc lại');refresh()}catch(e){message(e.message,true)}};
$('upload').onchange=async e=>{try{for(let f of e.target.files){let r=await fetch('/api/upload',{method:'POST',headers:{'X-Portal-Token':TOKEN,'Content-Type':'application/octet-stream'},body:f});let j=await r.json();if(!r.ok)throw Error(j.error);selected=j.name;detailSignature=''}message('Đã thêm ảnh; OCR sẽ tự đọc.');refresh()}catch(e){message(e.message,true)}finally{e.target.value=''}};
refresh();setInterval(refresh,2500);
</script></html>'''


def atomic_json(path, value):
    temp = path.with_name(path.name + '.' + secrets.token_hex(5) + '.tmp')
    try:
        temp.write_text(json.dumps(value, ensure_ascii=False), encoding='utf-8')
        os.replace(temp, path)
    finally:
        temp.unlink(missing_ok=True)


class Store:
    def __init__(self, directory, reader=None):
        self.root = Path(directory).resolve()
        self.root.mkdir(parents=True, exist_ok=True)
        self.reader = reader
        self.engine = None
        self.lock = threading.RLock()
        self.reading = set()
        self.stop = threading.Event()

    def path(self, name):
        if not isinstance(name, str) or not name or Path(name).name != name or '/' in name or '\\' in name or '\x00' in name:
            raise ValueError('Tên ảnh không hợp lệ.')
        path = (self.root / name).resolve()
        if path.parent != self.root or path.suffix.lower() != '.bmp' or not path.is_file():
            raise ValueError('Không tìm thấy ảnh đã lưu.')
        return path

    def files(self):
        return sorted(self.root.glob('*.bmp'), key=lambda p: p.stat().st_mtime_ns, reverse=True)

    def item(self, path):
        result = {'name': path.name, 'status': 'pending', 'code': None, 'label': None}
        try:
            result.update(json.loads(path.with_suffix('.ocr.json').read_text(encoding='utf-8')))
        except (OSError, ValueError):
            pass
        result['name'] = path.name
        if 'kind' not in result and any(parse_math(c.get('raw', c.get('text', ''))) for c in result.get('candidates', [])):
            result.update(resolve_candidates(result['candidates']))
        # A best candidate is a review suggestion, never an auto-accepted code or training label.
        ranked = Counter(c.get('text') for c in result.get('candidates', []) if c.get('valid') and re.fullmatch('[A-Z0-9]{4}', str(c.get('text', '')))).most_common()
        result['suggestion'] = ranked[0][0] if ranked and (len(ranked) == 1 or ranked[0][1] > ranked[1][1]) else None
        if result.get('kind') == 'math':
            result['suggestion'] = result.get('math_result')
        try:
            label = path.with_suffix('.label.txt').read_text(encoding='ascii').strip().upper()
            if len(label) <= 7 and re.fullmatch(r'(?:[A-Z0-9]{4}|-?[0-9]{1,7})', label):
                result['label'] = label
        except OSError:
            pass
        result['display_code'] = result['label'] or result.get('code') or result.get('suggestion')
        if path.name in self.reading:
            result['status'] = 'reading'
        return result

    def label(self, name, code):
        path = self.path(name)
        code = str(code).strip().upper()
        if len(code) > 7 or not re.fullmatch(r'(?:[A-Z0-9]{4}|-?[0-9]{1,7})', code):
            raise ValueError('Nhập mã 4 chữ/số hoặc kết quả phép tính dạng số (ví dụ: 4).')
        target = path.with_suffix('.label.txt')
        temp = target.with_name(target.name + '.' + secrets.token_hex(5) + '.tmp')
        try:
            temp.write_text(code + '\n', encoding='ascii')
            os.replace(temp, target)
        finally:
            temp.unlink(missing_ok=True)
        return code

    def scan_one(self):
        for path in self.files():
            with self.lock:
                if path.name in self.reading or path.with_suffix('.ocr.json').exists():
                    continue
                self.reading.add(path.name)
            try:
                if self.reader:
                    result = self.reader(path)
                else:
                    import ddddocr
                    import sys
                    sys.path.insert(0, str(Path(__file__).resolve().parent))
                    from CaptchaOcr import recognize
                    if self.engine is None:
                        self.engine = ddddocr.DdddOcr(show_ad=False)
                    native = True
                    if path.name.startswith('upload-'):
                        from PIL import Image
                        with Image.open(path) as uploaded:
                            native = uploaded.width / max(1, uploaded.height) > 2 and uploaded.height <= 250
                    result = recognize(path, engine=self.engine, native_frame=native)
                result['read_at'] = time.time()
                atomic_json(path.with_suffix('.ocr.json'), result)
            except Exception as error:
                atomic_json(path.with_suffix('.ocr.json'), {'status': 'error', 'code': None, 'error': str(error)[:240]})
            finally:
                with self.lock:
                    self.reading.discard(path.name)
            return True
        return False

    def work(self):
        while not self.stop.is_set():
            try:
                if self.scan_one():
                    continue
            except OSError:
                pass
            self.stop.wait(1)


def make_server(store, port=47662):
    token_file = store.root / '.portal-session.json'
    try:
        token = json.loads(token_file.read_text(encoding='utf-8'))['token']
        if not isinstance(token, str) or len(token) < 32:
            raise ValueError('Invalid local token')
    except (OSError, ValueError, KeyError):
        token = secrets.token_urlsafe(32)
        atomic_json(token_file, {'token': token})

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *args):
            pass

        def send(self, value, status=200, mime='application/json; charset=utf-8'):
            data = value if isinstance(value, bytes) else json.dumps(value, ensure_ascii=False).encode('utf-8')
            self.send_response(status)
            self.send_header('Content-Type', mime)
            self.send_header('Content-Length', str(len(data)))
            self.send_header('Cache-Control', 'no-store')
            self.send_header('X-Content-Type-Options', 'nosniff')
            self.send_header('X-Frame-Options', 'DENY')
            self.end_headers()
            self.wfile.write(data)

        def valid_host(self):
            return self.headers.get('Host') == '127.0.0.1:' + str(self.server.server_port)

        def do_GET(self):
            if not self.valid_host():
                self.send({'error': 'Chỉ cho phép truy cập local.'}, 403)
                return
            url = urlsplit(self.path)
            try:
                if url.path == '/':
                    self.send(HTML.replace('__TOKEN__', json.dumps(token)).encode('utf-8'), mime='text/html; charset=utf-8')
                elif url.path == '/health':
                    self.send({'app': 'qltk-ocr-portal', 'samples': str(store.root)})
                elif url.path == '/api/items':
                    with store.lock:
                        items = [{k: v for k, v in store.item(p).items() if k in ('name', 'status', 'code', 'suggestion', 'label', 'display_code', 'error')} for p in store.files()]
                    self.send({'items': items, 'total': len(items), 'verified': sum(bool(p['label']) for p in items)})
                elif url.path in ('/api/item', '/image'):
                    path = store.path(parse_qs(url.query).get('name', [''])[0])
                    if url.path == '/image':
                        self.send(path.read_bytes(), mime='image/bmp')
                    else:
                        with store.lock:
                            self.send(store.item(path))
                else:
                    self.send({'error': 'Không có trang này.'}, 404)
            except (ValueError, OSError) as error:
                self.send({'error': str(error)}, 400)

        def do_POST(self):
            origin = self.headers.get('Origin')
            if not self.valid_host() or self.headers.get('X-Portal-Token') != token or origin and origin != 'http://127.0.0.1:' + str(self.server.server_port):
                self.send({'error': 'Yêu cầu không hợp lệ.'}, 403)
                return
            try:
                length = int(self.headers.get('Content-Length', '0'))
                if length < 1 or length > 12 * 1024 * 1024:
                    raise ValueError('Ảnh tối đa 12 MB.')
                body = self.rfile.read(length)
                route = urlsplit(self.path).path
                if route == '/api/upload':
                    from PIL import Image
                    with Image.open(io.BytesIO(body)) as image:
                        if image.width * image.height > 12000000:
                            raise ValueError('Ảnh quá lớn.')
                        output = io.BytesIO()
                        image.convert('RGB').save(output, format='BMP')
                    data = output.getvalue()
                    name = 'upload-' + hashlib.sha256(data).hexdigest() + '.bmp'
                    path = store.root / name
                    with store.lock:
                        if not path.exists():
                            temp = path.with_suffix('.tmp')
                            temp.write_bytes(data)
                            os.replace(temp, path)
                    self.send({'name': name})
                    return
                value = json.loads(body)
                with store.lock:
                    if route == '/api/label':
                        self.send({'code': store.label(value.get('name'), value.get('code'))})
                    elif route == '/api/retry':
                        path = store.path(value.get('name'))
                        if path.name in store.reading:
                            raise ValueError('Ảnh đang được đọc.')
                        path.with_suffix('.ocr.json').unlink(missing_ok=True)
                        self.send({'queued': True})
                    else:
                        self.send({'error': 'Không có thao tác này.'}, 404)
            except (ValueError, OSError, TypeError, AttributeError) as error:
                self.send({'error': str(error)}, 400)

    server = ThreadingHTTPServer(('127.0.0.1', port), Handler)
    server.daemon_threads = True
    return server


def portal_main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--portal', action='store_true')
    parser.add_argument('--samples', required=True)
    parser.add_argument('--port', type=int, default=47662)
    args = parser.parse_args()
    store = Store(args.samples)
    server = make_server(store, args.port)
    threading.Thread(target=store.work, daemon=True).start()
    print('OCR portal: http://127.0.0.1:' + str(server.server_port), flush=True)
    try:
        server.serve_forever()
    finally:
        store.stop.set()
        server.server_close()



if __name__ == "__main__":
    if "--portal" in sys.argv:
        portal_main()
    elif "--worker" in sys.argv:
        worker()
    else:
        raise SystemExit(main())
