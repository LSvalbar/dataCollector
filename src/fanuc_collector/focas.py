from __future__ import annotations

import ctypes
from ctypes import POINTER, byref, c_char, c_char_p, c_long, c_short, c_ubyte, c_ushort
import os
from pathlib import Path
import socket
import struct
import sys

from .models import MachineStatus, SystemInfo, utc_now
from .state_logic import spindle_override_percent


EW_OK = 0
ALARM_TYPE_ALL = -1
ALARM_INFORMATION2 = 1
TYPE_POWER_ON = 0
TYPE_OPERATING = 1
TYPE_CUTTING = 2
TYPE_CYCLE = 3
TYPE_FREE = 4
SP_ALL_TYPE = -1
PANEL_SIGNAL_ALL = -1

FOCAS_ERROR_DETAILS = {
    -17: ("EW_PROTOCOL", "协议错误"),
    -16: ("EW_SOCKET", "Windows Socket 错误"),
    -15: ("EW_NODLL", "FOCAS 运行时缺少配套 DLL"),
    -14: ("EW_INIERR", "FOCAS 初始化文件错误"),
    -13: ("EW_ITLOW", "智能终端低温报警"),
    -12: ("EW_ITHIGHT", "智能终端高温报警"),
    -11: ("EW_BUS", "总线错误"),
    -10: ("EW_SYSTEM2", "系统错误"),
    -9: ("EW_HSSB", "HSSB 通信错误"),
    -8: ("EW_HANDLE", "Windows 库句柄错误"),
    -7: ("EW_VERSION", "CNC/PMC 版本不匹配"),
    -6: ("EW_UNEXP", "异常错误"),
    -5: ("EW_SYSTEM", "系统错误"),
    -4: ("EW_PARITY", "共享内存奇偶校验错误"),
    -3: ("EW_MMCSYS", "EMM386 或 MMCSYS 安装错误"),
    -2: ("EW_RESET", "复位或停止"),
    -1: ("EW_BUSY", "设备忙"),
    0: ("EW_OK", "成功"),
    1: ("EW_FUNC", "命令准备错误"),
    2: ("EW_LENGTH", "数据块长度错误"),
    3: ("EW_NUMBER", "数据编号或地址范围错误"),
    4: ("EW_ATTRIB", "数据属性或类型错误"),
    5: ("EW_DATA", "数据错误"),
    6: ("EW_NOOPT", "控制器未开通该选件"),
    7: ("EW_PROT", "写保护"),
    8: ("EW_OVRFLOW", "内存溢出"),
    9: ("EW_PARAM", "CNC 参数不正确"),
    10: ("EW_BUFFER", "缓冲区错误"),
    11: ("EW_PATH", "路径错误"),
    12: ("EW_MODE", "机床模式不允许"),
    13: ("EW_REJECT", "执行被拒绝"),
    14: ("EW_DTSRVR", "Data Server 错误"),
    15: ("EW_ALARM", "机床存在报警"),
    16: ("EW_STOP", "CNC 未运行"),
    17: ("EW_PASSWD", "保护数据错误"),
    18: ("EW_PMC", "PMC 返回错误"),
    19: ("EW_PMCHANDLE", "PMC 句柄错误"),
    20: ("EW_RD_OVWSTP", "程序读取遇到 overwrite stop"),
    21: ("EW_RD_RSTFIN", "程序读取被 reset 中断"),
}

DEPENDENCY_DLL_NAMES = (
    "Fwlib32.dll",
    "fwlibe1.dll",
    "Fwlib0i.dll",
    "Fwlib0iB.dll",
    "fwlib0iD.dll",
    "fwlib0DN.dll",
    "fwlib30i.dll",
)


class FocasError(RuntimeError):
    pass


class FocasLibraryLoadError(FocasError):
    pass


class FocasCommunicationError(FocasError):
    pass


