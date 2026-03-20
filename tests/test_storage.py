from __future__ import annotations

from datetime import datetime, timezone
import unittest

from src.fanuc_collector.models import StateTransition
from src.fanuc_collector.storage import _latest_counter_keys, _latest_values_from_transitions


class StorageHelpersTest(unittest.TestCase):
    def test_machine_online_transitions_produce_observed_power_events(self) -> None:
        changed_at = datetime(2026, 3, 18, 12, 30, 0, tzinfo=timezone.utc)
        transitions = [
            StateTransition(
                state_key="machine_online",
                previous_value="0",
                current_value="1",
                changed_at=changed_at,
            ),
            StateTransition(
                state_key="machine_online",
                previous_value="1",
                current_value="0",
                changed_at=changed_at,
            ),
        ]

        latest = _latest_values_from_transitions(transitions)

        self.assertIn("observed_power_on_at", latest)
        self.assertIn("observed_power_off_at", latest)
        self.assertEqual(latest["observed_power_on_at"][0], "2026-03-18T12:30:00.000+00:00")
        self.assertEqual(latest["observed_power_off_at"][0], "2026-03-18T12:30:00.000+00:00")

    def test_latest_counter_keys_include_idle_and_utilization(self) -> None:
        import sqlite3

        connection = sqlite3.connect(":memory:")
        connection.execute(
            """
            CREATE TABLE daily_counters (
                day TEXT PRIMARY KEY,
                power_on_ms INTEGER NOT NULL,
                run_ms INTEGER NOT NULL,
                idle_ms INTEGER NOT NULL,
                alarm_ms INTEGER NOT NULL,
                emergency_ms INTEGER NOT NULL,
                sample_count INTEGER NOT NULL,
                updated_at TEXT NOT NULL
            )
            """
        )
        connection.execute(
            """
            INSERT INTO daily_counters (
                day, power_on_ms, run_ms, idle_ms, alarm_ms, emergency_ms, sample_count, updated_at
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            """,
            ("2026-03-20", 10000, 2500, 7000, 300, 200, 1, "2026-03-20T08:00:00.000+00:00"),
        )

        latest = _latest_counter_keys(connection, "2026-03-20")

        self.assertEqual(latest["today_idle_ms"][0], "7000")
        self.assertEqual(latest["today_alarm_ms"][0], "300")
        self.assertEqual(latest["today_emergency_ms"][0], "200")
        self.assertEqual(latest["today_utilization_percent"][0], "25.00")


if __name__ == "__main__":
    unittest.main()
