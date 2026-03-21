from __future__ import annotations

from itertools import cycle

from .focas import controller_mode_text, derive_oee_status
from .models import MachineStatus, SystemInfo, utc_now


class MockFocasClient:
    def __init__(self, running_modes: list[int]):
        self._running_modes = set(running_modes)
        self._states = cycle(
            [
                (1, 0, 0, 0),
                (1, 1, 0, 0),
                (1, 3, 0, 0),
                (1, 0, 1, 0),
                (1, 0, 0, 1),
            ]
        )
        self._connected = False
        self._timer_totals = {
            "power_on_total_ms": 0,
            "operating_total_ms": 0,
            "cutting_total_ms": 0,
            "cycle_total_ms": 0,
            "free_total_ms": 0,
        }

    def connect(self) -> None:
        self._connected = True

    def disconnect(self) -> None:
        self._connected = False

    def read_system_info(self) -> SystemInfo:
        self._ensure_connected()
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
        self._ensure_connected()
        automatic_mode, operation_mode, alarm_state, emergency_state = next(self._states)
        self._timer_totals["power_on_total_ms"] += 100
        if operation_mode in self._running_modes:
            self._timer_totals["operating_total_ms"] += 100
            self._timer_totals["cutting_total_ms"] += 80
            self._timer_totals["cycle_total_ms"] += 100
        elif alarm_state == 0 and emergency_state == 0:
            self._timer_totals["free_total_ms"] += 100
        controller_mode_number = automatic_mode
        controller_mode = controller_mode_text(controller_mode_number)
        oee_number, oee_text = derive_oee_status(
            operation_mode=operation_mode,
            alarm_state=alarm_state,
            emergency_state=emergency_state,
            running_modes=self._running_modes,
        )
        return MachineStatus(
            collected_at=utc_now(),
            machine_online=1,
            automatic_mode=automatic_mode,
            operation_mode=operation_mode,
            emergency_state=emergency_state,
            alarm_state=alarm_state,
            controller_mode_number=controller_mode_number,
            controller_mode_text=controller_mode,
            oee_status_number=oee_number,
            oee_status_text=oee_text,
            native_power_on_total_ms=self._timer_totals["power_on_total_ms"],
            native_operating_total_ms=self._timer_totals["operating_total_ms"],
            native_cutting_total_ms=self._timer_totals["cutting_total_ms"],
            native_cycle_total_ms=self._timer_totals["cycle_total_ms"],
            native_free_total_ms=self._timer_totals["free_total_ms"],
            raw_payload={
                "automatic_mode": automatic_mode,
                "operation_mode": operation_mode,
                "emergency_state": emergency_state,
                "alarm_state": alarm_state,
                "controller_mode_number": controller_mode_number,
                "oee_status_number": oee_number,
                **self._timer_totals,
            },
        )

    def read_processing_time_records(self) -> list[dict[str, int]]:
        return [
            {
                "program_number": 1234,
                "hour": 0,
                "minute": 12,
                "second": 30,
                "duration_ms": 750000,
            }
        ]

    def read_alarm_details(self) -> list[dict[str, int | str]]:
        return [
            {
                "alarm_number": 1001,
                "alarm_type": 0,
                "axis": 1,
                "message": "MOCK ALARM",
            }
        ]

    def _ensure_connected(self) -> None:
        if not self._connected:
            raise RuntimeError("Mock client is not connected")
