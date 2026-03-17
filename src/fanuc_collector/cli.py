from __future__ import annotations

import argparse
from pathlib import Path

from .config import load_config
from .runtime import CollectorRuntime
from .storage import export_snapshots_to_csv, read_latest_values


def main() -> int:
    parser = argparse.ArgumentParser(description="FANUC single machine collector")
    subparsers = parser.add_subparsers(dest="command", required=True)

    run_parser = subparsers.add_parser("run", help="Run collector")
    run_parser.add_argument("--config", required=True, help="Path to JSON config")

    latest_parser = subparsers.add_parser("show-latest", help="Show latest values from SQLite")
    latest_parser.add_argument("--db", required=True, help="Path to SQLite database")

    export_parser = subparsers.add_parser("export-snapshots", help="Export snapshots to CSV")
    export_parser.add_argument("--db", required=True, help="Path to SQLite database")
    export_parser.add_argument("--out", required=True, help="Path to CSV output")

    args = parser.parse_args()

    if args.command == "run":
        runtime = CollectorRuntime(load_config(args.config))
        return runtime.run()

    if args.command == "show-latest":
        rows = read_latest_values(args.db)
        for key, value_text, updated_at in rows:
            print(f"{key:24} {value_text:16} {updated_at}")
        return 0

    if args.command == "export-snapshots":
        export_snapshots_to_csv(args.db, args.out)
        print(f"Snapshots exported to {Path(args.out).resolve()}")
        return 0

    return 1
