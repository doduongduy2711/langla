"""Read the four-character code from a saved game screenshot. No game input."""
from __future__ import annotations

import argparse
import io
import json
import re
import sys
from collections import Counter
from pathlib import Path


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


def recognize(path: Path, crop=None, length=4, debug_dir=None, keep_case=False, engine=None):
    import cv2
    import ddddocr
    import numpy as np
    from PIL import Image, ImageOps

    with Image.open(path) as source:
        image = ImageOps.exif_transpose(source).convert("RGB")
    rgb = np.asarray(image)
    region = tuple(crop) if crop else find_region(rgb, cv2, np)
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
    votes = Counter(c["text"] for c in candidates if c["valid"])
    ranked = votes.most_common()
    top, count = ranked[0] if ranked else (None, 0)
    tied = len(ranked) > 1 and ranked[1][1] == count
    accepted = bool(top and count >= 2 and not tied and len(ranked) == 1)
    return {
        "code": top if accepted else None,
        "status": "ok" if accepted else "needs_review",
        "region": list(region),
        "votes": count,
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
            path = Path(json.loads(line)["image"])
            result = recognize(path, engine=engine)
            if result["code"]:
                print("OK\t" + result["code"], flush=True)
            else:
                print("REVIEW", flush=True)
        except Exception:
            print("ERROR", flush=True)

if __name__ == "__main__":
    if "--worker" in sys.argv:
        worker()
    else:
        raise SystemExit(main())
