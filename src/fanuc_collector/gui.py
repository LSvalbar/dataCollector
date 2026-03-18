from __future__ import annotations

from datetime import datetime
import json
from pathlib import Path
import threading
import tkinter as tk
from tkinter import filedialog, messagebox, ttk

from .config import load_config
from .runtime import CollectorRuntime
from .storage import export_snapshots_to_csv, read_daily_timeline, read_latest_values


STATUS_LABELS = {
    "power_off": "关机",
    "processing": "加工",
    "idle": "待机",
    "alarm": "报警",
    "emergency": "急停",
}

LATEST_KEY_LABELS = {
    "machine_name": "机床名称",
    "machine_ip": "机床 IP",
    "machine_port": "端口",
    "machine_online": "在线状态",
    "automatic_mode": "自动模式",
    "operation_mode": "运行模式",
    "emergency_state": "急停状态",
    "alarm_state": "报警状态",
    "controller_mode_number": "控制器模式编号",
    "controller_mode_text": "控制器模式",
    "oee_status_number": "OEE 状态编号",
    "oee_status_text": "OEE 状态",
    "counter_day": "统计日期",
    "today_power_on_ms": "当日开机累计时间(ms)",
    "today_processing_ms": "当日加工累计时间(ms)",
    "observed_power_on_at": "观测开机时间",
    "observed_power_off_at": "观测关机时间",
}

LATEST_VALUE_MAPPERS = {
    "machine_online": {"0": "关机", "1": "在线"},
    "emergency_state": {"0": "正常", "1": "急停"},
    "alarm_state": {"0": "正常", "1": "报警"},
    "controller_mode_text": {
        "MDI": "MDI",
        "Memory": "存储器",
        "Tape": "纸带",
        "Edit": "编辑",
        "Handle": "手轮",
        "JOG": "点动",
        "Teach": "示教",
        "Remote": "远程",
        "Offline": "离线",
    },
    "oee_status_text": {
        "Emergency": "急停",
        "Alarm": "报警",
        "Running": "加工",
        "Interrupted": "待机",
        "Offline": "关机",
    },
}


