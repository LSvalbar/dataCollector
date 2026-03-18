from __future__ import annotations

import csv
import json
import queue
import sqlite3
import threading
from datetime import datetime, timezone
from pathlib import Path

from .models import CounterDelta, MachineStatus, WriteEnvelope, isoformat_utc


_SENTINEL = object()


class StorageWriter:
    def __init__(self, db_path: str, max_queue_size: int, batch_size: int):
        self._db_path = Path(db_path)
        self._queue: queue.Queue[WriteEnvelope | object] = queue.Queue(maxsize=max_queue_size)
        self._batch_size = batch_size
        self._thread = threading.Thread(target=self._run, name="sqlite-writer", daemon=True)
        self._started = False

    def start(self) -> None:
        self._db_path.parent.mkdir(parents=True, exist_ok=True)
        self._started = True
        self._thread.start()

    def stop(self) -> None:
        if not self._started:
            return
        self._queue.put(_SENTINEL)
        self._thread.join()

    def enqueue(self, envelope: WriteEnvelope) -> None:
        self._queue.put(envelope)

    def _run(self) -> None:
        connection = sqlite3.connect(str(self._db_path), timeout=30, check_same_thread=False)
        connection.execute("PRAGMA journal_mode=WAL;")
        connection.execute("PRAGMA synchronous=NORMAL;")
        connection.execute("PRAGMA temp_store=MEMORY;")
        connection.execute("PRAGMA foreign_keys=ON;")
        self._init_schema(connection)

        pending: list[WriteEnvelope] = []
        while True:
            try:
                item = self._queue.get(timeout=1.0)
            except queue.Empty:
                item = None

            if item is _SENTINEL:
                if pending:
                    self._flush(connection, pending)
                    pending.clear()
                break

            if item is None:
                if pending:
                    self._flush(connection, pending)
                    pending.clear()
                continue

            pending.append(item)
            if len(pending) >= self._batch_size:
                self._flush(connection, pending)
                pending.clear()

        connection.close()

    def _init_schema(self, connection: sqlite3.Connection) -> None:
        connection.executescript(
            """
            CREATE TABLE IF NOT EXISTS machine_info (
                machine_name TEXT PRIMARY KEY,
                ip TEXT NOT NULL,
                port INTEGER NOT NULL,
                max_axis_number INTEGER NOT NULL,
                cnc_type TEXT NOT NULL,
                machine_type TEXT NOT NULL,
                series_number TEXT NOT NULL,
                version_number TEXT NOT NULL,
                axis_count INTEGER NOT NULL,
                additional_info INTEGER NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS poll_snapshots (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                collected_at TEXT NOT NULL,
                machine_online INTEGER NOT NULL,
                automatic_mode INTEGER NOT NULL,
                operation_mode INTEGER NOT NULL,
                emergency_state INTEGER NOT NULL,
                alarm_state INTEGER NOT NULL,
                controller_mode_number INTEGER NOT NULL,
                controller_mode_text TEXT NOT NULL,
                oee_status_number INTEGER NOT NULL,
                oee_status_text TEXT NOT NULL,
                raw_json TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_poll_snapshots_collected_at
                ON poll_snapshots (collected_at DESC);

            CREATE TABLE IF NOT EXISTS state_transitions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                state_key TEXT NOT NULL,
                previous_value TEXT,
                current_value TEXT NOT NULL,
                changed_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_state_transitions_key_time
                ON state_transitions (state_key, changed_at DESC);

            CREATE TABLE IF NOT EXISTS daily_counters (
                day TEXT PRIMARY KEY,
                power_on_ms INTEGER NOT NULL DEFAULT 0,
                run_ms INTEGER NOT NULL DEFAULT 0,
                alarm_ms INTEGER NOT NULL DEFAULT 0,
                emergency_ms INTEGER NOT NULL DEFAULT 0,
                sample_count INTEGER NOT NULL DEFAULT 0,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS latest_values (
                key TEXT PRIMARY KEY,
                value_text TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """
        )
        connection.commit()

    def _flush(self, connection: sqlite3.Connection, pending: list[WriteEnvelope]) -> None:
        latest_rows: dict[str, tuple[str, str]] = {}
        counter_days_to_refresh: set[str] = set()

        with connection:
            for envelope in pending:
                if envelope.snapshot is not None:
                    snapshot = envelope.snapshot
                    connection.execute(
                        """
                        INSERT INTO poll_snapshots (
                            collected_at,
                            machine_online,
                            automatic_mode,
                            operation_mode,
                            emergency_state,
                            alarm_state,
                            controller_mode_number,
                            controller_mode_text,
                            oee_status_number,
                            oee_status_text,
                            raw_json
                        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                        """,
                        (
                            isoformat_utc(snapshot.collected_at),
                            snapshot.machine_online,
                            snapshot.automatic_mode,
                            snapshot.operation_mode,
                            snapshot.emergency_state,
                            snapshot.alarm_state,
                            snapshot.controller_mode_number,
                            snapshot.controller_mode_text,
                            snapshot.oee_status_number,
                            snapshot.oee_status_text,
                            json.dumps(snapshot.raw_payload, ensure_ascii=True, separators=(",", ":")),
                        ),
                    )
                    latest_rows.update(_latest_values_from_snapshot(snapshot))

                if envelope.transitions:
                    latest_rows.update(_latest_values_from_transitions(envelope.transitions))
                    connection.executemany(
                        """
                        INSERT INTO state_transitions (
                            state_key,
                            previous_value,
                            current_value,
                            changed_at
                        ) VALUES (?, ?, ?, ?)
                        """,
                        [
                            (
                                transition.state_key,
                                transition.previous_value,
                                transition.current_value,
                                isoformat_utc(transition.changed_at),
                            )
                            for transition in envelope.transitions
                        ],
                    )

                if envelope.system_info is not None and envelope.snapshot is not None:
                    info = envelope.system_info
                    snapshot = envelope.snapshot
                    connection.execute(
                        """
                        INSERT INTO machine_info (
                            machine_name,
                            ip,
                            port,
                            max_axis_number,
                            cnc_type,
                            machine_type,
                            series_number,
                            version_number,
                            axis_count,
                            additional_info,
                            updated_at
                        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                        ON CONFLICT(machine_name) DO UPDATE SET
                            ip = excluded.ip,
                            port = excluded.port,
                            max_axis_number = excluded.max_axis_number,
                            cnc_type = excluded.cnc_type,
                            machine_type = excluded.machine_type,
                            series_number = excluded.series_number,
                            version_number = excluded.version_number,
                            axis_count = excluded.axis_count,
                            additional_info = excluded.additional_info,
                            updated_at = excluded.updated_at
                        """,
                        (
                            str(snapshot.raw_payload.get("machine_name", "")),
                            str(snapshot.raw_payload.get("machine_ip", "")),
                            int(snapshot.raw_payload.get("machine_port", 0)),
                            info.max_axis_number,
                            info.cnc_type,
                            info.machine_type,
                            info.series_number,
                            info.version_number,
                            info.axis_count,
                            info.additional_info,
                            isoformat_utc(snapshot.collected_at),
                        ),
                    )

                if envelope.counter_delta is not None:
                    counter = envelope.counter_delta
                    day = counter.collected_at.astimezone(timezone.utc).date().isoformat()
                    counter_days_to_refresh.add(day)
                    connection.execute(
                        """
                        INSERT INTO daily_counters (
                            day,
                            power_on_ms,
                            run_ms,
                            alarm_ms,
                            emergency_ms,
                            sample_count,
                            updated_at
                        ) VALUES (?, ?, ?, ?, ?, ?, ?)
                        ON CONFLICT(day) DO UPDATE SET
                            power_on_ms = daily_counters.power_on_ms + excluded.power_on_ms,
                            run_ms = daily_counters.run_ms + excluded.run_ms,
                            alarm_ms = daily_counters.alarm_ms + excluded.alarm_ms,
                            emergency_ms = daily_counters.emergency_ms + excluded.emergency_ms,
                            sample_count = daily_counters.sample_count + excluded.sample_count,
                            updated_at = excluded.updated_at
                        """,
                        (
                            day,
                            counter.power_on_ms,
                            counter.run_ms,
                            counter.alarm_ms,
                            counter.emergency_ms,
                            counter.sample_count,
                            isoformat_utc(counter.collected_at),
                        ),
                    )

            for day in sorted(counter_days_to_refresh):
                latest_rows.update(_latest_counter_keys(connection, day))

            if latest_rows:
                connection.executemany(
                    """
                    INSERT INTO latest_values (key, value_text, updated_at)
                    VALUES (?, ?, ?)
                    ON CONFLICT(key) DO UPDATE SET
                        value_text = excluded.value_text,
                        updated_at = excluded.updated_at
                    """,
                    [(key, value_text, updated_at) for key, (value_text, updated_at) in latest_rows.items()],
                )


