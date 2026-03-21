param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$enterpriseRoot = Split-Path -Parent $scriptRoot
$outputRoot = Join-Path $enterpriseRoot "dist\$Runtime"

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
DataCollector Enterprise Win10 测试运行说明
=========================================

1. 将整个 dist\$Runtime 目录复制到 Win10 测试机，例如：
   D:\source\dataCollector\enterprise\dist\$Runtime

2. 如果只是查看正式版界面效果，直接双击：
   Start-Enterprise-All.bat

3. 如果只想单独启动某个组件：
   Start-EnterpriseServer.bat
   Start-EnterpriseClient.bat
   Start-EnterpriseAgent.bat

4. 当前正式版 enterprise 使用的是演示种子数据，用于评审架构、界面、公式和部署方式。
   真实 FANUC 机床采集仍要在下一阶段把 C# Agent 接入 FOCAS 驱动和正式数据库。

5. 当前目录下三个子目录职责：
   server  - 中央服务端 API
   client  - 正式客户端
   agent   - 车间 Agent 骨架
"@

Set-Content -Path (Join-Path $outputRoot "Start-EnterpriseServer.bat") -Value $serverBat -Encoding Ascii
Set-Content -Path (Join-Path $outputRoot "Start-EnterpriseClient.bat") -Value $clientBat -Encoding Ascii
Set-Content -Path (Join-Path $outputRoot "Start-EnterpriseAgent.bat") -Value $agentBat -Encoding Ascii
Set-Content -Path (Join-Path $outputRoot "Start-Enterprise-All.bat") -Value $allBat -Encoding Ascii
Set-Content -Path (Join-Path $outputRoot "README-Win10-运行说明.txt") -Value $runbook -Encoding UTF8

Write-Host "Portable package ready: $outputRoot" -ForegroundColor Green