class CollectorGui:
    def __init__(self, root: tk.Tk, config_path: str):
        self.root = root
        self.config_path = Path(config_path).resolve()
        self.runtime: CollectorRuntime | None = None
        self.runtime_thread: threading.Thread | None = None
        self.last_exit_code: int | None = None
        self.local_timezone = datetime.now().astimezone().tzinfo
        self.report_refresh_counter = 0

        self.root.title("FANUC 机床数采程序")
        self.root.geometry("1280x860")
        self.root.minsize(1100, 760)

        self.status_var = tk.StringVar(value="已停止")
        self.report_date_var = tk.StringVar(value=datetime.now().date().isoformat())
        self.report_summary_var = tk.StringVar(value="请选择日期后点击“刷新统计”。")
        self.fields: dict[str, tk.StringVar] = {}

        self._build_ui()
        self._load_form_data()
        self._refresh_loop()
        self.root.protocol("WM_DELETE_WINDOW", self.on_close)

    def _build_ui(self) -> None:
        outer = ttk.Frame(self.root, padding=12)
        outer.pack(fill=tk.BOTH, expand=True)

        title = ttk.Label(outer, text="FANUC 机床数采程序", font=("Microsoft YaHei UI", 16, "bold"))
        title.pack(anchor="w")

        self.notebook = ttk.Notebook(outer)
        self.notebook.pack(fill=tk.BOTH, expand=True, pady=(10, 0))

        self.control_tab = ttk.Frame(self.notebook, padding=12)
        self.report_tab = ttk.Frame(self.notebook, padding=12)
        self.notebook.add(self.control_tab, text="采集控制")
        self.notebook.add(self.report_tab, text="时间统计")

        self._build_control_tab()
        self._build_report_tab()

    def _build_control_tab(self) -> None:
        frame = self.control_tab

        ttk.Label(frame, text="运行状态：").grid(row=0, column=0, sticky="w", pady=(0, 8))
        ttk.Label(frame, textvariable=self.status_var, foreground="#0b5").grid(
            row=0, column=1, columnspan=5, sticky="w", pady=(0, 8)
        )

        field_specs = [
            ("机床名称", "machine.name"),
            ("机床 IP", "machine.ip"),
            ("端口", "machine.port"),
            ("轮询间隔(ms)", "machine.poll_interval_ms"),
            ("快照间隔(ms)", "machine.snapshot_interval_ms"),
            ("加工模式编号", "machine.running_operation_modes"),
            ("FOCAS DLL 路径", "machine.focas_dll_path"),
            ("数据库路径", "storage.db_path"),
            ("日志路径", "runtime.log_path"),
        ]

        for row_index, (label_text, field_key) in enumerate(field_specs, start=1):
            ttk.Label(frame, text=label_text).grid(row=row_index, column=0, sticky="w", pady=4)
            var = tk.StringVar()
            self.fields[field_key] = var
            entry = ttk.Entry(frame, textvariable=var, width=84)
            entry.grid(row=row_index, column=1, columnspan=4, sticky="ew", padx=(8, 8), pady=4)
            if field_key == "machine.focas_dll_path":
                ttk.Button(frame, text="浏览", command=self.browse_dll).grid(row=row_index, column=5, sticky="ew")

        button_row = ttk.Frame(frame)
        button_row.grid(row=10, column=0, columnspan=6, sticky="ew", pady=(12, 10))

        ttk.Button(button_row, text="保存配置", command=self.save_config).pack(side=tk.LEFT, padx=(0, 8))
        ttk.Button(button_row, text="启动采集", command=self.start_collector).pack(side=tk.LEFT, padx=(0, 8))
        ttk.Button(button_row, text="停止采集", command=self.stop_collector).pack(side=tk.LEFT, padx=(0, 8))
        ttk.Button(button_row, text="刷新最新值", command=self.refresh_latest_values).pack(side=tk.LEFT, padx=(0, 8))
        ttk.Button(button_row, text="导出快照 CSV", command=self.export_csv).pack(side=tk.LEFT, padx=(0, 8))

        latest_frame = ttk.LabelFrame(frame, text="最新采集值", padding=8)
        latest_frame.grid(row=11, column=0, columnspan=3, sticky="nsew", padx=(0, 8))
        self.latest_text = tk.Text(latest_frame, height=22, width=62, wrap="none")
        self.latest_text.pack(fill=tk.BOTH, expand=True)

        log_frame = ttk.LabelFrame(frame, text="日志尾部", padding=8)
        log_frame.grid(row=11, column=3, columnspan=3, sticky="nsew")
        self.log_text = tk.Text(log_frame, height=22, width=62, wrap="none")
        self.log_text.pack(fill=tk.BOTH, expand=True)

        frame.columnconfigure(1, weight=1)
        frame.columnconfigure(2, weight=1)
        frame.columnconfigure(3, weight=1)
        frame.columnconfigure(4, weight=1)
        frame.rowconfigure(11, weight=1)

    def _build_report_tab(self) -> None:
        frame = self.report_tab

        filter_row = ttk.Frame(frame)
        filter_row.pack(fill=tk.X, pady=(0, 10))

        ttk.Label(filter_row, text="统计日期：").pack(side=tk.LEFT)
        ttk.Entry(filter_row, textvariable=self.report_date_var, width=16).pack(side=tk.LEFT, padx=(0, 8))
        ttk.Button(filter_row, text="刷新统计", command=self.refresh_timeline_report).pack(side=tk.LEFT, padx=(0, 8))
        ttk.Button(filter_row, text="今天", command=self.set_report_today).pack(side=tk.LEFT, padx=(0, 8))

        ttk.Label(
            frame,
            text="说明：统计页按时间段展示机床每天在做什么，状态包含加工、待机、报警、急停、关机。",
        ).pack(anchor="w", pady=(0, 8))

        ttk.Label(frame, textvariable=self.report_summary_var, foreground="#555").pack(anchor="w", pady=(0, 8))

        columns = ("status", "duration", "start_at", "end_at")
        self.report_tree = ttk.Treeview(frame, columns=columns, show="headings", height=24)
        self.report_tree.heading("status", text="状态")
        self.report_tree.heading("duration", text="时长")
        self.report_tree.heading("start_at", text="开始时间")
        self.report_tree.heading("end_at", text="截止时间")
        self.report_tree.column("status", width=120, anchor="center")
        self.report_tree.column("duration", width=160, anchor="center")
        self.report_tree.column("start_at", width=260, anchor="center")
        self.report_tree.column("end_at", width=260, anchor="center")

        scrollbar = ttk.Scrollbar(frame, orient="vertical", command=self.report_tree.yview)
        self.report_tree.configure(yscrollcommand=scrollbar.set)
        self.report_tree.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        scrollbar.pack(side=tk.RIGHT, fill=tk.Y)

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

        return {
            "machine": {
                "name": "fanuc-poc",
                "ip": "192.168.91.46",
                "port": 8193,
                "connect_timeout_sec": 10,
                "poll_interval_ms": 500,
                "snapshot_interval_ms": 5000,
                "running_operation_modes": [1, 2, 3],
                "focas_dll_path": "vendor/Fwlib32.dll",
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

    def save_config(self) -> None:
        payload = self._read_config_json()
        payload["machine"]["name"] = self.fields["machine.name"].get().strip()
        payload["machine"]["ip"] = self.fields["machine.ip"].get().strip()
        payload["machine"]["port"] = int(self.fields["machine.port"].get().strip())
        payload["machine"]["poll_interval_ms"] = int(self.fields["machine.poll_interval_ms"].get().strip())
        payload["machine"]["snapshot_interval_ms"] = int(self.fields["machine.snapshot_interval_ms"].get().strip())
        payload["machine"]["running_operation_modes"] = self._current_running_modes()
        payload["machine"]["focas_dll_path"] = self.fields["machine.focas_dll_path"].get().strip()
        payload["machine"]["mock_mode"] = False
        payload["storage"]["db_path"] = self.fields["storage.db_path"].get().strip()
        payload["runtime"]["log_path"] = self.fields["runtime.log_path"].get().strip()

        self.config_path.parent.mkdir(parents=True, exist_ok=True)
        self.config_path.write_text(json.dumps(payload, indent=2, ensure_ascii=False), encoding="utf-8")
        self.status_var.set(f"配置已保存：{self.config_path}")

    def browse_dll(self) -> None:
        selected = filedialog.askopenfilename(
            title="选择 FANUC FOCAS DLL",
            filetypes=[("DLL 文件", "*.dll"), ("全部文件", "*.*")],
        )
        if selected:
            self.fields["machine.focas_dll_path"].set(selected)

    def start_collector(self) -> None:
        if self.runtime_thread and self.runtime_thread.is_alive():
            messagebox.showinfo("提示", "采集程序已经在运行。")
            return

        try:
            self.save_config()
            self.runtime = CollectorRuntime(load_config(self.config_path))
        except Exception as exc:
            messagebox.showerror("配置错误", str(exc))
            return

        self.last_exit_code = None
        self.runtime_thread = threading.Thread(target=self._run_runtime, name="collector-runtime", daemon=True)
        self.runtime_thread.start()
        self.status_var.set("采集程序启动中...")

    def _run_runtime(self) -> None:
        assert self.runtime is not None
        self.last_exit_code = self.runtime.run()

    def stop_collector(self) -> None:
        if self.runtime is None:
            self.status_var.set("采集程序未运行。")
            return

        self.runtime.stop()
        self.status_var.set("已发送停止请求...")

    def refresh_latest_values(self) -> None:
        db_path = self._resolve_db_path()
        if not db_path.exists():
            self._set_text(self.latest_text, "数据库文件尚未生成。")
            return

        rows = read_latest_values(str(db_path))
        if not rows:
            self._set_text(self.latest_text, "暂无最新采集值。")
            return

        lines = []
        for key, value_text, updated_at in rows:
            display_key = LATEST_KEY_LABELS.get(key, key)
            display_value = LATEST_VALUE_MAPPERS.get(key, {}).get(value_text, value_text)
            lines.append(f"{display_key:20} {display_value:24} {self._format_timestamp_text(updated_at)}")
        self._set_text(self.latest_text, "\n".join(lines))

    def refresh_timeline_report(self, show_messages: bool = False) -> None:
        try:
            datetime.strptime(self.report_date_var.get().strip(), "%Y-%m-%d")
        except ValueError:
            if show_messages:
                messagebox.showwarning("日期格式错误", "统计日期必须是 YYYY-MM-DD，例如 2026-03-10。")
            self.report_summary_var.set("统计日期格式错误，请输入 YYYY-MM-DD。")
            self._clear_report_tree()
            return

        db_path = self._resolve_db_path()
        if not db_path.exists():
            self.report_summary_var.set("数据库文件尚未生成，无法统计。")
            self._clear_report_tree()
            return

        segments = read_daily_timeline(
            str(db_path),
            self.report_date_var.get().strip(),
            self._current_running_modes(),
            report_tz=self.local_timezone,
        )

        self._clear_report_tree()
        if not segments:
            self.report_summary_var.set("该日期暂无可用统计数据。")
            return

        summary_minutes: dict[str, int] = {}
        for segment in segments:
            status_name = STATUS_LABELS.get(segment.state_code, segment.state_code)
            duration_text = self._format_duration(segment.duration_ms)
            start_text = self._format_datetime(segment.start_at)
            end_text = self._format_datetime(segment.end_at)
            self.report_tree.insert("", tk.END, values=(status_name, duration_text, start_text, end_text))
            summary_minutes[status_name] = summary_minutes.get(status_name, 0) + segment.duration_ms

        summary_parts = [
            f"{status_name}{self._format_duration(duration_ms)}"
            for status_name, duration_ms in sorted(summary_minutes.items(), key=lambda item: item[0])
        ]
        self.report_summary_var.set(f"{self.report_date_var.get().strip()} 时间统计：{'，'.join(summary_parts)}")

    def set_report_today(self) -> None:
        self.report_date_var.set(datetime.now().date().isoformat())
        self.refresh_timeline_report(show_messages=False)

    def export_csv(self) -> None:
        db_path = self._resolve_db_path()
        if not db_path.exists():
            messagebox.showwarning("导出失败", "数据库文件尚未生成。")
            return

        default_output = db_path.with_suffix(".csv")
        output_path = filedialog.asksaveasfilename(
            title="导出快照 CSV",
            defaultextension=".csv",
            initialfile=default_output.name,
            initialdir=str(default_output.parent),
            filetypes=[("CSV 文件", "*.csv")],
        )
        if not output_path:
            return

        export_snapshots_to_csv(str(db_path), output_path)
        self.status_var.set(f"快照已导出：{output_path}")

    def _refresh_loop(self) -> None:
        if self.runtime_thread and self.runtime_thread.is_alive():
            self.status_var.set("采集运行中")
        elif self.last_exit_code is not None:
            self.status_var.set(f"采集已停止，退出码 {self.last_exit_code}")

        self.refresh_latest_values()
        self._refresh_log_tail()

        self.report_refresh_counter = (self.report_refresh_counter + 1) % 5
        if self.report_refresh_counter == 0:
            self.refresh_timeline_report(show_messages=False)

        self.root.after(1000, self._refresh_loop)

    def _refresh_log_tail(self) -> None:
        log_path = self._resolve_log_path()
        if not log_path.exists():
            self._set_text(self.log_text, "日志文件尚未生成。")
            return

        lines = log_path.read_text(encoding="utf-8", errors="ignore").splitlines()
        self._set_text(self.log_text, "\n".join(lines[-40:]))

    def _resolve_db_path(self) -> Path:
        db_path = Path(self.fields["storage.db_path"].get().strip())
        if db_path.is_absolute():
            return db_path
        return (self.config_path.parent.parent / db_path).resolve()

    def _resolve_log_path(self) -> Path:
        log_path = Path(self.fields["runtime.log_path"].get().strip())
        if log_path.is_absolute():
            return log_path
        return (self.config_path.parent.parent / log_path).resolve()

    def _current_running_modes(self) -> list[int]:
        return [int(item.strip()) for item in self.fields["machine.running_operation_modes"].get().split(",") if item.strip()]

    def _clear_report_tree(self) -> None:
        for item_id in self.report_tree.get_children():
            self.report_tree.delete(item_id)

    def _format_duration(self, duration_ms: int) -> str:
        total_seconds = max(0, duration_ms // 1000)
        hours, remainder = divmod(total_seconds, 3600)
        minutes, seconds = divmod(remainder, 60)

        parts = []
        if hours:
            parts.append(f"{hours}小时")
        if minutes:
            parts.append(f"{minutes}分钟")
        if seconds or not parts:
            parts.append(f"{seconds}秒")
        return "".join(parts)

    def _format_datetime(self, value: datetime) -> str:
        return value.astimezone(self.local_timezone).strftime("%Y-%m-%d %H:%M:%S")

    def _format_timestamp_text(self, value_text: str) -> str:
        try:
            return self._format_datetime(datetime.fromisoformat(value_text))
        except ValueError:
            return value_text

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
