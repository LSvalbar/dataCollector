param(
    [string]$ConfigPath = "D:\Project\Codex\DataCollector\config\machine.192.168.91.46.json"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = "D:\Project\Codex\DataCollector"
$pythonExe = "python"
$resolvedConfig = [System.IO.Path]::GetFullPath($ConfigPath)

if (-not (Test-Path $resolvedConfig)) {
    throw "Config not found: $resolvedConfig"
}

Write-Host "Starting FANUC collector GUI..."
Write-Host "Config: $resolvedConfig"
Write-Host ""

Set-Location $projectRoot
& $pythonExe run_collector.py gui --config $resolvedConfig
