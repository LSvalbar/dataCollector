from __future__ import annotations

import ctypes
from ctypes import POINTER, byref, c_char, c_char_p, c_long, c_short, c_ushort
import os
from pathlib import Path

from .models import MachineStatus, SystemInfo, utc_now


EW_OK = 0


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
                f"Failed to load FOCAS DLL: {self._dll_path}. Check 32-bit vs 64-bit compatibility."
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
            raise FocasCommunicationError(f"cnc_allclibhndl3 failed with code {result}")

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
