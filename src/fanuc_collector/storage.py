from __future__ import annotations

import csv
import json
import queue
import sqlite3
import threading
import time as time_module
from datetime import date, datetime, time, timedelta, timezone, tzinfo as tzinfo_type
from pathlib import Path

from .models import CounterDelta, MachineStatus, TimelineSegment, WriteEnvelope, isoformat_utc


_SENTINEL = object()


class StorageWriter:
    def __init__(
        self,
        db_path: str,
        max_queue_size: int,
        batch_size: int,
        counter_tz: tzinfo_type | None = None,
    ):
        self._db_path = Path(db_path)
        self._queue: queue.Queue[WriteEnvelope | object] = queue.Queue(maxsize=max_queue_size)
        self._batch_size = batch_size
        self._flush_interval_ms = 1000
        self._counter_tz = counter_tz or _resolve_report_timezone(None)
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
        pending_started_at_ms: int | None = None
        while True:
            try:
                item = self._queue.get(timeout=1.0)
            except queue.Empty:
                item = None

            if item is _SENTINEL:
                if pending:
                    self._flush(connection, pending)
                    pending.clear()
                    pending_started_at_ms = None
                break

            if item is None:
                if pending:
                    self._flush(connection, pending)
                    pending.clear()
                    pending_started_at_ms = None
                continue

            if pending_started_at_ms is None:
                pending_started_at_ms = self._monotonic_ms()
            pending.append(item)
            if self._should_flush_pending(pending, pending_started_at_ms):
                self._flush(connection, pending)
                pending.clear()
                pending_started_at_ms = None

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
                machine_state_code TEXT NOT NULL DEFAULT '',
                machine_state_text TEXT NOT NULL DEFAULT '',
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
                cutting_ms INTEGER NOT NULL DEFAULT 0,
                cycle_ms INTEGER NOT NULL DEFAULT 0,
                waiting_ms INTEGER NOT NULL DEFAULT 0,
                idle_ms INTEGER NOT NULL DEFAULT 0,
                spindle_run_ms INTEGER NOT NULL DEFAULT 0,
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
        self._ensure_column(connection, "poll_snapshots", "machine_state_code", "TEXT NOT NULL DEFAULT ''")
        self._ensure_column(connection, "poll_snapshots", "machine_state_text", "TEXT NOT NULL DEFAULT ''")
        self._ensure_column(connection, "daily_counters", "cutting_ms", "INTEGER NOT NULL DEFAULT 0")
        self._ensure_column(connection, "daily_counters", "cycle_ms", "INTEGER NOT NULL DEFAULT 0")
        self._ensure_column(connection, "daily_counters", "waiting_ms", "INTEGER NOT NULL DEFAULT 0")
        self._ensure_column(connection, "daily_counters", "idle_ms", "INTEGER NOT NULL DEFAULT 0")
        self._ensure_column(connection, "daily_counters", "spindle_run_ms", "INTEGER NOT NULL DEFAULT 0")
        connection.commit()

    def _ensure_column(self, connection: sqlite3.Connection, table_name: str, column_name: str, column_sql: str) -> None:
        columns = {
            row[1]
            for row in connection.execute(f"PRAGMA table_info({table_name})")
        }
        if column_name in columns:
            return
        connection.execute(f"ALTER TABLE {table_name} ADD COLUMN {column_name} {column_sql}")

    def _should_flush_pending(self, pending: list[WriteEnvelope], pending_started_at_ms: int) -> bool:
        if len(pending) >= self._batch_size:
            return True
        if pending and pending[-1].transitions:
            return True
        return (self._monotonic_ms() - pending_started_at_ms) >= self._flush_interval_ms

    def _monotonic_ms(self) -> int:
        return time_module.monotonic_ns() // 1_000_000

    def _flush(self, connection: sqlite3.Connection, pending: list[WriteEnvelope]) -> None:
        latest_rows: dict[str, tuple[str, str]] = {}
        counter_days_to_refresh: set[str] = set()

        with connection:
            for envelope in pending:
                if envelope.latest_snapshot is not None:
                    latest_rows.update(_latest_values_from_snapshot(envelope.latest_snapshot))

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
                            machine_state_code,
                            machine_state_text,
                            controller_mode_number,
                            controller_mode_text,
                            oee_status_number,
                            oee_status_text,
                            raw_json
                        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                        """,
                        (
                            isoformat_utc(snapshot.collected_at),
                            snapshot.machine_online,
                            snapshot.automatic_mode,
                            snapshot.operation_mode,
                            snapshot.emergency_state,
                            snapshot.alarm_state,
                            snapshot.machine_state_code,
                            snapshot.machine_state_text,
                            snapshot.controller_mode_number,
                            snapshot.controller_mode_text,
                            snapshot.oee_status_number,
                            snapshot.oee_status_text,
                            json.dumps(snapshot.raw_payload, ensure_ascii=True, separators=(",", ":")),
                        ),
                    )

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
                    day = counter.collected_at.astimezone(self._counter_tz).date().isoformat()
                    counter_days_to_refresh.add(day)
                    connection.execute(
                        """
                        INSERT INTO daily_counters (
                            day,
                            power_on_ms,
                            run_ms,
                            cutting_ms,
                            cycle_ms,
                            waiting_ms,
                            idle_ms,
                            spindle_run_ms,
                            alarm_ms,
                            emergency_ms,
                            sample_count,
                            updated_at
                        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                        ON CONFLICT(day) DO UPDATE SET
                            power_on_ms = daily_counters.power_on_ms + excluded.power_on_ms,
                            run_ms = daily_counters.run_ms + excluded.run_ms,
                            cutting_ms = daily_counters.cutting_ms + excluded.cutting_ms,
                            cycle_ms = daily_counters.cycle_ms + excluded.cycle_ms,
                            waiting_ms = daily_counters.waiting_ms + excluded.waiting_ms,
                            idle_ms = daily_counters.idle_ms + excluded.idle_ms,
                            spindle_run_ms = daily_counters.spindle_run_ms + excluded.spindle_run_ms,
                            alarm_ms = daily_counters.alarm_ms + excluded.alarm_ms,
                            emergency_ms = daily_counters.emergency_ms + excluded.emergency_ms,
                            sample_count = daily_counters.sample_count + excluded.sample_count,
                            updated_at = excluded.updated_at
                        """,
                        (
                            day,
                            counter.power_on_ms,
                            counter.run_ms,
                            counter.cutting_ms,
                            counter.cycle_ms,
                            counter.waiting_ms,
                            counter.idle_ms,
                            counter.spindle_run_ms,
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
    def _optional_number_text(value: int | None) -> str:
        return "" if value is None else str(value)

    return {
        "machine_name": (str(snapshot.raw_payload.get("machine_name", "")), updated_at),
        "machine_ip": (str(snapshot.raw_payload.get("machine_ip", "")), updated_at),
        "machine_port": (str(snapshot.raw_payload.get("machine_port", "")), updated_at),
        "machine_online": (str(snapshot.machine_online), updated_at),
        "machine_state_code": (snapshot.machine_state_code, updated_at),
        "machine_state_text": (snapshot.machine_state_text, updated_at),
        "automatic_mode": (str(snapshot.automatic_mode), updated_at),
        "operation_mode": (str(snapshot.operation_mode), updated_at),
        "emergency_state": (str(snapshot.emergency_state), updated_at),
        "alarm_state": (str(snapshot.alarm_state), updated_at),
        "controller_mode_number": (str(snapshot.controller_mode_number), updated_at),
        "controller_mode_text": (snapshot.controller_mode_text, updated_at),
        "oee_status_number": (str(snapshot.oee_status_number), updated_at),
        "oee_status_text": (snapshot.oee_status_text, updated_at),
        "native_power_on_total_ms": (_optional_number_text(snapshot.native_power_on_total_ms), updated_at),
        "native_operating_total_ms": (_optional_number_text(snapshot.native_operating_total_ms), updated_at),
        "native_cutting_total_ms": (_optional_number_text(snapshot.native_cutting_total_ms), updated_at),
        "native_cycle_total_ms": (_optional_number_text(snapshot.native_cycle_total_ms), updated_at),
        "native_free_total_ms": (_optional_number_text(snapshot.native_free_total_ms), updated_at),
        "spindle_speed_rpm": (str(snapshot.raw_payload.get("spindle_speed_rpm", "")), updated_at),
        "spindle_override_percent": (str(snapshot.raw_payload.get("spindle_override_percent", "")), updated_at),
        "spindle_load_percent": (str(snapshot.raw_payload.get("spindle_load_percent", "")), updated_at),
        "current_program_number": (str(snapshot.raw_payload.get("current_program_number", "")), updated_at),
        "current_program_name": (str(snapshot.raw_payload.get("current_program_name", "")), updated_at),
        "current_program_processing_ms": (str(snapshot.raw_payload.get("current_program_processing_ms", "")), updated_at),
        "current_alarm_text": (str(snapshot.raw_payload.get("current_alarm_text", "")), updated_at),
        "last_processing_program_number": (str(snapshot.raw_payload.get("last_processing_program_number", "")), updated_at),
        "last_processing_duration_ms": (str(snapshot.raw_payload.get("last_processing_duration_ms", "")), updated_at),
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
        SELECT power_on_ms, run_ms, cutting_ms, cycle_ms, waiting_ms, idle_ms, spindle_run_ms, alarm_ms, emergency_ms, updated_at
        FROM daily_counters
        WHERE day = ?
        """,
        (day,),
    ).fetchone()
    if row is None:
        return {}

    power_on_ms, run_ms, cutting_ms, cycle_ms, waiting_ms, idle_ms, spindle_run_ms, alarm_ms, emergency_ms, updated_at = row
    utilization = 0.0 if int(power_on_ms) <= 0 else (int(run_ms) / int(power_on_ms)) * 100.0
    return {
        "counter_day": (day, updated_at),
        "today_power_on_ms": (str(power_on_ms), updated_at),
        "today_processing_ms": (str(run_ms), updated_at),
        "today_cutting_ms": (str(cutting_ms), updated_at),
        "today_cycle_ms": (str(cycle_ms), updated_at),
        "today_waiting_ms": (str(waiting_ms), updated_at),
        "today_idle_ms": (str(idle_ms), updated_at),
        "today_spindle_run_ms": (str(spindle_run_ms), updated_at),
        "today_alarm_ms": (str(alarm_ms), updated_at),
        "today_emergency_ms": (str(emergency_ms), updated_at),
        "today_utilization_percent": (f"{utilization:.2f}", updated_at),
    }


