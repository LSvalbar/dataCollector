from __future__ import annotations


AUTO_MDI = 0
AUTO_MEMORY = 1
AUTO_EDIT = 3
AUTO_HANDLE = 4
AUTO_JOG = 5
AUTO_REMOTE = 10

RUN_RESET = 0
RUN_STOP = 1
RUN_HOLD = 2
RUN_START = 3
RUN_MSTR = 4

ACTIVE_RUN_OPERATION_MODES = {RUN_START, RUN_MSTR}
NON_RUNNING_OPERATION_MODES = {RUN_RESET, RUN_STOP, RUN_HOLD}
WAITING_AUTOMATIC_MODES = {AUTO_MEMORY, AUTO_REMOTE}

STATE_LABELS = {
    "power_off": "关机",
    "processing": "加工",
    "running": "等待",
    "waiting": "等待",
    "idle": "待机",
    "alarm": "报警",
    "emergency": "急停",
}


def is_operation_running(operation_mode: int, configured_running_modes: set[int] | list[int] | tuple[int, ...]) -> bool:
    configured = set(configured_running_modes)
    if operation_mode in ACTIVE_RUN_OPERATION_MODES:
        return True
    if operation_mode in NON_RUNNING_OPERATION_MODES:
        return False
    return operation_mode in configured


def spindle_override_percent(raw_signal: int | None) -> int | None:
    if raw_signal is None:
        return None
    if raw_signal < 0:
        return None
    if raw_signal <= 20:
        return raw_signal * 10
    return None


def to_int(value) -> int | None:
    if value in {None, ""}:
        return None
    try:
        return int(float(value))
    except (TypeError, ValueError):
        return None


def classify_machine_state(
    machine_online: int,
    automatic_mode: int,
    operation_mode: int,
    emergency_state: int,
    alarm_state: int,
    spindle_speed_rpm: int | None,
    configured_running_modes: set[int] | list[int] | tuple[int, ...],
) -> tuple[str, str]:
    if int(machine_online) == 0:
        return "power_off", STATE_LABELS["power_off"]

    if int(emergency_state):
        return "emergency", STATE_LABELS["emergency"]

    if int(alarm_state):
        return "alarm", STATE_LABELS["alarm"]

    if is_operation_running(int(operation_mode), configured_running_modes):
        if (spindle_speed_rpm or 0) > 0:
            return "processing", STATE_LABELS["processing"]
        return "waiting", STATE_LABELS["waiting"]

    if int(automatic_mode) in WAITING_AUTOMATIC_MODES:
        return "waiting", STATE_LABELS["waiting"]

    return "idle", STATE_LABELS["idle"]