class ODBSYS(ctypes.Structure):
    _fields_ = [
        ("addinfo", c_short),
        ("max_axis", c_short),
        ("cnc_type", c_char * 2),
        ("mt_type", c_char * 2),
        ("series", c_char * 4),
        ("version", c_char * 4),
        ("axes", c_char * 2),
    ]


class ODBST(ctypes.Structure):
    _fields_ = [
        ("dummy", c_short),
        ("tmmode", c_short),
        ("aut", c_short),
        ("run", c_short),
        ("motion", c_short),
        ("mstb", c_short),
        ("emergency", c_short),
        ("alarm", c_short),
        ("edit", c_short),
    ]


class ODBPTIME_RECORD(ctypes.Structure):
    _fields_ = [
        ("prg_no", c_long),
        ("hour", c_short),
        ("minute", c_ubyte),
        ("second", c_ubyte),
    ]


class ODBPTIME(ctypes.Structure):
    _fields_ = [
        ("num", c_short),
        ("data", ODBPTIME_RECORD * 10),
    ]


class IODBTIME(ctypes.Structure):
    _fields_ = [
        ("minute", c_long),
        ("msec", c_long),
    ]


class ODBACT(ctypes.Structure):
    _fields_ = [
        ("dummy", c_short * 2),
        ("data", c_long),
    ]


class LOADELM(ctypes.Structure):
    _fields_ = [
        ("data", c_long),
        ("dec", c_short),
        ("unit", c_short),
        ("name", c_char),
        ("suff1", c_char),
        ("suff2", c_char),
        ("reserve", c_char),
    ]


class ODBSPLOAD(ctypes.Structure):
    _fields_ = [
        ("spload", LOADELM),
        ("spspeed", LOADELM),
    ]


class ODBSPN(ctypes.Structure):
    _fields_ = [
        ("datano", c_short),
        ("type", c_short),
        ("data", c_short * 8),
    ]


class ODBEXEPRG(ctypes.Structure):
    _fields_ = [
        ("name", c_char * 36),
        ("o_num", c_long),
    ]


class IODBSGNL(ctypes.Structure):
    _fields_ = [
        ("datano", c_short),
        ("type", c_short),
        ("mode", c_short),
        ("hndl_ax", c_short),
        ("hndl_mv", c_short),
        ("rpd_ovrd", c_short),
        ("jog_ovrd", c_short),
        ("feed_ovrd", c_short),
        ("spdl_ovrd", c_short),
        ("blck_del", c_short),
        ("sngl_blck", c_short),
        ("machn_lock", c_short),
        ("dry_run", c_short),
        ("mem_prtct", c_short),
        ("feed_hold", c_short),
        ("manual_rpd", c_short),
        ("dummy", c_short * 2),
    ]


class ALMINFO2_ENTRY(ctypes.Structure):
    _fields_ = [
        ("axis", c_short),
        ("alm_no", c_short),
        ("msg_len", c_short),
        ("alm_msg", c_char * 34),
    ]


class ALMINFO2_ALM2(ctypes.Structure):
    _fields_ = [
        ("alm", ALMINFO2_ENTRY * 5),
        ("data_end", c_short),
    ]


class ALMINFO2_UNION(ctypes.Union):
    _fields_ = [
        ("alm2", ALMINFO2_ALM2),
    ]


class ALMINFO2(ctypes.Structure):
    _fields_ = [
        ("u", ALMINFO2_UNION),
    ]


class ODBALMMSG2(ctypes.Structure):
    _fields_ = [
        ("alm_no", c_long),
        ("type", c_short),
        ("axis", c_short),
        ("dummy", c_short),
        ("msg_len", c_short),
        ("alm_msg", c_char * 64),
    ]


def focas_error_text(code: int) -> str:
    name, description = FOCAS_ERROR_DETAILS.get(code, (f"UNKNOWN_{code}", "未知错误"))
    return f"{name}({code}): {description}"


