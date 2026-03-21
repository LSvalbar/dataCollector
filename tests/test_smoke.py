from __future__ import annotations

import json
from pathlib import Path
import sqlite3
import shutil
import tempfile
import threading
import time
import unittest

from src.fanuc_collector.config import load_config
from src.fanuc_collector.runtime import CollectorRuntime


class SmokeTest(unittest.TestCase):
    def _write_config(self, root: Path, db_path: Path, log_path: Path) -> Path:
        config_path = root / "machine.json"
        config_path.write_text(
            json.dumps(
                {
                    "machine": {
                        "name": "mock-fanuc",
                        "ip": "127.0.0.1",
                        "port": 8193,
                        "connect_timeout_sec": 5,
                        "poll_interval_ms": 100,
                        "snapshot_interval_ms": 300,
                        "running_operation_modes": [1, 2, 3],
                        "focas_dll_path": "vendor/fwlib64.dll",
                        "mock_mode": True,
                    },
                    "storage": {
                        "db_path": str(db_path),
                        "queue_max_size": 1000,
                        "writer_batch_size": 20,
                    },
                    "runtime": {
                        "log_path": str(log_path),
                        "log_level": "INFO",
                        "reconnect_initial_ms": 100,
                        "reconnect_max_ms": 1000,
                    },
                }
            ),
            encoding="utf-8",
        )
        return config_path

    def test_mock_collection_writes_data(self) -> None:
        temp_dir = tempfile.mkdtemp(prefix="fanuc-collector-test-")
        try:
            root = Path(temp_dir)
            db_path = root / "collector.db"
            log_path = root / "collector.log"
            config_path = self._write_config(root, db_path, log_path)

            runtime = CollectorRuntime(load_config(config_path))
            thread = threading.Thread(target=runtime.run, daemon=True)
            thread.start()
            time.sleep(1.2)
            runtime.stop()
            thread.join(timeout=5)

            with sqlite3.connect(db_path) as connection:
                snapshot_count = connection.execute("SELECT COUNT(*) FROM poll_snapshots").fetchone()[0]
                transition_count = connection.execute("SELECT COUNT(*) FROM state_transitions").fetchone()[0]
                latest_count = connection.execute("SELECT COUNT(*) FROM latest_values").fetchone()[0]
                latest_map = {
                    key: value_text
                    for key, value_text, _ in connection.execute(
                        "SELECT key, value_text, updated_at FROM latest_values"
                    )
                }

            self.assertGreater(snapshot_count, 0)
            self.assertGreater(transition_count, 0)
            self.assertGreater(latest_count, 0)
            self.assertIn("today_power_on_ms", latest_map)
            self.assertIn("today_processing_ms", latest_map)
            self.assertIn("today_spindle_run_ms", latest_map)
            self.assertIn("today_cutting_ms", latest_map)
            self.assertIn("today_cycle_ms", latest_map)
            self.assertIn("today_waiting_ms", latest_map)
            self.assertIn("today_idle_ms", latest_map)
            self.assertIn("today_alarm_ms", latest_map)
            self.assertIn("today_emergency_ms", latest_map)
            self.assertIn("today_utilization_percent", latest_map)
            self.assertIn("spindle_speed_rpm", latest_map)
            self.assertIn("spindle_override_percent", latest_map)
            self.assertIn("spindle_load_percent", latest_map)
            self.assertIn("current_program_number", latest_map)
            self.assertIn("current_program_name", latest_map)
            self.assertIn("current_program_processing_ms", latest_map)
            self.assertIn("machine_state_text", latest_map)
            self.assertGreater(int(latest_map["today_power_on_ms"]), 0)
            self.assertGreaterEqual(int(latest_map["today_processing_ms"]), 0)
        finally:
            time.sleep(0.2)
            shutil.rmtree(temp_dir, ignore_errors=True)

    def test_mock_collection_flushes_latest_values_while_running(self) -> None:
        temp_dir = tempfile.mkdtemp(prefix="fanuc-collector-live-test-")
        try:
            root = Path(temp_dir)
            db_path = root / "collector.db"
            log_path = root / "collector.log"
            config_path = self._write_config(root, db_path, log_path)

            runtime = CollectorRuntime(load_config(config_path))
            thread = threading.Thread(target=runtime.run, daemon=True)
            thread.start()

            time.sleep(1.4)

            with sqlite3.connect(db_path) as connection:
                latest_count = connection.execute("SELECT COUNT(*) FROM latest_values").fetchone()[0]

            runtime.stop()
            thread.join(timeout=5)

            self.assertGreater(latest_count, 0)
        finally:
            time.sleep(0.2)
            shutil.rmtree(temp_dir, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()
