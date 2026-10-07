import tempfile
import io
import json
import unittest
from pathlib import Path
from unittest.mock import Mock

from deploy_r2 import cleanup, delete_release, deploy, release_plan


class MissingObject(Exception):
    response = {"Error": {"Code": "NoSuchKey"}}


def client_mock(latest=None):
    client = Mock()
    if latest:
        client.get_object.side_effect = lambda **kwargs: {
            "Body": io.BytesIO(json.dumps(latest).encode())}
    else:
        client.get_object.side_effect = MissingObject()
    client.get_paginator.return_value.paginate.return_value = []
    return client


class DeploymentTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "Build").mkdir()
        (self.root / "StreamingAssets").mkdir()
        for name in ("index.html", "Build/game.loader.js", "Build/game.data.br",
                     "Build/game.framework.js.br", "Build/game.wasm.br",
                     "StreamingAssets/socket.io.min.js"):
            (self.root / name).write_bytes(b"example")
        self.uploads, self.key, self.manifest = release_plan(
            self.root, "https://assets.example.com", "xyz", "a" * 40)

    def test_prefix_urls_and_brotli_headers(self):
        self.assertEqual(self.key, "xyz/latest.json")
        self.assertEqual(self.manifest["codeUrl"],
                         "https://assets.example.com/xyz/" + "a" * 40 + "/Build/game.wasm.br")
        wasm = next(headers for _, key, headers in self.uploads if key.endswith(".wasm.br"))
        self.assertEqual(wasm["ContentEncoding"], "br")
        self.assertEqual(wasm["ContentType"], "application/wasm")
        self.assertTrue(any("StreamingAssets/" in key for _, key, _ in self.uploads))

    def test_incomplete_build_rejected(self):
        (self.root / "Build/game.wasm.br").unlink()
        with self.assertRaises(ValueError):
            release_plan(self.root, "https://assets.example.com", "xyz", "a" * 40)

    def test_failed_upload_keeps_previous_manifest(self):
        client = client_mock()
        client.upload_file.side_effect = RuntimeError("upload failed")
        with self.assertRaises(RuntimeError):
            deploy(client, "abc", self.uploads, self.key, self.manifest)
        self.assertFalse(any(c.kwargs.get("Key") == self.key for c in client.put_object.call_args_list))

    def test_bad_metadata_keeps_previous_manifest(self):
        client = client_mock()
        client.head_object.return_value = {"ContentLength": 7}
        with self.assertRaises(RuntimeError):
            deploy(client, "abc", self.uploads, self.key, self.manifest)
        self.assertFalse(any(c.kwargs.get("Key") == self.key for c in client.put_object.call_args_list))

    def test_success_publishes_manifest_last(self):
        client = client_mock()
        client.head_object.side_effect = [
            {"ContentLength": path.stat().st_size, **headers}
            for path, _, headers in self.uploads]
        deploy(client, "abc", self.uploads, self.key, self.manifest)
        self.assertEqual(client.put_object.call_args.kwargs["Key"], self.key)
        self.assertEqual(client.put_object.call_args.kwargs["CacheControl"], "no-store")

    def test_cleanup_keeps_two_and_only_deletes_managed_folders(self):
        client = client_mock()
        current, previous, old, partial = [char * 40 for char in "abcd"]
        keys = [f"xyz/{sha}/.release.json" for sha in (current, previous, old, partial)]
        keys += ["xyz/latest.json", "xyz/unrelated/file", "xyz/" + "e" * 40 + "/index.html"]
        def pages(**kwargs):
            return [{"Contents": [{"Key": key} for key in keys if key.startswith(kwargs["Prefix"])]}]
        client.get_paginator.return_value.paginate.side_effect = pages
        cleanup(client, "abc", "xyz/", {"commit": current, "previousCommit": previous})
        deleted = {call.kwargs["Key"] for call in client.delete_object.call_args_list}
        self.assertEqual(deleted, {f"xyz/{old}/.release.json", f"xyz/{partial}/.release.json"})

    def test_failed_upload_deletes_only_failed_release(self):
        client = client_mock({"commit": "b" * 40, "previousCommit": "c" * 40})
        client.upload_file.side_effect = RuntimeError("failed")
        failed_key = self.uploads[0][1]
        client.get_paginator.return_value.paginate.return_value = [{"Contents": [{"Key": failed_key}]}]
        with self.assertRaises(RuntimeError):
            deploy(client, "abc", self.uploads, self.key, self.manifest)
        client.delete_object.assert_called_once_with(Bucket="abc", Key=failed_key)

    def test_previous_success_recorded(self):
        client = client_mock({"commit": "b" * 40, "previousCommit": "c" * 40})
        client.head_object.side_effect = [{"ContentLength": p.stat().st_size, **h} for p, _, h in self.uploads]
        deploy(client, "abc", self.uploads, self.key, self.manifest)
        published = json.loads(client.put_object.call_args.kwargs["Body"])
        self.assertEqual(published["previousCommit"], "b" * 40)

    def test_delete_marker_last_for_retry(self):
        client = client_mock()
        root = "xyz/" + "c" * 40 + "/"
        client.get_paginator.return_value.paginate.return_value = [
            {"Contents": [{"Key": root + ".release.json"}, {"Key": root + "index.html"}]}]
        delete_release(client, "abc", "xyz/", "c" * 40)
        self.assertEqual(client.delete_object.call_args.kwargs["Key"], root + ".release.json")

    def test_cleanup_error_does_not_fail_published_release(self):
        client = client_mock()
        client.head_object.side_effect = [{"ContentLength": p.stat().st_size, **h} for p, _, h in self.uploads]
        client.get_paginator.return_value.paginate.side_effect = RuntimeError("list unavailable")
        deploy(client, "abc", self.uploads, self.key, self.manifest)
        self.assertEqual(client.put_object.call_args.kwargs["Key"], self.key)

    def test_rerun_does_not_overwrite_current_files(self):
        client = client_mock({"commit": "a" * 40, "previousCommit": "b" * 40})
        deploy(client, "abc", self.uploads, self.key, self.manifest)
        client.upload_file.assert_not_called()
        client.put_object.assert_not_called()

    def test_ambiguous_publication_error_does_not_delete_current(self):
        client = client_mock()
        reads = [{"commit": "b" * 40}, {"commit": "a" * 40, "previousCommit": "b" * 40}]
        client.get_object.side_effect = lambda **kwargs: {"Body": io.BytesIO(json.dumps(reads.pop(0)).encode())}
        client.head_object.side_effect = [{"ContentLength": p.stat().st_size, **h} for p, _, h in self.uploads]
        client.put_object.side_effect = [None, RuntimeError("response lost")]
        with self.assertRaises(RuntimeError):
            deploy(client, "abc", self.uploads, self.key, self.manifest)
        client.delete_object.assert_not_called()


if __name__ == "__main__":
    unittest.main()