def _decode_ascii(raw_value) -> str:
    return bytes(raw_value).decode("ascii", errors="ignore").replace("\x00", "").strip()


def _decode_text(raw_value, length: int | None = None) -> str:
    data = bytes(raw_value)
    if length is not None and length >= 0:
        data = data[:length]
    return data.decode("ascii", errors="ignore").replace("\x00", "").strip()


def _scale_numeric_value(raw_value: int, decimal_places: int) -> int | float:
    scale = int(decimal_places)
    if scale <= 0:
        return int(raw_value)
    value = int(raw_value) / (10 ** scale)
    if float(value).is_integer():
        return int(value)
    return round(value, min(scale, 3))


def controller_mode_text(mode: int) -> str:
    mapping = {
        0: "MDI",
        1: "Memory",
        2: "Tape",
        3: "Edit",
        4: "Handle",
        5: "JOG",
        6: "Teach",
        7: "Remote",
    }
    return mapping.get(mode, f"Unknown({mode})")


def derive_oee_status(
    operation_mode: int,
    alarm_state: int,
    emergency_state: int,
    running_modes: set[int],
) -> tuple[int, str]:
    if emergency_state:
        return 1, "Emergency"
    if alarm_state:
        return 2, "Alarm"
    if operation_mode in running_modes:
        return 3, "Running"
    return 4, "Interrupted"


