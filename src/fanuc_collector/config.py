from __future__ import annotations

from dataclasses import dataclass
import json
from pathlib import Path


@dataclass(slots=True)
class MachineConfig:
    name: str
    ip: str
    port: int
    connect_timeout_sec: int
    poll_interval_ms: int
    snapshot_interval_ms: int
    running_operation_modes: list[int]
    focas_dll_path: str
    mock_mode: bool


@dataclass(slots=True)
class StorageConfig:
    db_path: str
    queue_max_size: int
    writer_batch_size: int


@dataclass(slots=True)
class RuntimeConfig:
    log_path: str
    log_level: str
    reconnect_initial_ms: int
    reconnect_max_ms: int


@dataclass(slots=True)
class AppConfig:
    machine: MachineConfig
    storage: StorageConfig
    runtime: RuntimeConfig


def _require(data: dict, key: str):
    if key not in data:
        raise ValueError(f"Missing required config key: {key}")
    return data[key]


def load_config(path: str | Path) -> AppConfig:
    config_path = Path(path)
    raw = json.loads(config_path.read_text(encoding="utf-8"))

    machine = _require(raw, "machine")
    storage = _require(raw, "storage")
    runtime = _require(raw, "runtime")

    base_dir = config_path.parent.resolve()

    dll_path = Path(_require(machine, "focas_dll_path"))
    db_path = Path(_require(storage, "db_path"))
    log_path = Path(_require(runtime, "log_path"))

    if not dll_path.is_absolute():
        dll_path = (base_dir.parent / dll_path).resolve()
    if not db_path.is_absolute():
        db_path = (base_dir.parent / db_path).resolve()
    if not log_path.is_absolute():
        log_path = (base_dir.parent / log_path).resolve()

    return AppConfig(
        machine=MachineConfig(
            name=_require(machine, "name"),
            ip=_require(machine, "ip"),
            port=int(_require(machine, "port")),
            connect_timeout_sec=int(_require(machine, "connect_timeout_sec")),
            poll_interval_ms=int(_require(machine, "poll_interval_ms")),
            snapshot_interval_ms=int(_require(machine, "snapshot_interval_ms")),
            running_operation_modes=[int(item) for item in _require(machine, "running_operation_modes")],
            focas_dll_path=str(dll_path),
            mock_mode=bool(_require(machine, "mock_mode")),
        ),
        storage=StorageConfig(
            db_path=str(db_path),
            queue_max_size=int(_require(storage, "queue_max_size")),
            writer_batch_size=int(_require(storage, "writer_batch_size")),
        ),
        runtime=RuntimeConfig(
            log_path=str(log_path),
            log_level=str(_require(runtime, "log_level")).upper(),
            reconnect_initial_ms=int(_require(runtime, "reconnect_initial_ms")),
            reconnect_max_ms=int(_require(runtime, "reconnect_max_ms")),
        ),
    )