def read_daily_timeline(
    db_path: str,
    day_text: str,
    running_modes: list[int],
    report_tz: tzinfo_type | None = None,
) -> list[TimelineSegment]:
    timezone_value = _resolve_report_timezone(report_tz)
    day_value = date.fromisoformat(day_text)
    start_local = datetime.combine(day_value, time.min, tzinfo=timezone_value)
    end_local = start_local + timedelta(days=1)
    now_local = datetime.now(timezone_value)

    if day_value == now_local.date() and now_local < end_local:
        end_local = now_local

    start_utc = start_local.astimezone(timezone.utc)
    end_utc = end_local.astimezone(timezone.utc)
    start_utc_text = isoformat_utc(start_utc)
    end_utc_text = isoformat_utc(end_utc)

    with sqlite3.connect(db_path) as connection:
        connection.row_factory = sqlite3.Row

        previous_row = connection.execute(
            """
            SELECT
                collected_at,
                machine_online,
                automatic_mode,
                operation_mode,
                emergency_state,
                alarm_state,
                machine_state_code,
                raw_json
            FROM poll_snapshots
            WHERE collected_at < ?
            ORDER BY collected_at DESC
            LIMIT 1
            """,
            (start_utc_text,),
        ).fetchone()

        rows = list(
            connection.execute(
                """
                SELECT
                    collected_at,
                    machine_online,
                    automatic_mode,
                    operation_mode,
                    emergency_state,
                    alarm_state,
                    machine_state_code,
                    raw_json
                FROM poll_snapshots
                WHERE collected_at >= ? AND collected_at <= ?
                ORDER BY collected_at ASC
                """,
                (start_utc_text, end_utc_text),
            )
        )

    points = []
    if previous_row is not None:
        points.append(previous_row)
    points.extend(rows)

    if not points:
        return []

    segments: list[TimelineSegment] = []
    running_mode_set = set(running_modes)

    for index, row in enumerate(points):
        current_at = datetime.fromisoformat(row["collected_at"])
        next_at = end_utc if index + 1 >= len(points) else datetime.fromisoformat(points[index + 1]["collected_at"])
        interval_start = max(current_at, start_utc)
        interval_end = min(next_at, end_utc)
        if interval_end <= interval_start:
            continue

        state_code = str(row["machine_state_code"] or "").strip()
        if not state_code:
            raw_payload = _parse_raw_json(row["raw_json"])
            state_code = _timeline_state_code(
                machine_online=int(row["machine_online"]),
                automatic_mode=int(row["automatic_mode"]),
                operation_mode=int(row["operation_mode"]),
                emergency_state=int(row["emergency_state"]),
                alarm_state=int(row["alarm_state"]),
                spindle_speed_rpm=int(raw_payload.get("spindle_speed_rpm", 0) or 0),
                running_modes=running_mode_set,
            )
        _append_timeline_segment(segments, state_code, interval_start, interval_end)

    return segments


