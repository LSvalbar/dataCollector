param(
    [Parameter(Mandatory = $true)]
    [string]$IpAddress,

    [int[]]$Ports = @(8193),

    [int]$PingCount = 4,

    [int]$TcpTimeoutMs = 2000,

    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Test-TcpPort {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TargetHost,

        [Parameter(Mandatory = $true)]
        [int]$Port,

        [Parameter(Mandatory = $true)]
        [int]$TimeoutMs
    )

    $client = New-Object System.Net.Sockets.TcpClient
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

    try {
        $async = $client.BeginConnect($TargetHost, $Port, $null, $null)
        if (-not $async.AsyncWaitHandle.WaitOne($TimeoutMs, $false)) {
            return [pscustomobject]@{
                Port = $Port
                Open = $false
                DurationMs = $stopwatch.ElapsedMilliseconds
                Error = "Timeout"
            }
        }

        $client.EndConnect($async)

        return [pscustomobject]@{
            Port = $Port
            Open = $true
            DurationMs = $stopwatch.ElapsedMilliseconds
            Error = $null
        }
    }
    catch {
        return [pscustomobject]@{
            Port = $Port
            Open = $false
            DurationMs = $stopwatch.ElapsedMilliseconds
            Error = $_.Exception.Message
        }
    }
    finally {
        $stopwatch.Stop()
        $client.Dispose()
    }
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
if (-not $OutputPath) {
    $safeIp = $IpAddress -replace "[:\\/]", "_"
    $OutputPath = Join-Path -Path (Get-Location) -ChildPath "machine-precheck-$safeIp-$timestamp.json"
}
else {
    $OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
}

$result = [ordered]@{
    CheckedAt = (Get-Date).ToString("s")
    IpAddress = $IpAddress
    Ping = $null
    Ports = @()
}

try {
    $pingReply = Test-Connection -ComputerName $IpAddress -Count $PingCount -ErrorAction Stop
    $avgLatency = [Math]::Round((($pingReply | Measure-Object -Property ResponseTime -Average).Average), 2)

    $result.Ping = [pscustomobject]@{
        Reachable = $true
        Count = $PingCount
        AverageLatencyMs = $avgLatency
    }
}
catch {
    $result.Ping = [pscustomobject]@{
        Reachable = $false
        Count = $PingCount
        AverageLatencyMs = $null
        Error = $_.Exception.Message
    }
}

foreach ($port in $Ports) {
    $result.Ports += Test-TcpPort -TargetHost $IpAddress -Port $port -TimeoutMs $TcpTimeoutMs
}

$json = $result | ConvertTo-Json -Depth 5
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($OutputPath, $json, $utf8NoBom)

Write-Host "Precheck saved to: $OutputPath"
Write-Host ""
Write-Host "Ping:"
$result.Ping | Format-List | Out-String | Write-Host

Write-Host "Ports:"
$result.Ports | Format-Table -AutoSize | Out-String | Write-Host

Write-Host "Note:"
Write-Host "- Port 8193 is commonly used in FANUC FOCAS Ethernet scenarios."
Write-Host "- A reachable IP does not prove FOCAS is enabled."
Write-Host "- A closed port does not by itself prove the machine is unsupported; site settings may differ."
