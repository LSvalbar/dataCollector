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
from src.fanuc_collector.focas import FocasCommunicationError
from src.fanuc_collector.models import MachineStatus, SystemInfo, utc_now
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

    def test_transient_disconnect_does_not_write_power_off_snapshot(self) -> None:
        class FlakyClient:
            def __init__(self):
                self.connected = False
                self.read_count = 0
                self.power_ms = 0
                self.operating_ms = 0
                self.cutting_ms = 0
                self.free_ms = 0

            def connect(self) -> None:
                self.connected = True

            def disconnect(self) -> None:
                self.connected = False

            def read_system_info(self) -> SystemInfo:
                return SystemInfo(
                    max_axis_number=32,
                    cnc_type="0",
                    machine_type="T",
                    series_number="D7F3",
                    version_number="13.0",
                    axis_count=2,
                    additional_info=1026,
                )

            def read_status(self) -> MachineStatus:
                self.read_count += 1
                if self.read_count == 2:
                    raise FocasCommunicationError("transient disconnect")

                self.power_ms += 100
                self.operating_ms += 100
                self.cutting_ms += 80
                collected_at = utc_now()
                return MachineStatus(
                    collected_at=collected_at,
                    machine_online=1,
                    automatic_mode=1,
                    operation_mode=3,
                    emergency_state=0,
                    alarm_state=0,
                    controller_mode_number=1,
                    controller_mode_text="Memory",
                    oee_status_number=3,
                    oee_status_text="Running",
                    native_power_on_total_ms=self.power_ms,
                    native_operating_total_ms=self.operating_ms,
                    native_cutting_total_ms=self.cutting_ms,
                    native_cycle_total_ms=self.operating_ms,
                    native_free_total_ms=self.free_ms,
                    raw_payload={},
                )

            def read_spindle_metrics(self) -> dict[str, int]:
                return {
                    "spindle_speed_rpm": 1800,
                    "spindle_override_percent": 100,
                    "spindle_load_percent": 40,
                }

            def read_current_program(self) -> dict[str, int | str]:
                return {
                    "current_program_number": 1234,
                    "current_program_name": "O1234 TEST",
                }

            def read_processing_time_records(self) -> list[dict[str, int]]:
                return [
                    {
                        "program_number": 1234,
                        "hour": 0,
                        "minute": 1,
                        "second": 0,
                        "duration_ms": 60_000,
                    }
                ]

            def read_alarm_details(self) -> list[dict[str, int | str]]:
                return []

        temp_dir = tempfile.mkdtemp(prefix="fanuc-transient-gap-test-")
        try:
            root = Path(temp_dir)
            db_path = root / "collector.db"
            log_path = root / "collector.log"
            config_path = self._write_config(root, db_path, log_path)

            runtime = CollectorRuntime(load_config(config_path))
            runtime._client = FlakyClient()
            thread = threading.Thread(target=runtime.run, daemon=True)
            thread.start()
            time.sleep(1.2)
            runtime.stop()
            thread.join(timeout=5)

            with sqlite3.connect(db_path) as connection:
                offline_count = connection.execute(
                    "SELECT COUNT(*) FROM poll_snapshots WHERE machine_online = 0"
                ).fetchone()[0]

            self.assertEqual(offline_count, 0)
        finally:
            time.sleep(0.2)
            shutil.rmtree(temp_dir, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()