def _latest_values_from_snapshot(snapshot: MachineStatus) -> dict[str, tuple[str, str]]:
    updated_at = isoformat_utc(snapshot.collected_at)
    return {
        "machine_name": (str(snapshot.raw_payload.get("machine_name", "")), updated_at),
        "machine_ip": (str(snapshot.raw_payload.get("machine_ip", "")), updated_at),
        "machine_port": (str(snapshot.raw_payload.get("machine_port", "")), updated_at),
        "machine_online": (str(snapshot.machine_online), updated_at),
        "automatic_mode": (str(snapshot.automatic_mode), updated_at),
        "operation_mode": (str(snapshot.operation_mode), updated_at),
        "emergency_state": (str(snapshot.emergency_state), updated_at),
        "alarm_state": (str(snapshot.alarm_state), updated_at),
        "controller_mode_number": (str(snapshot.controller_mode_number), updated_at),
        "controller_mode_text": (snapshot.controller_mode_text, updated_at),
        "oee_status_number": (str(snapshot.oee_status_number), updated_at),
        "oee_status_text": (snapshot.oee_status_text, updated_at),
    }


def _latest_values_from_transitions(transitions) -> dict[str, tuple[str, str]]:
    latest_rows: dict[str, tuple[str, str]] = {}
    for transition in transitions:
        if transition.state_key != "machine_online":
            continue

        changed_at = isoformat_utc(transition.changed_at)
        if transition.previous_value == "0" and transition.current_value == "1":
            latest_rows["observed_power_on_at"] = (changed_at, changed_at)
        elif transition.previous_value == "1" and transition.current_value == "0":
            latest_rows["observed_power_off_at"] = (changed_at, changed_at)

    return latest_rows


