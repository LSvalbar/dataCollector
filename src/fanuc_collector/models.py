from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime, timezone


def utc_now() -> datetime:
    return datetime.now(timezone.utc)


def isoformat_utc(value: datetime) -> str:
    return value.astimezone(timezone.utc).isoformat(timespec="milliseconds")


@dataclass(slots=True)
class SystemInfo:
    max_axis_number: int
    cnc_type: str
    machine_type: str
    series_number: str
    version_number: str
    axis_count: int
    additional_info: int


@dataclass(slots=True)
class MachineStatus:
    collected_at: datetime
    machine_online: int
    automatic_mode: int
    operation_mode: int
    emergency_state: int
    alarm_state: int
    controller_mode_number: int
    controller_mode_text: str
    oee_status_number: int
    oee_status_text: str
    machine_state_code: str = "unknown"
    machine_state_text: str = "未知"
    native_power_on_total_ms: int | None = None
    native_operating_total_ms: int | None = None
    native_cutting_total_ms: int | None = None
    native_cycle_total_ms: int | None = None
    native_free_total_ms: int | None = None
    raw_payload: dict[str, int | str] = field(default_factory=dict)

    @classmethod
    def offline(cls, collected_at: datetime) -> "MachineStatus":
        return cls(
            collected_at=collected_at,
            machine_online=0,
            automatic_mode=-1,
            operation_mode=-1,
            emergency_state=0,
            alarm_state=0,
            controller_mode_number=-1,
            controller_mode_text="Offline",
            oee_status_number=0,
            oee_status_text="Offline",
            machine_state_code="power_off",
            machine_state_text="关机",
            native_power_on_total_ms=None,
            native_operating_total_ms=None,
            native_cutting_total_ms=None,
            native_cycle_total_ms=None,
            native_free_total_ms=None,
            raw_payload={},
        )


@dataclass(slots=True)
class StateTransition:
    state_key: str
    previous_value: str | None
    current_value: str
    changed_at: datetime


@dataclass(slots=True)
class CounterDelta:
    collected_at: datetime
    power_on_ms: int = 0
    run_ms: int = 0
    cutting_ms: int = 0
    cycle_ms: int = 0
    waiting_ms: int = 0
    idle_ms: int = 0
    spindle_run_ms: int = 0
    alarm_ms: int = 0
    emergency_ms: int = 0
    sample_count: int = 0


@dataclass(slots=True)
class WriteEnvelope:
    system_info: SystemInfo | None = None
    snapshot: MachineStatus | None = None
    latest_snapshot: MachineStatus | None = None
    transitions: list[StateTransition] = field(default_factory=list)
    counter_delta: CounterDelta | None = None


@dataclass(slots=True)
class TimelineSegment:
    state_code: str
    start_at: datetime
    end_at: datetime
    duration_ms: int
