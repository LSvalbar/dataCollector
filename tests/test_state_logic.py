from __future__ import annotations

import unittest

from src.fanuc_collector.state_logic import classify_machine_state, spindle_override_percent


class StateLogicTest(unittest.TestCase):
    def test_processing_state_requires_running_mode_and_spindle_rotation(self) -> None:
        state_code, state_text = classify_machine_state(
            machine_online=1,
            automatic_mode=1,
            operation_mode=3,
            emergency_state=0,
            alarm_state=0,
            spindle_speed_rpm=1800,
            configured_running_modes=[1, 2, 3],
        )

        self.assertEqual(state_code, "processing")
        self.assertEqual(state_text, "加工")

    def test_waiting_state_prefers_auto_memory_when_not_running(self) -> None:
        state_code, _ = classify_machine_state(
            machine_online=1,
            automatic_mode=1,
            operation_mode=2,
            emergency_state=0,
            alarm_state=0,
            spindle_speed_rpm=0,
            configured_running_modes=[1, 2, 3],
        )

        self.assertEqual(state_code, "waiting")

    def test_spindle_override_signal_maps_to_percent(self) -> None:
        self.assertEqual(spindle_override_percent(10), 100)
        self.assertEqual(spindle_override_percent(20), 200)
        self.assertIsNone(spindle_override_percent(-1))


if __name__ == "__main__":
    unittest.main()
