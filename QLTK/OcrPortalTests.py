import json
import re
import tempfile
import threading
import unittest
import urllib.error
import urllib.request
from pathlib import Path

from OcrPortal import Store, make_server


class PortalTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.root = Path(self.directory.name)
        self.image = self.root / 'sample.bmp'
        self.image.write_bytes(b'synthetic-image-fixture')
        self.calls = []

        def reader(path):
            self.calls.append(path.name)
            return {'status': 'ok', 'code': 'AB12', 'candidates': [{'text': 'AB12', 'method': 'fixture'}]}

        self.store = Store(self.root, reader)
        self.server = make_server(self.store, 0)
        self.url = 'http://127.0.0.1:' + str(self.server.server_port)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        html = urllib.request.urlopen(self.url).read().decode()
        self.token = json.loads(re.search(r'const TOKEN=("[^"]+")', html).group(1))

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join()
        self.directory.cleanup()

    def request(self, path, body=None, token=None):
        headers = {}
        data = None
        if body is not None:
            data = json.dumps(body).encode()
            headers = {'Content-Type': 'application/json', 'X-Portal-Token': token or self.token}
        return json.loads(urllib.request.urlopen(urllib.request.Request(self.url + path, data=data, headers=headers)).read())

    def test_auto_read_does_not_become_ground_truth(self):
        self.assertTrue(self.store.scan_one())
        self.assertFalse(self.store.scan_one())
        self.assertEqual(self.calls, ['sample.bmp'])
        self.assertFalse(self.image.with_suffix('.label.txt').exists())
        item = self.request('/api/item?name=sample.bmp')
        self.assertEqual(item['code'], 'AB12')
        self.assertIsNone(item['label'])
        self.request('/api/label', {'name': 'sample.bmp', 'code': 'xy34'})
        self.assertEqual(self.image.with_suffix('.label.txt').read_text().strip(), 'XY34')
        self.assertEqual(self.request('/api/items')['verified'], 1)

    def test_new_images_queue_and_predictions_persist(self):
        self.store.scan_one()
        other = self.root / 'next.bmp'
        other.write_bytes(b'next-fixture')
        self.assertTrue(self.store.scan_one())
        self.assertEqual(self.calls, ['sample.bmp', 'next.bmp'])
        restarted = Store(self.root, lambda path: self.fail('Cached image read twice'))
        self.assertFalse(restarted.scan_one())
        self.request('/api/retry', {'name': 'sample.bmp'})
        self.assertTrue(self.store.scan_one())

    def test_error_is_reviewable_and_can_be_corrected(self):
        def fail(path):
            raise ValueError('fixture OCR failure')
        self.store.reader = fail
        self.store.scan_one()
        self.assertEqual(self.request('/api/item?name=sample.bmp')['status'], 'error')
        self.request('/api/label', {'name': 'sample.bmp', 'code': 'a1b2'})
        self.assertEqual(self.request('/api/item?name=sample.bmp')['label'], 'A1B2')

    def test_path_token_and_validation_guards(self):
        for name in ('../outside.bmp', '..\\outside.bmp', '/outside.bmp', 'missing.bmp'):
            with self.assertRaises(ValueError):
                self.store.path(name)
        for value in ('123', 'ABCDE', 'A!12'):
            with self.assertRaises(urllib.error.HTTPError) as error:
                self.request('/api/label', {'name': 'sample.bmp', 'code': value})
            self.assertEqual(error.exception.code, 400)
        with self.assertRaises(urllib.error.HTTPError) as error:
            self.request('/api/label', {'name': 'sample.bmp', 'code': 'AB12'}, token='wrong')
        self.assertEqual(error.exception.code, 403)
        request = urllib.request.Request(self.url + '/api/items', headers={'Host': 'external.example'})
        with self.assertRaises(urllib.error.HTTPError) as error:
            urllib.request.urlopen(request)
        self.assertEqual(error.exception.code, 403)


if __name__ == '__main__':
    unittest.main()
