from __future__ import annotations

from pathlib import Path
import shutil
import tempfile
import unittest

import app_gui


class EntrypointTest(unittest.TestCase):
    def test_ensure_runtime_layout_copies_example_config(self) -> None:
        temp_dir = tempfile.mkdtemp(prefix="fanuc-entrypoint-test-")
        try:
            root = Path(temp_dir) / "runtime"
            resource_root = Path(temp_dir) / "bundle"
            config_dir = resource_root / "config"
            vendor_dir = resource_root / "vendor"
            config_dir.mkdir(parents=True, exist_ok=True)
            vendor_dir.mkdir(parents=True, exist_ok=True)

            example_path = config_dir / "machine.local.example.json"
            example_payload = '{"machine":{"name":"demo"}}'
            example_path.write_text(example_payload, encoding="utf-8")
            (vendor_dir / "Fwlib32.dll").write_bytes(b"demo-dll")

            config_path = app_gui.ensure_runtime_layout(root, resource_root)

            self.assertEqual(config_path, root / "config" / "machine.local.json")
            self.assertTrue(config_path.exists())
            self.assertEqual(config_path.read_text(encoding="utf-8"), example_payload)
            self.assertTrue((root / "data").exists())
            self.assertTrue((root / "logs").exists())
            self.assertEqual((root / "config" / "machine.local.example.json").read_text(encoding="utf-8"), example_payload)
            self.assertEqual((root / "vendor" / "Fwlib32.dll").read_bytes(), b"demo-dll")
        finally:
            shutil.rmtree(temp_dir, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()
