# -*- mode: python ; coding: utf-8 -*-

from pathlib import Path


project_root = Path(SPECPATH).resolve().parent
vendor_dir = project_root / "vendor"
config_dir = project_root / "config"

vendor_paths = {path.resolve() for path in vendor_dir.glob("*.dll")}
vendor_paths.update(path.resolve() for path in vendor_dir.glob("*.DLL"))
vendor_binaries = [(str(path), "vendor") for path in sorted(vendor_paths)]

datas = []
example_config = config_dir / "machine.local.example.json"
vendor_readme = vendor_dir / "README.txt"

if example_config.exists():
    datas.append((str(example_config), "config"))
if vendor_readme.exists():
    datas.append((str(vendor_readme), "vendor"))


a = Analysis(
    [str(project_root / "app_gui.py")],
    pathex=[str(project_root)],
    binaries=vendor_binaries,
    datas=datas,
    hiddenimports=[],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    noarchive=False,
    optimize=0,
)
pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    [],
    exclude_binaries=True,
    name="dataCollector",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    console=False,
    disable_windowed_traceback=False,
)

coll = COLLECT(
    exe,
    a.binaries,
    a.zipfiles,
    a.datas,
    strip=False,
    upx=True,
    upx_exclude=[],
    name="dataCollector",
    contents_directory=".",
)
