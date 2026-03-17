from __future__ import annotations

import json
from pathlib import Path
import threading
import tkinter as tk
from tkinter import filedialog, messagebox, ttk

from .config import load_config
from .runtime import CollectorRuntime
from .storage import export_snapshots_to_csv, read_latest_values


class CollectorGui:
    def __init__(self, root: tk.Tk, config_path: str):
        self.root = root
        self.config_path = Path(config_path).resolve()
        self.runtime: CollectorRuntime | None = None
        self.runtime_thread: threading.Thread | None = None
        self.last_exit_code: int | None = None

        self.root.title("FANUC Single Machine Collector")
        self.root.geometry("1120x760")
        self.root.minsize(980, 680)

        self.status_var = tk.StringVar(value="Stopped")
        self.fields: dict[str, tk.StringVar] = {}

        self._build_ui()
        self._load_form_data()
        self._refresh_loop()
        self.root.protocol("WM_DELETE_WINDOW", self.on_close)

    def _build_ui(self) -> None:
        frame = ttk.Frame(self.root, padding=12)
        frame.pack(fill=tk.BOTH, expand=True)

        title = ttk.Label(frame, text="FANUC Single Machine Collector", font=("Microsoft YaHei UI", 16, "bold"))
        title.grid(row=0, column=0, columnspan=6, sticky="w")

        ttk.Label(frame, text="Status:").grid(row=1, column=0, sticky="w", pady=(8, 4))
        ttk.Label(frame, textvariable=self.status_var, foreground="#0b5").grid(
            row=1, column=1, columnspan=5, sticky="w", pady=(8, 4)
        )

        field_specs = [
            ("Machine Name", "machine.name"),
            ("Machine IP", "machine.ip"),
            ("Port", "machine.port"),
            ("Poll Interval (ms)", "machine.poll_interval_ms"),
            ("Snapshot Interval (ms)", "machine.snapshot_interval_ms"),
            ("Running Modes", "machine.running_operation_modes"),
            ("DLL Path", "machine.focas_dll_path"),
            ("DB Path", "storage.db_path"),
            ("Log Path", "runtime.log_path"),
        ]

        for row_index, (label_text, field_key) in enumerate(field_specs, start=2):
            ttk.Label(frame, text=label_text).grid(row=row_index, column=0, sticky="w", pady=4)
            var = tk.StringVar()
            self.fields[field_key] = var
            entry = ttk.Entry(frame, textvariable=var, width=78)
            entry.grid(row=row_index, column=1, columnspan=4, sticky="ew", padx=(8, 8), pady=4)
            if field_key == "machine.focas_dll_path":
                ttk.Button(frame, text="Browse", command=self.browse_dll).grid(row=row_index, column=5, sticky="ew")

        button_row = ttk.Frame(frame)
        button_row.grid(row=12, column=0, columnspan=6, sticky="ew", pady=(10, 10))

        ttk.Button(button_row, text="Save Config", command=self.save_config).pack(side=tk.LEFT, padx=(0, 8))
        ttk.Button(button_row, text="Start Collector", command=self.start_collector).pack(side=tk.LEFT, padx=(0, 8))
        ttk.Button(button_row, text="Stop Collector", command=self.stop_collector).pack(side=tk.LEFT, padx=(0, 8))
        ttk.Button(button_row, text="Refresh Latest", command=self.refresh_latest_values).pack(side=tk.LEFT, padx=(0, 8))
        ttk.Button(button_row, text="Export CSV", command=self.export_csv).pack(side=tk.LEFT, padx=(0, 8))

        latest_frame = ttk.LabelFrame(frame, text="Latest Values", padding=8)
        latest_frame.grid(row=13, column=0, columnspan=3, sticky="nsew", padx=(0, 8))
        self.latest_text = tk.Text(latest_frame, height=20, width=58, wrap="none")
        self.latest_text.pack(fill=tk.BOTH, expand=True)

        log_frame = ttk.LabelFrame(frame, text="Log Tail", padding=8)
        log_frame.grid(row=13, column=3, columnspan=3, sticky="nsew")
        self.log_text = tk.Text(log_frame, height=20, width=58, wrap="none")
        self.log_text.pack(fill=tk.BOTH, expand=True)

        frame.columnconfigure(1, weight=1)
        frame.columnconfigure(2, weight=1)
        frame.columnconfigure(3, weight=1)
        frame.columnconfigure(4, weight=1)
        frame.rowconfigure(13, weight=1)

    def _load_form_data(self) -> None:
        payload = self._read_config_json()
        self.fields["machine.name"].set(str(payload["machine"]["name"]))
        self.fields["machine.ip"].set(str(payload["machine"]["ip"]))
        self.fields["machine.port"].set(str(payload["machine"]["port"]))
        self.fields["machine.poll_interval_ms"].set(str(payload["machine"]["poll_interval_ms"]))
        self.fields["machine.snapshot_interval_ms"].set(str(payload["machine"]["snapshot_interval_ms"]))
        self.fields["machine.running_operation_modes"].set(
            ",".join(str(item) for item in payload["machine"]["running_operation_modes"])
        )
        self.fields["machine.focas_dll_path"].set(str(payload["machine"]["focas_dll_path"]))
        self.fields["storage.db_path"].set(str(payload["storage"]["db_path"]))
        self.fields["runtime.log_path"].set(str(payload["runtime"]["log_path"]))

    def _read_config_json(self) -> dict:
        if self.config_path.exists():
            return json.loads(self.config_path.read_text(encoding="utf-8"))

        default_payload = {
            "machine": {
                "name": "fanuc-poc",
                "ip": "192.168.91.46",
                "port": 8193,
                "connect_timeout_sec": 10,
                "poll_interval_ms": 500,
                "snapshot_interval_ms": 5000,
                "running_operation_modes": [1, 2, 3],
                "focas_dll_path": "vendor/fwlib64.dll",
                "mock_mode": False,
            },
            "storage": {
                "db_path": "data/fanuc-poc.db",
                "queue_max_size": 10000,
                "writer_batch_size": 200,
            },
            "runtime": {
                "log_path": "logs/fanuc-poc.log",
                "log_level": "INFO",
                "reconnect_initial_ms": 1000,
                "reconnect_max_ms": 30000,
            },
        }
        return default_payload

    def save_config(self) -> None:
        payload = self._read_config_json()
        payload["machine"]["name"] = self.fields["machine.name"].get().strip()
        payload["machine"]["ip"] = self.fields["machine.ip"].get().strip()
        payload["machine"]["port"] = int(self.fields["machine.port"].get().strip())
        payload["machine"]["poll_interval_ms"] = int(self.fields["machine.poll_interval_ms"].get().strip())
        payload["machine"]["snapshot_interval_ms"] = int(self.fields["machine.snapshot_interval_ms"].get().strip())
        payload["machine"]["running_operation_modes"] = [
            int(item.strip()) for item in self.fields["machine.running_operation_modes"].get().split(",") if item.strip()
        ]
        payload["machine"]["focas_dll_path"] = self.fields["machine.focas_dll_path"].get().strip()
        payload["machine"]["mock_mode"] = False
        payload["storage"]["db_path"] = self.fields["storage.db_path"].get().strip()
        payload["runtime"]["log_path"] = self.fields["runtime.log_path"].get().strip()

        self.config_path.parent.mkdir(parents=True, exist_ok=True)
        self.config_path.write_text(json.dumps(payload, indent=2, ensure_ascii=False), encoding="utf-8")
        self.status_var.set(f"Config saved: {self.config_path}")

    def browse_dll(self) -> None:
        selected = filedialog.askopenfilename(
            title="Select FANUC FOCAS DLL",
            filetypes=[("DLL Files", "*.dll"), ("All Files", "*.*")],
        )
        if selected:
            self.fields["machine.focas_dll_path"].set(selected)

    def start_collector(self) -> None:
        if self.runtime_thread and self.runtime_thread.is_alive():
            messagebox.showinfo("Collector", "Collector is already running.")
            return

        try:
            self.save_config()
            self.runtime = CollectorRuntime(load_config(self.config_path))
        except Exception as exc:
            messagebox.showerror("Config Error", str(exc))
            return

        self.last_exit_code = None
        self.runtime_thread = threading.Thread(target=self._run_runtime, name="collector-runtime", daemon=True)
        self.runtime_thread.start()
        self.status_var.set("Collector starting...")

    def _run_runtime(self) -> None:
        assert self.runtime is not None
        self.last_exit_code = self.runtime.run()

    def stop_collector(self) -> None:
        if self.runtime is None:
            self.status_var.set("Collector is not running.")
            return

        self.runtime.stop()
        self.status_var.set("Stop requested...")

    def refresh_latest_values(self) -> None:
        db_path = Path(self.fields["storage.db_path"].get().strip())
        if not db_path.is_absolute():
            db_path = (self.config_path.parent.parent / db_path).resolve()

        if not db_path.exists():
            self._set_text(self.latest_text, "Database not created yet.")
            return

        rows = read_latest_values(str(db_path))
        if not rows:
            self._set_text(self.latest_text, "No latest values yet.")
            return

        lines = [f"{key:24} {value_text:16} {updated_at}" for key, value_text, updated_at in rows]
        self._set_text(self.latest_text, "\n".join(lines))

    def export_csv(self) -> None:
        db_path = Path(self.fields["storage.db_path"].get().strip())
        if not db_path.is_absolute():
            db_path = (self.config_path.parent.parent / db_path).resolve()

        if not db_path.exists():
            messagebox.showwarning("Export CSV", "Database file does not exist yet.")
            return

        default_output = db_path.with_suffix(".csv")
        output_path = filedialog.asksaveasfilename(
            title="Export snapshots to CSV",
            defaultextension=".csv",
            initialfile=default_output.name,
            initialdir=str(default_output.parent),
            filetypes=[("CSV Files", "*.csv")],
        )
        if not output_path:
            return

        export_snapshots_to_csv(str(db_path), output_path)
        self.status_var.set(f"CSV exported: {output_path}")

    def _refresh_loop(self) -> None:
        if self.runtime_thread and self.runtime_thread.is_alive():
            self.status_var.set("Collector running")
        elif self.last_exit_code is not None:
            self.status_var.set(f"Collector stopped with exit code {self.last_exit_code}")
        self.refresh_latest_values()
        self._refresh_log_tail()
        self.root.after(1000, self._refresh_loop)

    def _refresh_log_tail(self) -> None:
        log_path = Path(self.fields["runtime.log_path"].get().strip())
        if not log_path.is_absolute():
            log_path = (self.config_path.parent.parent / log_path).resolve()

        if not log_path.exists():
            self._set_text(self.log_text, "Log file not created yet.")
            return

        lines = log_path.read_text(encoding="utf-8", errors="ignore").splitlines()
        self._set_text(self.log_text, "\n".join(lines[-40:]))

    def _set_text(self, widget: tk.Text, value: str) -> None:
        widget.configure(state="normal")
        widget.delete("1.0", tk.END)
        widget.insert("1.0", value)
        widget.configure(state="disabled")

    def on_close(self) -> None:
        if self.runtime is not None:
            self.runtime.stop()
        if self.runtime_thread is not None and self.runtime_thread.is_alive():
            self.runtime_thread.join(timeout=3)
        self.root.destroy()


def launch_gui(config_path: str) -> int:
    root = tk.Tk()
    CollectorGui(root, config_path)
    root.mainloop()
    return 0
