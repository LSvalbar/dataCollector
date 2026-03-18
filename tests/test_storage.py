from __future__ import annotations

from datetime import datetime, timezone
import unittest

from src.fanuc_collector.models import StateTransition
from src.fanuc_collector.storage import _latest_values_from_transitions


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


if __name__ == "__main__":
    unittest.main()
