param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$enterpriseRoot = Split-Path -Parent $scriptRoot
$outputRoot = Join-Path $enterpriseRoot "dist\$Runtime"
$activeOutputRoot = $outputRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "dotnet SDK was not found on this machine." -ForegroundColor Yellow
    Write-Host "Publish-Win10Portable.ps1 must run on a build machine, not on a runtime-only Win10 machine." -ForegroundColor Yellow
    Write-Host "Run this script on the development machine, then copy dist\$Runtime to the Win10 test machine." -ForegroundColor Yellow
    exit 1
}

Write-Host "Publishing enterprise package to $outputRoot" -ForegroundColor Cyan

if (Test-Path $outputRoot) {
    try {
        Remove-Item -Recurse -Force $outputRoot
    }
    catch {
        $suffix = Get-Date -Format "yyyyMMddHHmmss"
        $activeOutputRoot = Join-Path $enterpriseRoot "dist\$Runtime-$suffix"
        Write-Host "Default output folder is locked. Publishing to $activeOutputRoot instead." -ForegroundColor Yellow
    }
}

New-Item -ItemType Directory -Force -Path $activeOutputRoot | Out-Null

$publishTargets = @(
    @{
        Name = "launcher"
        Runtime = $Runtime
        Project = Join-Path $enterpriseRoot "src\DataCollector.Launcher.Wpf\DataCollector.Launcher.Wpf.csproj"
        Output = $activeOutputRoot
        SingleFile = $true
        ReadyToRun = $false
    },
    @{
        Name = "server"
        Runtime = $Runtime
        Project = Join-Path $enterpriseRoot "src\DataCollector.Server.Api\DataCollector.Server.Api.csproj"
        Output = Join-Path $activeOutputRoot "server"
        SingleFile = $false
        ReadyToRun = $true
    },
    @{
        Name = "client"
        Runtime = $Runtime
        Project = Join-Path $enterpriseRoot "src\DataCollector.Desktop.Wpf\DataCollector.Desktop.Wpf.csproj"
        Output = Join-Path $activeOutputRoot "client"
        SingleFile = $false
        ReadyToRun = $true
    },
    @{
        Name = "agent"
        Runtime = "win-x86"
        Project = Join-Path $enterpriseRoot "src\DataCollector.Agent.Worker\DataCollector.Agent.Worker.csproj"
        Output = Join-Path $activeOutputRoot "agent"
        SingleFile = $false
        ReadyToRun = $true
    }
)

foreach ($target in $publishTargets) {
    dotnet publish $target.Project `
        -c $Configuration `
        -r $target.Runtime `
        --self-contained true `
        -p:PublishSingleFile=$($target.SingleFile.ToString().ToLowerInvariant()) `
        -p:PublishReadyToRun=$($target.ReadyToRun.ToString().ToLowerInvariant()) `
        -p:DebugSymbols=false `
        -p:DebugType=None `
        -o $target.Output
}

$serverBat = @"
@echo off
setlocal
cd /d "%~dp0server"
start "DataCollector Server" "%~dp0server\DataCollector.Server.Api.exe"
"@

$clientBat = @"
@echo off
setlocal
cd /d "%~dp0client"
set DATACOLLECTOR_API_URL=http://localhost:5180
start "DataCollector Client" "%~dp0client\DataCollector.Desktop.Wpf.exe"
"@

$agentBat = @"
@echo off
setlocal
cd /d "%~dp0agent"
start "DataCollector Agent" "%~dp0agent\DataCollector.Agent.Worker.exe"
"@

$allBat = @"
@echo off
setlocal
cd /d "%~dp0server"
start "DataCollector Server" "%~dp0server\DataCollector.Server.Api.exe"
timeout /t 3 /nobreak >nul
cd /d "%~dp0client"
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
   DataCollectorLauncher.exe

   If you still want the old batch-based way, Start-Enterprise-All.bat is also kept as a fallback.

3. To run components separately:
   Start-EnterpriseServer.bat
   Start-EnterpriseClient.bat
   Start-EnterpriseAgent.bat

4. Publish-Win10Portable.ps1 is a packaging script, not a runtime script.
   If the Win10 machine does not have dotnet, do not run this script there.
   Run it on the development machine and copy dist\$Runtime afterward.

5. The server uses SQLite persistence by default.
   The database file will be created automatically under:
   server\data\enterprise.db

6. The current enterprise build starts with an empty device list.
   Add departments, workshops and machines from the client before testing live collection.

7. Agent setup basics:
   - Edit agent\appsettings.json
   - ServerBaseUrl should point to the enterprise server, normally:
     http://localhost:5180
   - AgentNodeName must match the device's Agent node in the client
   - Machine lists are now pulled automatically from the server
   - You no longer need to maintain Machines[] manually for normal usage

8. If the CNC can be pinged but the enterprise client still shows offline, check:
   - Start-EnterpriseAgent.bat or DataCollectorLauncher.exe was actually started
   - AgentNodeName matches the device archive
   - the device is enabled in the client
   - the device really belongs to this Agent node
   - the agent log reports no node mismatch or unknown device codes
"@

Set-Content -Path (Join-Path $activeOutputRoot "Start-EnterpriseServer.bat") -Value $serverBat -Encoding Ascii
Set-Content -Path (Join-Path $activeOutputRoot "Start-EnterpriseClient.bat") -Value $clientBat -Encoding Ascii
Set-Content -Path (Join-Path $activeOutputRoot "Start-EnterpriseAgent.bat") -Value $agentBat -Encoding Ascii
Set-Content -Path (Join-Path $activeOutputRoot "Start-Enterprise-All.bat") -Value $allBat -Encoding Ascii
Set-Content -Path (Join-Path $activeOutputRoot "README-Win10.txt") -Value $runbook -Encoding Ascii

Write-Host "Portable package ready: $activeOutputRoot" -ForegroundColor Green
