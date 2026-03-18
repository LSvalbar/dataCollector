param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptRoot
$pythonExe = Join-Path -Path $projectRoot -ChildPath "runtime\python311-win32\python.exe"
$specPath = Join-Path -Path $projectRoot -ChildPath "packaging\dataCollector-gui.spec"
$distPath = Join-Path -Path $projectRoot -ChildPath "dist"
$buildPath = Join-Path -Path $projectRoot -ChildPath "build"
$vendorDll = Join-Path -Path $projectRoot -ChildPath "vendor\Fwlib32.dll"

if (-not (Test-Path $pythonExe)) {
    throw "Bundled x86 Python runtime not found: $pythonExe"
}

if (-not (Test-Path $specPath)) {
    throw "PyInstaller spec file not found: $specPath"
}

if (-not (Test-Path $vendorDll)) {
    throw "Required FOCAS DLL not found: $vendorDll"
}

Write-Host "Building portable dataCollector EXE..."
Write-Host "Python: $pythonExe"
Write-Host "Spec:   $specPath"
Write-Host ""

Set-Location $projectRoot
& $pythonExe -m PyInstaller --noconfirm --clean --distpath $distPath --workpath $buildPath $specPath

if ($LASTEXITCODE -ne 0) {
    throw "PyInstaller build failed with exit code $LASTEXITCODE"
}

Write-Host ""
Write-Host "Build completed."
Write-Host "Portable package: $(Join-Path -Path $distPath -ChildPath 'dataCollector')"
