param(
    [string]$ConfigPath = "D:\Project\Codex\DataCollector\config\machine.192.168.91.46.json"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = "D:\Project\Codex\DataCollector"
$pythonExe = "python"
$resolvedConfig = [System.IO.Path]::GetFullPath($ConfigPath)
$dllPath = Join-Path -Path $projectRoot -ChildPath "vendor\fwlib64.dll"

if (-not (Test-Path $resolvedConfig)) {
    throw "Config not found: $resolvedConfig"
}

if (-not (Test-Path $dllPath)) {
    throw "FOCAS DLL not found: $dllPath"
}

Write-Host "Starting FANUC collector..."
Write-Host "Config: $resolvedConfig"
Write-Host "DLL:    $dllPath"
Write-Host ""

Set-Location $projectRoot
& $pythonExe run_collector.py run --config $resolvedConfig
