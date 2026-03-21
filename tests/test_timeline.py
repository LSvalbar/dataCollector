from __future__ import annotations

import sqlite3
import tempfile
from pathlib import Path
import shutil
import unittest
from datetime import timezone

from src.fanuc_collector.storage import StorageWriter, read_daily_timeline


class TimelineReportTest(unittest.TestCase):
    def test_read_daily_timeline_returns_processing_and_power_off_segments(self) -> None:
        temp_dir = tempfile.mkdtemp(prefix="fanuc-timeline-test-")
        try:
            db_path = Path(temp_dir) / "timeline.db"
            writer = StorageWriter(str(db_path), max_queue_size=10, batch_size=10)

            with sqlite3.connect(db_path) as connection:
                writer._init_schema(connection)
                connection.executemany(
                    """
                    INSERT INTO poll_snapshots (
                        collected_at,
                        machine_online,
                        automatic_mode,
                        operation_mode,
                        emergency_state,
                        alarm_state,
                        machine_state_code,
                        machine_state_text,
                        controller_mode_number,
                        controller_mode_text,
                        oee_status_number,
                        oee_status_text,
                        raw_json
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    [
                        ("2026-03-10T09:00:00.000+00:00", 1, 1, 3, 0, 0, "processing", "加工", 1, "Memory", 3, "Running", "{\"spindle_speed_rpm\":1800}"),
                        ("2026-03-10T09:30:00.000+00:00", 1, 1, 2, 0, 0, "waiting", "等待", 1, "Memory", 4, "Interrupted", "{\"spindle_speed_rpm\":0}"),
                        ("2026-03-10T17:05:32.000+00:00", 0, -1, -1, 0, 0, "power_off", "关机", -1, "Offline", 0, "Offline", "{}"),
                        ("2026-03-10T20:05:32.000+00:00", 1, 4, 1, 0, 0, "idle", "待机", 4, "Handle", 4, "Interrupted", "{\"spindle_speed_rpm\":0}"),
                    ],
                )
                connection.commit()

            segments = read_daily_timeline(
                str(db_path),
                "2026-03-10",
                running_modes=[1, 2, 3],
                report_tz=timezone.utc,
            )

            processing_segment = next(segment for segment in segments if segment.state_code == "processing")
            power_off_segment = next(segment for segment in segments if segment.state_code == "power_off")

            self.assertEqual(processing_segment.duration_ms, 30 * 60 * 1000)
            self.assertEqual(power_off_segment.duration_ms, 3 * 60 * 60 * 1000)
        finally:
            shutil.rmtree(temp_dir, ignore_errors=True)

    def test_read_daily_timeline_merges_short_power_off_gap_between_same_states(self) -> None:
        temp_dir = tempfile.mkdtemp(prefix="fanuc-timeline-gap-test-")
        try:
            db_path = Path(temp_dir) / "timeline.db"
            writer = StorageWriter(str(db_path), max_queue_size=10, batch_size=10)

            with sqlite3.connect(db_path) as connection:
                writer._init_schema(connection)
                connection.executemany(
                    """
                    INSERT INTO poll_snapshots (
                        collected_at,
                        machine_online,
                        automatic_mode,
                        operation_mode,
                        emergency_state,
                        alarm_state,
                        machine_state_code,
                        machine_state_text,
                        controller_mode_number,
                        controller_mode_text,
                        oee_status_number,
                        oee_status_text,
                        raw_json
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    [
                        ("2026-03-10T06:00:00.000+00:00", 1, 1, 3, 0, 0, "processing", "加工", 1, "Memory", 3, "Running", "{\"spindle_speed_rpm\":1800}"),
                        ("2026-03-10T06:05:00.000+00:00", 0, -1, -1, 0, 0, "power_off", "关机", -1, "Offline", 0, "Offline", "{}"),
                        ("2026-03-10T06:05:01.000+00:00", 1, 1, 3, 0, 0, "processing", "加工", 1, "Memory", 3, "Running", "{\"spindle_speed_rpm\":1800}"),
                        ("2026-03-10T06:10:00.000+00:00", 1, 1, 2, 0, 0, "waiting", "等待", 1, "Memory", 4, "Interrupted", "{\"spindle_speed_rpm\":0}"),
                    ],
                )
                connection.commit()

            segments = read_daily_timeline(
                str(db_path),
                "2026-03-10",
                running_modes=[1, 2, 3],
                report_tz=timezone.utc,
            )

            power_off_segments = [segment for segment in segments if segment.state_code == "power_off"]
            processing_segment = next(segment for segment in segments if segment.state_code == "processing")

            self.assertEqual(power_off_segments, [])
            self.assertEqual(processing_segment.duration_ms, 10 * 60 * 1000)
        finally:
            shutil.rmtree(temp_dir, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()