def _resolve_report_timezone(report_tz: tzinfo_type | None) -> tzinfo_type:
    if report_tz is not None:
        return report_tz
    local_timezone = datetime.now().astimezone().tzinfo
    return local_timezone or timezone.utc


def _timeline_state_code(
    machine_online: int,
    automatic_mode: int,
    operation_mode: int,
    emergency_state: int,
    alarm_state: int,
    spindle_speed_rpm: int,
    running_modes: set[int],
) -> str:
    from .state_logic import classify_machine_state

    state_code, _ = classify_machine_state(
        machine_online=machine_online,
        automatic_mode=automatic_mode,
        operation_mode=operation_mode,
        emergency_state=emergency_state,
        alarm_state=alarm_state,
        spindle_speed_rpm=spindle_speed_rpm,
        configured_running_modes=running_modes,
    )
    return state_code


def _parse_raw_json(value: str | None) -> dict:
    if not value:
        return {}
    try:
        return json.loads(value)
    except json.JSONDecodeError:
        return {}


def _append_timeline_segment(
    segments: list[TimelineSegment],
    state_code: str,
    start_at: datetime,
    end_at: datetime,
) -> None:
    duration_ms = max(0, int((end_at - start_at).total_seconds() * 1000))
    if duration_ms <= 0:
        return

    if segments and segments[-1].state_code == state_code and segments[-1].end_at == start_at:
        last_segment = segments[-1]
        last_segment.end_at = end_at
        last_segment.duration_ms += duration_ms
        return

    segments.append(
        TimelineSegment(
            state_code=state_code,
            start_at=start_at,
            end_at=end_at,
            duration_ms=duration_ms,
        )
    )


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
                "machine_state_code",
                "machine_state_text",
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
                machine_state_code,
                machine_state_text,
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