class FocasClient:
    def __init__(self, dll_path: str, ip: str, port: int, timeout_sec: int, running_modes: list[int]):
        self._dll_path = Path(dll_path)
        self._ip = ip
        self._port = port
        self._timeout_sec = timeout_sec
        self._running_modes = set(running_modes)
        self._lib = None
        self._handle = c_ushort(0)
        self._dll_directory = None

    def connect(self) -> None:
        if not self._dll_path.exists():
            raise FocasLibraryLoadError(f"FOCAS DLL not found: {self._dll_path}")

        self._dll_directory = os.add_dll_directory(str(self._dll_path.parent))

        try:
            self._lib = ctypes.WinDLL(str(self._dll_path))
        except OSError as exc:
            raise FocasLibraryLoadError(
                "Failed to load FOCAS DLL: "
                f"{self._dll_path}. "
                f"python={sys.executable}, bits={struct.calcsize('P') * 8}. "
                f"Windows loader error: {exc}"
            ) from exc

        self._lib.cnc_allclibhndl3.argtypes = [c_char_p, c_ushort, c_long, POINTER(c_ushort)]
        self._lib.cnc_allclibhndl3.restype = c_short
        self._lib.cnc_freelibhndl.argtypes = [c_ushort]
        self._lib.cnc_freelibhndl.restype = c_short
        self._lib.cnc_sysinfo.argtypes = [c_ushort, POINTER(ODBSYS)]
        self._lib.cnc_sysinfo.restype = c_short
        self._lib.cnc_statinfo.argtypes = [c_ushort, POINTER(ODBST)]
        self._lib.cnc_statinfo.restype = c_short
        self._lib.cnc_acts.argtypes = [c_ushort, POINTER(ODBACT)]
        self._lib.cnc_acts.restype = c_short
        self._lib.cnc_rdspmeter.argtypes = [c_ushort, c_short, POINTER(c_short), POINTER(ODBSPLOAD)]
        self._lib.cnc_rdspmeter.restype = c_short
        self._lib.cnc_rdspload.argtypes = [c_ushort, c_short, POINTER(ODBSPN)]
        self._lib.cnc_rdspload.restype = c_short
        self._lib.cnc_exeprgname.argtypes = [c_ushort, POINTER(ODBEXEPRG)]
        self._lib.cnc_exeprgname.restype = c_short
        self._lib.cnc_rdopnlsgnl.argtypes = [c_ushort, c_short, POINTER(IODBSGNL)]
        self._lib.cnc_rdopnlsgnl.restype = c_short
        self._lib.cnc_rdtimer.argtypes = [c_ushort, c_short, POINTER(IODBTIME)]
        self._lib.cnc_rdtimer.restype = c_short
        self._lib.cnc_rdproctime.argtypes = [c_ushort, POINTER(ODBPTIME)]
        self._lib.cnc_rdproctime.restype = c_short
        self._lib.cnc_rdalminfo2.argtypes = [c_ushort, c_short, c_short, c_short, POINTER(ALMINFO2)]
        self._lib.cnc_rdalminfo2.restype = c_short
        self._lib.cnc_rdalmmsg2.argtypes = [c_ushort, c_short, POINTER(c_short), POINTER(ODBALMMSG2)]
        self._lib.cnc_rdalmmsg2.restype = c_short

        result = self._lib.cnc_allclibhndl3(
            self._ip.encode("ascii"),
            self._port,
            self._timeout_sec,
            byref(self._handle),
        )
        if result != EW_OK:
            raise FocasCommunicationError(self._format_connect_error(result))

    def disconnect(self) -> None:
        if self._lib is None or self._handle.value == 0:
            if self._dll_directory is not None:
                self._dll_directory.close()
                self._dll_directory = None
            return
        self._lib.cnc_freelibhndl(self._handle)
        self._handle = c_ushort(0)
        if self._dll_directory is not None:
            self._dll_directory.close()
            self._dll_directory = None

    def read_system_info(self) -> SystemInfo:
        self._ensure_connected()
        buffer = ODBSYS()
        result = self._lib.cnc_sysinfo(self._handle, byref(buffer))
        if result != EW_OK:
            raise FocasCommunicationError(f"cnc_sysinfo failed with code {result}")

        axis_text = _decode_ascii(buffer.axes) or "0"

        return SystemInfo(
            max_axis_number=int(buffer.max_axis),
            cnc_type=_decode_ascii(buffer.cnc_type),
            machine_type=_decode_ascii(buffer.mt_type),
            series_number=_decode_ascii(buffer.series),
            version_number=_decode_ascii(buffer.version),
            axis_count=int(axis_text),
            additional_info=int(buffer.addinfo),
        )

    def read_status(self) -> MachineStatus:
        self._ensure_connected()
        buffer = ODBST()
        result = self._lib.cnc_statinfo(self._handle, byref(buffer))
        if result != EW_OK:
            raise FocasCommunicationError(f"cnc_statinfo failed with code {result}")

        timer_totals = self.read_timer_totals()

        automatic_mode = int(buffer.aut)
        operation_mode = int(buffer.run)
        emergency_state = int(buffer.emergency)
        alarm_state = int(buffer.alarm)
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
            native_power_on_total_ms=timer_totals.get("power_on_total_ms"),
            native_operating_total_ms=timer_totals.get("operating_total_ms"),
            native_cutting_total_ms=timer_totals.get("cutting_total_ms"),
            native_cycle_total_ms=timer_totals.get("cycle_total_ms"),
            native_free_total_ms=timer_totals.get("free_total_ms"),
            raw_payload={
                "automatic_mode": automatic_mode,
                "operation_mode": operation_mode,
                "emergency_state": emergency_state,
                "alarm_state": alarm_state,
                "controller_mode_number": controller_mode_number,
                "oee_status_number": oee_number,
                **timer_totals,
            },
        )

    def read_timer_totals(self) -> dict[str, int | str]:
        self._ensure_connected()
        timer_map = {
            "power_on_total_ms": TYPE_POWER_ON,
            "operating_total_ms": TYPE_OPERATING,
            "cutting_total_ms": TYPE_CUTTING,
            "cycle_total_ms": TYPE_CYCLE,
            "free_total_ms": TYPE_FREE,
        }
        values: dict[str, int | str] = {}
        errors: list[str] = []

        for key, timer_type in timer_map.items():
            timer_value, error_code = self._try_read_timer(timer_type)
            if timer_value is not None:
                values[key] = timer_value
            elif error_code is not None:
                errors.append(f"{key}:{focas_error_text(error_code)}")

        if errors:
            values["native_timer_errors"] = " | ".join(errors)

        return values

    def read_spindle_metrics(self) -> dict[str, int | float]:
        self._ensure_connected()
        values: dict[str, int | float] = {}

        spindle_speed = self._read_actual_spindle_speed()
        if spindle_speed is not None:
            values["spindle_speed_rpm"] = spindle_speed

        spindle_load, spindle_meter_speed = self._read_spindle_meter()
        if spindle_load is None:
            spindle_load = self._read_spindle_load_fallback()
        if spindle_load is not None:
            values["spindle_load_percent"] = spindle_load
        if spindle_speed is None and spindle_meter_speed is not None:
            values["spindle_speed_rpm"] = spindle_meter_speed

        spindle_override_raw = self._read_spindle_override_signal()
        if spindle_override_raw is not None:
            values["spindle_override_signal"] = spindle_override_raw
            spindle_override_value = spindle_override_percent(spindle_override_raw)
            if spindle_override_value is not None:
                values["spindle_override_percent"] = spindle_override_value

        return values

    def read_current_program(self) -> dict[str, int | str]:
        self._ensure_connected()
        buffer = ODBEXEPRG()
        result = self._lib.cnc_exeprgname(self._handle, byref(buffer))
        if result != EW_OK:
            return {}

        program_name = _decode_text(buffer.name)
        program_number = int(buffer.o_num)
        if program_number == 0 and not program_name:
            return {}

        return {
            "current_program_number": program_number,
            "current_program_name": program_name,
        }

    def read_processing_time_records(self) -> list[dict[str, int]]:
        self._ensure_connected()
        buffer = ODBPTIME()
        result = self._lib.cnc_rdproctime(self._handle, byref(buffer))
        if result != EW_OK:
            return []

        record_count = max(0, min(int(buffer.num), len(buffer.data)))
        records: list[dict[str, int]] = []
        for index in range(record_count):
            record = buffer.data[index]
            if int(record.prg_no) == 0 and int(record.hour) == 0 and int(record.minute) == 0 and int(record.second) == 0:
                continue
            records.append(
                {
                    "program_number": int(record.prg_no),
                    "hour": int(record.hour),
                    "minute": int(record.minute),
                    "second": int(record.second),
                    "duration_ms": (
                        int(record.hour) * 3600 + int(record.minute) * 60 + int(record.second)
                    )
                    * 1000,
                }
            )
        return records

    def read_alarm_details(self) -> list[dict[str, int | str]]:
        self._ensure_connected()
        details = self._read_alarm_messages()
        if details:
            return details
        return self._read_alarm_info()

    def _ensure_connected(self) -> None:
        if self._lib is None or self._handle.value == 0:
            raise FocasCommunicationError("FOCAS client is not connected")

    def _try_read_timer(self, timer_type: int) -> tuple[int | None, int | None]:
        buffer = IODBTIME()
        result = self._lib.cnc_rdtimer(self._handle, timer_type, byref(buffer))
        if result != EW_OK:
            return None, int(result)
        return int(buffer.minute) * 60_000 + int(buffer.msec), None

    def _read_actual_spindle_speed(self) -> int | None:
        buffer = ODBACT()
        result = self._lib.cnc_acts(self._handle, byref(buffer))
        if result != EW_OK:
            return None
        return max(0, int(buffer.data))

    def _read_spindle_meter(self) -> tuple[float | int | None, float | int | None]:
        data_count = c_short(1)
        buffer = ODBSPLOAD()
        result = self._lib.cnc_rdspmeter(self._handle, SP_ALL_TYPE, byref(data_count), byref(buffer))
        if result != EW_OK:
            return None, None
        spindle_load = _scale_numeric_value(buffer.spload.data, buffer.spload.dec)
        spindle_speed = _scale_numeric_value(buffer.spspeed.data, buffer.spspeed.dec)
        return spindle_load, spindle_speed

    def _read_spindle_load_fallback(self) -> int | None:
        buffer = ODBSPN()
        result = self._lib.cnc_rdspload(self._handle, 1, byref(buffer))
        if result != EW_OK:
            return None
        try:
            return max(0, int(buffer.data[0]))
        except (IndexError, TypeError):
            return None

    def _read_spindle_override_signal(self) -> int | None:
        buffer = IODBSGNL()
        result = self._lib.cnc_rdopnlsgnl(self._handle, PANEL_SIGNAL_ALL, byref(buffer))
        if result != EW_OK:
            return None
        return int(buffer.spdl_ovrd)

    def _read_alarm_messages(self) -> list[dict[str, int | str]]:
        max_alarm_count = 10
        read_count = c_short(max_alarm_count)
        buffer = (ODBALMMSG2 * max_alarm_count)()
        result = self._lib.cnc_rdalmmsg2(self._handle, ALARM_TYPE_ALL, byref(read_count), buffer)
        if result != EW_OK:
            return []

        details: list[dict[str, int | str]] = []
        for index in range(max(0, min(int(read_count.value), max_alarm_count))):
            item = buffer[index]
            message_text = _decode_text(item.alm_msg, int(item.msg_len))
            if int(item.alm_no) == 0 and not message_text:
                continue
            details.append(
                {
                    "alarm_number": int(item.alm_no),
                    "alarm_type": int(item.type),
                    "axis": int(item.axis),
                    "message": message_text,
                }
            )
        return details

    def _read_alarm_info(self) -> list[dict[str, int | str]]:
        buffer = ALMINFO2()
        result = self._lib.cnc_rdalminfo2(self._handle, ALARM_INFORMATION2, ALARM_TYPE_ALL, 0, byref(buffer))
        if result != EW_OK:
            return []

        details: list[dict[str, int | str]] = []
        for item in buffer.u.alm2.alm:
            message_text = _decode_text(item.alm_msg, int(item.msg_len))
            if int(item.alm_no) == 0 and not message_text:
                continue
            details.append(
                {
                    "alarm_number": int(item.alm_no),
                    "axis": int(item.axis),
                    "message": message_text,
                }
            )
        return details

    def _format_connect_error(self, code: int) -> str:
        context = [
            f"function=cnc_allclibhndl3",
            f"error={focas_error_text(code)}",
            f"target={self._ip}:{self._port}",
            f"timeout_sec={self._timeout_sec}",
            f"dll_path={self._dll_path}",
            f"python={sys.executable}",
            f"python_bits={struct.calcsize('P') * 8}",
            f"cwd={Path.cwd()}",
            f"tcp_probe={self._probe_tcp_port()}",
        ]

        if code == -15:
            context.append(f"dependency_check={self._dependency_check_summary()}")

        return " | ".join(context)

    def _probe_tcp_port(self) -> str:
        try:
            with socket.create_connection((self._ip, self._port), timeout=min(max(self._timeout_sec, 1), 3)):
                return "ok"
        except OSError as exc:
            return f"failed({exc})"

    def _dependency_check_summary(self) -> str:
        details: list[str] = []
        dll_dir = self._dll_path.parent
        details.append(f"dll_dir_exists={dll_dir.exists()}")

        for dll_name in DEPENDENCY_DLL_NAMES:
            dll_file = dll_dir / dll_name
            if not dll_file.exists():
                details.append(f"{dll_name}=missing")
                continue

            try:
                ctypes.WinDLL(str(dll_file))
            except OSError as exc:
                details.append(f"{dll_name}=load_failed({exc})")
            else:
                details.append(f"{dll_name}=ok")

        return ", ".join(details)
