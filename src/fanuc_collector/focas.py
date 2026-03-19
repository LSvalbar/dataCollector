from __future__ import annotations

import ctypes
from ctypes import POINTER, byref, c_char, c_char_p, c_long, c_short, c_ushort
import os
from pathlib import Path
import socket
import struct
import sys

from .models import MachineStatus, SystemInfo, utc_now


EW_OK = 0

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


def focas_error_text(code: int) -> str:
    name, description = FOCAS_ERROR_DETAILS.get(code, (f"UNKNOWN_{code}", "未知错误"))
    return f"{name}({code}): {description}"


def _decode_ascii(raw_value) -> str:
    return bytes(raw_value).decode("ascii", errors="ignore").replace("\x00", "").strip()


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
        if self._lib is None or self._handle.value == 0:
            raise FocasCommunicationError("FOCAS client is not connected")

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
