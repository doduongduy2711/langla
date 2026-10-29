"""Crop and consensus regression checks using generated images and a stub model."""
import importlib.util
import io
from pathlib import Path
import tempfile
from PIL import Image, ImageDraw

spec = importlib.util.spec_from_file_location("captcha_ocr", Path(__file__).with_name("CaptchaOcr.py"))
ocr = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ocr)


class Model:
    def __init__(self, values=("942B",) * 4):
        self.values = iter(values)
        self.sizes = []

    def classification(self, data):
        with Image.open(io.BytesIO(data)) as image:
            self.sizes.append(image.size)
        return next(self.values)


with tempfile.TemporaryDirectory() as folder:
    path = Path(folder) / "generated.png"
    for background in ((223, 217, 229), (210, 210, 210), (217, 225, 245)):
        Image.new("RGB", (340, 100), background).save(path)
        model = Model()
        result = ocr.recognize(path, engine=model, native_frame=True)
        assert result["region"] == [0, 0, 340, 100]
        assert model.sizes == [(356, 116)] * 4, "native frame clipped or color detector used"
        assert result["status"] == "ok"
    model = Model(("942B", "942B", "942B", "9428"))
    assert ocr.recognize(path, engine=model, native_frame=True)["code"] is None, "conflicting codes accepted"
    for values, expected in [
        (("10x5=?", "10×5=", "10*5?", "noise"), "50"),
        (("2+2=?",)*4, "4"),
        (("2-5=?",)*4, "-3"),
        (("10/5=?",)*4, "2"),
        (("10/0=?",)*4, None),
        (("10/3=?",)*4, None),
        (("1X23",)*4, None),
        (("2+2=?", "2+2=?", "2*2=?", "noise"), None),
    ]:
        result = ocr.recognize(path, engine=Model(values), native_frame=True)
        assert result['code'] == expected, (values, result)
    assert ocr.parse_math('__import__("os")') is None
    screenshot = Image.new("RGB", (500, 300), "black")
    ImageDraw.Draw(screenshot).rectangle((70, 80, 409, 179), fill=(217, 225, 245))
    screenshot.save(path)
    result = ocr.recognize(path, engine=Model())
    assert result["region"][0] > 70 and result["region"][1] > 80, "screenshot panel detection lost"

print("PASS: native frames retain the complete image on purple, gray and blue backgrounds; screenshot crop and strict consensus remain intact.")
