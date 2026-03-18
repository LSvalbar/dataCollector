param(
    [string]$ConfigPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptRoot
$pythonExe = Join-Path -Path $projectRoot -ChildPath "runtime\python311-win32\python.exe"
$exampleConfig = Join-Path -Path $projectRoot -ChildPath "config\machine.local.example.json"

if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path -Path $projectRoot -ChildPath "config\machine.local.json"
}

$resolvedConfig = [System.IO.Path]::GetFullPath($ConfigPath)

if (-not (Test-Path $pythonExe)) {
    throw "Bundled 32-bit Python not found: $pythonExe. Copy the runtime folder together with the project."
}

if (-not (Test-Path $resolvedConfig)) {
    if (Test-Path $exampleConfig) {
        Copy-Item -Path $exampleConfig -Destination $resolvedConfig -Force
    }
    else {
        throw "Config not found and no example template available: $resolvedConfig"
    }
}

Write-Host "Starting FANUC collector..."
Write-Host "Config: $resolvedConfig"
Write-Host "Python: $pythonExe"
Write-Host ""

Set-Location $projectRoot
& $pythonExe run_collector.py run --config $resolvedConfig
