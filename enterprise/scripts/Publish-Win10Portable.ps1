param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$enterpriseRoot = Split-Path -Parent $scriptRoot
$outputRoot = Join-Path $enterpriseRoot "dist\$Runtime"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "dotnet SDK was not found on this machine." -ForegroundColor Yellow
    Write-Host "Publish-Win10Portable.ps1 must run on a build machine, not on a runtime-only Win10 machine." -ForegroundColor Yellow
    Write-Host "Run this script on the development machine, then copy dist\$Runtime to the Win10 test machine." -ForegroundColor Yellow
    exit 1
}

Write-Host "Publishing enterprise package to $outputRoot" -ForegroundColor Cyan

if (Test-Path $outputRoot) {
    Remove-Item -Recurse -Force $outputRoot
}

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

$publishTargets = @(
    @{
        Name = "server"
        Project = Join-Path $enterpriseRoot "src\DataCollector.Server.Api\DataCollector.Server.Api.csproj"
    },
    @{
        Name = "client"
        Project = Join-Path $enterpriseRoot "src\DataCollector.Desktop.Wpf\DataCollector.Desktop.Wpf.csproj"
    },
    @{
        Name = "agent"
        Project = Join-Path $enterpriseRoot "src\DataCollector.Agent.Worker\DataCollector.Agent.Worker.csproj"
    }
)

foreach ($target in $publishTargets) {
    $targetOutput = Join-Path $outputRoot $target.Name
    dotnet publish $target.Project `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:PublishReadyToRun=true `
        -p:DebugSymbols=false `
        -p:DebugType=None `
        -o $targetOutput
}

$serverBat = @"
@echo off
setlocal
cd /d "%~dp0"
start "DataCollector Server" "%~dp0server\DataCollector.Server.Api.exe"
"@

$clientBat = @"
@echo off
setlocal
cd /d "%~dp0"
set DATACOLLECTOR_API_URL=http://localhost:5180
start "DataCollector Client" "%~dp0client\DataCollector.Desktop.Wpf.exe"
"@

$agentBat = @"
@echo off
setlocal
cd /d "%~dp0"
start "DataCollector Agent" "%~dp0agent\DataCollector.Agent.Worker.exe"
"@

$allBat = @"
@echo off
setlocal
cd /d "%~dp0"
start "DataCollector Server" "%~dp0server\DataCollector.Server.Api.exe"
timeout /t 3 /nobreak >nul
set DATACOLLECTOR_API_URL=http://localhost:5180
start "DataCollector Client" "%~dp0client\DataCollector.Desktop.Wpf.exe"
"@

$runbook = @"
DataCollector Enterprise Win10 Runtime Notes
===========================================

1. Copy the entire dist\$Runtime folder to the Win10 test machine.
   Example:
   D:\source\dataCollector\enterprise\dist\$Runtime

2. To view the enterprise UI, double-click:
   Start-Enterprise-All.bat

3. To run components separately:
   Start-EnterpriseServer.bat
   Start-EnterpriseClient.bat
   Start-EnterpriseAgent.bat

4. Publish-Win10Portable.ps1 is a packaging script, not a runtime script.
   If the Win10 machine does not have dotnet, do not run this script there.
   Run it on the development machine and copy dist\$Runtime afterward.

5. The current enterprise build uses seeded demo data for architecture and UI review.
   Real FANUC machine collection still requires the next step: C# Agent + FOCAS + production database.
"@

Set-Content -Path (Join-Path $outputRoot "Start-EnterpriseServer.bat") -Value $serverBat -Encoding Ascii
Set-Content -Path (Join-Path $outputRoot "Start-EnterpriseClient.bat") -Value $clientBat -Encoding Ascii
Set-Content -Path (Join-Path $outputRoot "Start-EnterpriseAgent.bat") -Value $agentBat -Encoding Ascii
Set-Content -Path (Join-Path $outputRoot "Start-Enterprise-All.bat") -Value $allBat -Encoding Ascii
Set-Content -Path (Join-Path $outputRoot "README-Win10.txt") -Value $runbook -Encoding Ascii

Write-Host "Portable package ready: $outputRoot" -ForegroundColor Green
