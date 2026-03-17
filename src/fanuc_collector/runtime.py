from __future__ import annotations

import logging
from logging.handlers import RotatingFileHandler
from pathlib import Path
import threading
import time

from .config import AppConfig
from .focas import FocasClient, FocasCommunicationError, FocasLibraryLoadError
from .mock_driver import MockFocasClient
from .models import CounterDelta, MachineStatus, StateTransition, WriteEnvelope, utc_now
from .storage import StorageWriter


TRACKED_FIELDS = (
    "machine_online",
    "automatic_mode",
    "operation_mode",
    "emergency_state",
    "alarm_state",
    "controller_mode_text",
    "oee_status_text",
)


class CollectorRuntime:
    def __init__(self, config: AppConfig):
        self._config = config
        self._logger = configure_logging(config.runtime.log_path, config.runtime.log_level)
        self._stop_event = threading.Event()
        self._writer = StorageWriter(
            db_path=config.storage.db_path,
            max_queue_size=config.storage.queue_max_size,
            batch_size=config.storage.writer_batch_size,
        )
        self._client = self._build_client()

    def run(self) -> int:
        self._writer.start()
        backoff_ms = self._config.runtime.reconnect_initial_ms
        previous_status: MachineStatus | None = None
        previous_time = None
        last_snapshot_ms = 0
        system_info_sent = False

        try:
            while not self._stop_event.is_set():
                try:
                    self._client.connect()
                    self._logger.info(
                        "Connected to machine %s at %s:%s",
                        self._config.machine.name,
                        self._config.machine.ip,
                        self._config.machine.port,
                    )

                    if not system_info_sent:
                        system_info = self._client.read_system_info()
                        status = self._client.read_status()
                        self._decorate_snapshot(status)
                        self._writer.enqueue(
                            WriteEnvelope(
                                system_info=system_info,
                                snapshot=status,
                                transitions=self._build_transitions(previous_status, status),
                            )
                        )
                        system_info_sent = True
                    else:
                        status = self._client.read_status()
                        self._decorate_snapshot(status)
                        self._writer.enqueue(
                            WriteEnvelope(
                                snapshot=status,
                                transitions=self._build_transitions(previous_status, status),
                            )
                        )

                    previous_status = status
                    previous_time = status.collected_at
                    last_snapshot_ms = time.monotonic_ns() // 1_000_000
                    backoff_ms = self._config.runtime.reconnect_initial_ms

                    while not self._stop_event.is_set():
                        loop_started = time.monotonic_ns() // 1_000_000
                        status = self._client.read_status()
                        self._decorate_snapshot(status)

                        counter_delta = None
                        if previous_status is not None and previous_time is not None:
                            elapsed_ms = max(0, int((status.collected_at - previous_time).total_seconds() * 1000))
                            counter_delta = self._build_counter_delta(previous_status, status.collected_at, elapsed_ms)

                        transitions = self._build_transitions(previous_status, status)
                        should_snapshot = self._should_persist_snapshot(
                            last_snapshot_ms=last_snapshot_ms,
                            loop_started_ms=loop_started,
                            has_transitions=bool(transitions),
                        )

                        if should_snapshot or transitions or counter_delta is not None:
                            self._writer.enqueue(
                                WriteEnvelope(
                                    snapshot=status if (should_snapshot or transitions) else None,
                                    transitions=transitions,
                                    counter_delta=counter_delta,
                                )
                            )
                            if should_snapshot:
                                last_snapshot_ms = loop_started

                        previous_status = status
                        previous_time = status.collected_at
                        self._sleep_interval(loop_started)

                except KeyboardInterrupt:
                    self._stop_event.set()
                except (FocasCommunicationError, FocasLibraryLoadError, OSError) as exc:
                    self._logger.warning("Collector error: %s", exc)
                    now = utc_now()
                    counter_delta = None
                    if previous_status is not None and previous_time is not None:
                        elapsed_ms = max(0, int((now - previous_time).total_seconds() * 1000))
                        counter_delta = self._build_counter_delta(previous_status, now, elapsed_ms)

                    offline_status = MachineStatus.offline(now)
                    self._decorate_snapshot(offline_status)
                    transitions = self._build_transitions(previous_status, offline_status)
                    self._writer.enqueue(
                        WriteEnvelope(
                            snapshot=offline_status,
                            transitions=transitions,
                            counter_delta=counter_delta,
                        )
                    )
                    previous_status = offline_status
                    previous_time = now
                    self._client.disconnect()

                    if isinstance(exc, FocasLibraryLoadError):
                        return 2

                    self._logger.info("Reconnect in %sms", backoff_ms)
                    time.sleep(backoff_ms / 1000)
                    backoff_ms = min(backoff_ms * 2, self._config.runtime.reconnect_max_ms)
        finally:
            self._client.disconnect()
            self._writer.stop()
            close_logger(self._logger)

        return 0

    def stop(self) -> None:
        self._stop_event.set()

    def _build_client(self):
        if self._config.machine.mock_mode:
            return MockFocasClient(self._config.machine.running_operation_modes)
        return FocasClient(
            dll_path=self._config.machine.focas_dll_path,
            ip=self._config.machine.ip,
            port=self._config.machine.port,
            timeout_sec=self._config.machine.connect_timeout_sec,
            running_modes=self._config.machine.running_operation_modes,
        )

    def _decorate_snapshot(self, status: MachineStatus) -> None:
        status.raw_payload["machine_name"] = self._config.machine.name
        status.raw_payload["machine_ip"] = self._config.machine.ip
        status.raw_payload["machine_port"] = self._config.machine.port

    def _build_transitions(
        self,
        previous_status: MachineStatus | None,
        current_status: MachineStatus,
    ) -> list[StateTransition]:
        if previous_status is None:
            return [
                StateTransition(
                    state_key=field_name,
                    previous_value=None,
                    current_value=str(getattr(current_status, field_name)),
                    changed_at=current_status.collected_at,
                )
                for field_name in TRACKED_FIELDS
            ]

        transitions: list[StateTransition] = []
        for field_name in TRACKED_FIELDS:
            previous_value = getattr(previous_status, field_name)
            current_value = getattr(current_status, field_name)
            if previous_value != current_value:
                transitions.append(
                    StateTransition(
                        state_key=field_name,
                        previous_value=str(previous_value),
                        current_value=str(current_value),
                        changed_at=current_status.collected_at,
                    )
                )
        return transitions

    def _build_counter_delta(self, previous_status: MachineStatus, collected_at, elapsed_ms: int) -> CounterDelta | None:
        if elapsed_ms <= 0:
            return None

        run_modes = set(self._config.machine.running_operation_modes)
        return CounterDelta(
            collected_at=collected_at,
            power_on_ms=elapsed_ms if previous_status.machine_online else 0,
            run_ms=elapsed_ms if previous_status.operation_mode in run_modes else 0,
            alarm_ms=elapsed_ms if previous_status.alarm_state else 0,
            emergency_ms=elapsed_ms if previous_status.emergency_state else 0,
            sample_count=1,
        )

    def _should_persist_snapshot(self, last_snapshot_ms: int, loop_started_ms: int, has_transitions: bool) -> bool:
        if has_transitions:
            return True
        return (loop_started_ms - last_snapshot_ms) >= self._config.machine.snapshot_interval_ms

    def _sleep_interval(self, loop_started_ms: int) -> None:
        elapsed = (time.monotonic_ns() // 1_000_000) - loop_started_ms
        sleep_ms = max(0, self._config.machine.poll_interval_ms - elapsed)
        if sleep_ms > 0:
            time.sleep(sleep_ms / 1000)


def configure_logging(log_path: str, log_level: str) -> logging.Logger:
    logger = logging.getLogger("fanuc-collector")
    logger.setLevel(getattr(logging, log_level, logging.INFO))
    close_logger(logger)
    logger.handlers.clear()
    logger.propagate = False

    Path(log_path).parent.mkdir(parents=True, exist_ok=True)

    formatter = logging.Formatter("%(asctime)s %(levelname)s %(message)s")

    file_handler = RotatingFileHandler(log_path, maxBytes=2_000_000, backupCount=5, encoding="utf-8")
    file_handler.setFormatter(formatter)
    logger.addHandler(file_handler)

    console_handler = logging.StreamHandler()
    console_handler.setFormatter(formatter)
    logger.addHandler(console_handler)

    return logger


def close_logger(logger: logging.Logger) -> None:
    for handler in list(logger.handlers):
        try:
            handler.flush()
            handler.close()
        finally:
            logger.removeHandler(handler)
