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
            raw_payload={
                "automatic_mode": automatic_mode,
                "operation_mode": operation_mode,
                "emergency_state": emergency_state,
                "alarm_state": alarm_state,
                "controller_mode_number": controller_mode_number,
                "oee_status_number": oee_number,
            },
        )

    def _ensure_connected(self) -> None:
        if not self._connected:
            raise RuntimeError("Mock client is not connected")
