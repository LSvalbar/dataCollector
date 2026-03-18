from __future__ import annotations

import argparse
import os
from pathlib import Path
import shutil
import sys
import tkinter as tk
from tkinter import messagebox

from src.fanuc_collector.gui import launch_gui


def application_root() -> Path:
    if getattr(sys, "frozen", False):
        return Path(sys.executable).resolve().parent
    return Path(__file__).resolve().parent


def bundled_resource_root() -> Path:
    if getattr(sys, "frozen", False):
        bundle_dir = getattr(sys, "_MEIPASS", "")
        if bundle_dir:
            return Path(bundle_dir).resolve()
    return application_root()


def ensure_runtime_layout(root: Path, resource_root: Path | None = None) -> Path:
    source_root = resource_root or root
    config_dir = root / "config"
    data_dir = root / "data"
    logs_dir = root / "logs"
    vendor_dir = root / "vendor"

    config_dir.mkdir(parents=True, exist_ok=True)
    data_dir.mkdir(parents=True, exist_ok=True)
    logs_dir.mkdir(parents=True, exist_ok=True)
    vendor_dir.mkdir(parents=True, exist_ok=True)

    config_path = config_dir / "machine.local.json"
    example_path = source_root / "config" / "machine.local.example.json"
    local_example_path = config_dir / "machine.local.example.json"

    if example_path.exists() and not local_example_path.exists():
        shutil.copy2(example_path, local_example_path)

    if not config_path.exists() and local_example_path.exists():
        shutil.copy2(local_example_path, config_path)

    bundled_vendor_dir = source_root / "vendor"
    if bundled_vendor_dir.exists():
        for source_path in bundled_vendor_dir.iterdir():
            if not source_path.is_file():
                continue
            target_path = vendor_dir / source_path.name
            if not target_path.exists() or source_path.stat().st_size != target_path.stat().st_size:
                shutil.copy2(source_path, target_path)

    return config_path


def show_startup_error(message: str) -> None:
    root = tk.Tk()
    root.withdraw()
    try:
        messagebox.showerror("dataCollector", message)
    finally:
        root.destroy()


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument("--prepare-only", action="store_true")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    root_dir = application_root()
    os.chdir(root_dir)
    config_path = ensure_runtime_layout(root_dir, bundled_resource_root())
    if args.prepare_only:
        return 0
    return launch_gui(str(config_path))


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except Exception as exc:  # pragma: no cover - startup safeguard for frozen app
        show_startup_error(f"Application startup failed:\n{exc}")
        raise SystemExit(1)