def _latest_counter_keys(connection: sqlite3.Connection, day: str) -> dict[str, tuple[str, str]]:
    row = connection.execute(
        """
        SELECT power_on_ms, run_ms, updated_at
        FROM daily_counters
        WHERE day = ?
        """,
        (day,),
    ).fetchone()
    if row is None:
        return {}

    power_on_ms, run_ms, updated_at = row
    return {
        "counter_day": (day, updated_at),
        "today_power_on_ms": (str(power_on_ms), updated_at),
        "today_processing_ms": (str(run_ms), updated_at),
    }


def export_snapshots_to_csv(db_path: str, output_path: str) -> None:
    output = Path(output_path)
    output.parent.mkdir(parents=True, exist_ok=True)

    with sqlite3.connect(db_path) as connection, output.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle)
        writer.writerow(
            [
                "collected_at",
                "machine_online",
                "automatic_mode",
                "operation_mode",
                "emergency_state",
                "alarm_state",
                "controller_mode_number",
                "controller_mode_text",
                "oee_status_number",
                "oee_status_text",
                "raw_json",
            ]
        )
        for row in connection.execute(
            """
            SELECT
                collected_at,
                machine_online,
                automatic_mode,
                operation_mode,
                emergency_state,
                alarm_state,
                controller_mode_number,
                controller_mode_text,
                oee_status_number,
                oee_status_text,
                raw_json
            FROM poll_snapshots
            ORDER BY collected_at ASC
            """
        ):
            writer.writerow(row)


def read_latest_values(db_path: str) -> list[tuple[str, str, str]]:
    with sqlite3.connect(db_path) as connection:
        return list(
            connection.execute(
                """
                SELECT key, value_text, updated_at
                FROM latest_values
                ORDER BY key ASC
                """
            )
        )
