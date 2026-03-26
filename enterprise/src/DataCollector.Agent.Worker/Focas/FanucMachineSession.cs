using System.Text;
using System.Text.RegularExpressions;
using System.Net.Sockets;
using DataCollector.Contracts;

namespace DataCollector.Agent.Worker.Focas;

internal sealed class FanucMachineSession : IDisposable
{
    private readonly MachineEndpointOptions _machine;
    private readonly ILogger _logger;
    private readonly TimeSpan _transientFailureTolerance;
    private readonly Dictionary<int, string?> _drawingNumberCache = new();
    private ushort _handle;
    private bool _connected;
    private MachineRealtimeSnapshotDto? _lastSuccessfulSnapshot;
    private DateTimeOffset? _failureStartedAt;
    private bool _interruptedReported;

    public FanucMachineSession(
        MachineEndpointOptions machine,
        ILogger logger,
        TimeSpan transientFailureTolerance)
    {
        _machine = machine;
        _logger = logger;
        _transientFailureTolerance = transientFailureTolerance;
    }

    public MachineEndpointOptions Endpoint => _machine;

    public MachineRealtimeSnapshotDto? Collect()
    {
        try
        {
            var snapshot = CollectCore(DateTimeOffset.Now);
            RegisterSuccess(snapshot);
            return snapshot;
        }
        catch (Exception firstException)
        {
            Disconnect();

            try
            {
                var recoveredSnapshot = CollectCore(DateTimeOffset.Now);
                RegisterSuccess(recoveredSnapshot);
                _logger.LogDebug("Recovered collect after reconnect for {DeviceCode}", _machine.DeviceCode);
                return recoveredSnapshot;
            }
            catch (Exception secondException)
            {
                return HandleCollectionFailure(firstException, secondException);
            }
        }
    }

    public void Dispose()
    {
        Disconnect();
    }

    private MachineRealtimeSnapshotDto CollectCore(DateTimeOffset collectedAt)
    {
        EnsureConnected();

        var sysInfoResult = FanucNative.cnc_sysinfo(_handle, out _);
        if (sysInfoResult != FanucNative.EwOk)
        {
            throw CreateFocasException("cnc_sysinfo", sysInfoResult);
        }

        var statusResult = FanucNative.cnc_statinfo(_handle, out var status);
        if (statusResult != FanucNative.EwOk)
        {
            throw CreateFocasException("cnc_statinfo", statusResult);
        }

        var spindleMetrics = ReadSpindleMetrics();
        var program = ReadCurrentProgram();
        var timers = ReadTimers();
        var alarm = ReadCurrentAlarm(status.Alarm != 0);

        return new MachineRealtimeSnapshotDto
        {
            DeviceCode = _machine.DeviceCode,
            CollectedAt = collectedAt,
            MachineOnline = true,
            CurrentState = DeriveState(status.OperationMode, status.Alarm != 0, status.Emergency != 0, spindleMetrics.SpindleSpeedRpm),
            AutomaticMode = status.AutomaticMode,
            OperationMode = status.OperationMode,
            EmergencyState = status.Emergency != 0,
            AlarmState = status.Alarm != 0,
            CurrentAlarmNumber = alarm.AlarmNumber,
            CurrentAlarmMessage = alarm.AlarmMessage,
            ControllerModeText = ControllerModeText(status.AutomaticMode),
            OeeStatusText = DeriveOeeText(status.OperationMode, status.Alarm != 0, status.Emergency != 0),
            SpindleSpeedRpm = spindleMetrics.SpindleSpeedRpm,
            SpindleLoadPercent = spindleMetrics.SpindleLoadPercent,
            CurrentProgramNo = program.ProgramNo,
            CurrentProgramName = program.ProgramName,
            CurrentDrawingNumber = program.DrawingNumber,
            NativePowerOnTotalMs = timers.PowerOnTotalMs,
            NativeOperatingTotalMs = timers.OperatingTotalMs,
            NativeCuttingTotalMs = timers.CuttingTotalMs,
            NativeFreeTotalMs = timers.FreeTotalMs,
            DataQualityCode = "focas_realtime",
        };
    }

    private MachineRealtimeSnapshotDto? HandleCollectionFailure(Exception firstException, Exception secondException)
    {
        var failureTime = DateTimeOffset.Now;
        var effectiveException = secondException;
        var failureStartedAt = _failureStartedAt ?? failureTime;
        var firstFailure = _failureStartedAt is null;
        _failureStartedAt ??= failureTime;

        var elapsed = failureTime - failureStartedAt;
        if (firstFailure)
        {
            _logger.LogWarning(
                effectiveException,
                "Collect failed for {DeviceCode}, entering transient failure grace window {GraceSeconds}s",
                _machine.DeviceCode,
                (int)Math.Max(0, _transientFailureTolerance.TotalSeconds));
        }
        else
        {
            _logger.LogDebug(
                effectiveException,
                "Collect still failing for {DeviceCode} after {ElapsedSeconds}s",
                _machine.DeviceCode,
                (int)Math.Max(0, elapsed.TotalSeconds));
        }

        if (_lastSuccessfulSnapshot is not null && elapsed < _transientFailureTolerance)
        {
            return CloneSnapshot(
                _lastSuccessfulSnapshot,
                failureTime,
                _lastSuccessfulSnapshot.DataQualityCode,
                effectiveException.Message);
        }

        if (_lastSuccessfulSnapshot is null && elapsed < _transientFailureTolerance)
        {
            return null;
        }

        if (!_interruptedReported)
        {
            _logger.LogWarning(
                firstException,
                "Collect failed for {DeviceCode}, marking communication interrupted after {ElapsedSeconds}s",
                _machine.DeviceCode,
                (int)Math.Max(0, elapsed.TotalSeconds));
            _interruptedReported = true;
        }

        return new MachineRealtimeSnapshotDto
        {
            DeviceCode = _machine.DeviceCode,
            CollectedAt = failureTime,
            MachineOnline = false,
            CurrentState = MachineOperationalState.CommunicationInterrupted,
            DataQualityCode = "focas_error",
            ErrorMessage = effectiveException.Message,
        };
    }

    private void RegisterSuccess(MachineRealtimeSnapshotDto snapshot)
    {
        _failureStartedAt = null;
        _interruptedReported = false;
        _lastSuccessfulSnapshot = CloneSnapshot(snapshot, snapshot.CollectedAt, snapshot.DataQualityCode, null);
    }

    private void EnsureConnected()
    {
        if (_connected)
        {
            return;
        }

        ProbeEndpointReachable();

        var result = FanucNative.cnc_allclibhndl3(_machine.IpAddress, (ushort)_machine.Port, _machine.TimeoutSeconds, out _handle);
        if (result != FanucNative.EwOk)
        {
            throw CreateFocasException("cnc_allclibhndl3", result);
        }

        _connected = true;
    }

    private void ProbeEndpointReachable()
    {
        using var tcpClient = new TcpClient();
        var connectTask = tcpClient.ConnectAsync(_machine.IpAddress, _machine.Port);
        var probeTimeout = TimeSpan.FromMilliseconds(Math.Clamp(_machine.TimeoutSeconds * 1000, 1000, 2000));
        if (!connectTask.Wait(probeTimeout))
        {
            throw new TimeoutException($"TCP 预检超时：{_machine.IpAddress}:{_machine.Port}");
        }

        connectTask.GetAwaiter().GetResult();
    }

    private void Disconnect()
    {
        if (!_connected)
        {
            return;
        }

        FanucNative.cnc_freelibhndl(_handle);
        _handle = 0;
        _connected = false;
    }

    private SpindleMetrics ReadSpindleMetrics()
    {
        int? spindleSpeed = null;
        double? spindleLoad = null;

        var actsResult = FanucNative.cnc_acts(_handle, out var actBuffer);
        if (actsResult == FanucNative.EwOk)
        {
            spindleSpeed = Math.Max(0, actBuffer.Data);
        }

        short dataCount = 1;
        var meterResult = FanucNative.cnc_rdspmeter(_handle, FanucNative.PanelSignalAll, ref dataCount, out var meterBuffer);
        if (meterResult == FanucNative.EwOk)
        {
            spindleLoad = ScaleNumericValue(meterBuffer.SpindleLoad.Data, meterBuffer.SpindleLoad.Decimal);
            spindleSpeed ??= (int?)ScaleNumericValue(meterBuffer.SpindleSpeed.Data, meterBuffer.SpindleSpeed.Decimal);
        }

        if (!spindleLoad.HasValue)
        {
            var loadResult = FanucNative.cnc_rdspload(_handle, 1, out var loadBuffer);
            if (loadResult == FanucNative.EwOk && loadBuffer.Data is { Length: > 0 })
            {
                spindleLoad = Math.Max(0, (int)loadBuffer.Data[0]);
            }
        }

        return new SpindleMetrics(
            spindleSpeed.HasValue ? Math.Max(0, spindleSpeed.Value) : null,
            spindleLoad);
    }

    private ProgramMetadata ReadCurrentProgram()
    {
        var result = FanucNative.cnc_exeprgname(_handle, out var buffer);
        if (result != FanucNative.EwOk)
        {
            return ProgramMetadata.Empty;
        }

        var programName = DecodeAscii(buffer.Name);
        if (buffer.ONumber == 0)
        {
            return new ProgramMetadata(null, null, string.IsNullOrWhiteSpace(programName) ? null : programName, null);
        }

        return new ProgramMetadata(
            buffer.ONumber,
            $"O{buffer.ONumber:0000}",
            string.IsNullOrWhiteSpace(programName) ? null : programName,
            ReadDrawingNumber(buffer.ONumber));
    }

    private string? ReadDrawingNumber(int programNumber)
    {
        if (_drawingNumberCache.TryGetValue(programNumber, out var cachedDrawingNumber))
        {
            return cachedDrawingNumber;
        }

        long topProgramNumber = programNumber;
        short readCount = 1;
        var buffer = new FanucNative.PrgDir3[1];
        var result = FanucNative.cnc_rdprogdir3(_handle, 1, ref topProgramNumber, ref readCount, buffer);
        if (result != FanucNative.EwOk || readCount <= 0)
        {
            _drawingNumberCache[programNumber] = null;
            return null;
        }

        var entry = buffer[0];
        if (entry.Number != programNumber)
        {
            _drawingNumberCache[programNumber] = null;
            return null;
        }

        var drawingNumber = ExtractDrawingNumber(DecodeAscii(entry.Comment));
        _drawingNumberCache[programNumber] = drawingNumber;
        return drawingNumber;
    }

    private (long? PowerOnTotalMs, long? OperatingTotalMs, long? CuttingTotalMs, long? FreeTotalMs) ReadTimers()
    {
        return (
            ReadTimer(FanucNative.TimerPowerOn),
            ReadTimer(FanucNative.TimerOperating),
            ReadTimer(FanucNative.TimerCutting),
            ReadTimer(FanucNative.TimerFree));
    }

    private long? ReadTimer(short timerType)
    {
        var result = FanucNative.cnc_rdtimer(_handle, timerType, out var buffer);
        return result == FanucNative.EwOk
            ? (long)buffer.Minute * 60_000L + buffer.Millisecond
            : null;
    }

    private MachineOperationalState DeriveState(int operationMode, bool alarm, bool emergency, int? spindleSpeed)
    {
        if (emergency)
        {
            return MachineOperationalState.Emergency;
        }

        if (alarm)
        {
            return MachineOperationalState.Alarm;
        }

        if ((spindleSpeed ?? 0) > 0 || _machine.ProcessingOperationModes.Contains(operationMode))
        {
            return MachineOperationalState.Processing;
        }

        return MachineOperationalState.Standby;
    }

    private static string ControllerModeText(int automaticMode)
    {
        return automaticMode switch
        {
            0 => "MDI",
            1 => "Memory",
            2 => "Tape",
            3 => "Edit",
            4 => "Handle",
            5 => "JOG",
            6 => "Teach",
            7 => "Remote",
            _ => $"Unknown({automaticMode})",
        };
    }

    private string DeriveOeeText(int operationMode, bool alarm, bool emergency)
    {
        if (emergency)
        {
            return "Emergency";
        }

        if (alarm)
        {
            return "Alarm";
        }

        if (_machine.ProcessingOperationModes.Contains(operationMode))
        {
            return "Running";
        }

        return "Interrupted";
    }

    private (int? AlarmNumber, string? AlarmMessage) ReadCurrentAlarm(bool alarmActive)
    {
        if (!alarmActive)
        {
            return (null, null);
        }

        short readCount = 10;
        var alarmMessages = new FanucNative.OdbAlmMsg2[10];
        var messageResult = FanucNative.cnc_rdalmmsg2(_handle, FanucNative.AlarmTypeAll, ref readCount, alarmMessages);
        if (messageResult == FanucNative.EwOk)
        {
            var limit = Math.Max(0, Math.Min((int)readCount, alarmMessages.Length));
            for (var index = 0; index < limit; index++)
            {
                var message = alarmMessages[index];
                var alarmMessage = DecodeAscii(message.AlarmMessage).Trim();
                if (message.AlarmNumber == 0 && string.IsNullOrWhiteSpace(alarmMessage))
                {
                    continue;
                }

                return (message.AlarmNumber == 0 ? null : message.AlarmNumber, string.IsNullOrWhiteSpace(alarmMessage) ? null : alarmMessage);
            }
        }

        var infoResult = FanucNative.cnc_rdalminfo2(_handle, FanucNative.AlarmInformation2, FanucNative.AlarmTypeAll, 0, out var alarmInfo);
        if (infoResult == FanucNative.EwOk && alarmInfo.Union.Alarm2.Alarms is { Length: > 0 })
        {
            foreach (var entry in alarmInfo.Union.Alarm2.Alarms)
            {
                var alarmMessage = DecodeAscii(entry.AlarmMessage).Trim();
                if (entry.AlarmNumber == 0 && string.IsNullOrWhiteSpace(alarmMessage))
                {
                    continue;
                }

                return (entry.AlarmNumber == 0 ? null : entry.AlarmNumber, string.IsNullOrWhiteSpace(alarmMessage) ? null : alarmMessage);
            }
        }

        return (null, null);
    }

    private static double ScaleNumericValue(int value, short decimals)
    {
        if (decimals <= 0)
        {
            return value;
        }

        return value / Math.Pow(10, decimals);
    }

    private static MachineRealtimeSnapshotDto CloneSnapshot(
        MachineRealtimeSnapshotDto source,
        DateTimeOffset collectedAt,
        string dataQualityCode,
        string? errorMessage)
    {
        return new MachineRealtimeSnapshotDto
        {
            DeviceCode = source.DeviceCode,
            CollectedAt = collectedAt,
            MachineOnline = source.MachineOnline,
            CurrentState = source.CurrentState,
            AutomaticMode = source.AutomaticMode,
            OperationMode = source.OperationMode,
            EmergencyState = source.EmergencyState,
            AlarmState = source.AlarmState,
            CurrentAlarmNumber = source.CurrentAlarmNumber,
            CurrentAlarmMessage = source.CurrentAlarmMessage,
            ControllerModeText = source.ControllerModeText,
            OeeStatusText = source.OeeStatusText,
            SpindleSpeedRpm = source.SpindleSpeedRpm,
            SpindleLoadPercent = source.SpindleLoadPercent,
            CurrentProgramNo = source.CurrentProgramNo,
            CurrentProgramName = source.CurrentProgramName,
            CurrentDrawingNumber = source.CurrentDrawingNumber,
            NativePowerOnTotalMs = source.NativePowerOnTotalMs,
            NativeOperatingTotalMs = source.NativeOperatingTotalMs,
            NativeCuttingTotalMs = source.NativeCuttingTotalMs,
            NativeFreeTotalMs = source.NativeFreeTotalMs,
            DataQualityCode = dataQualityCode,
            ErrorMessage = errorMessage,
        };
    }

    private static InvalidOperationException CreateFocasException(string functionName, short errorCode)
    {
        return new InvalidOperationException(
            $"{functionName} failed with code {errorCode} ({FanucNative.DescribeError(errorCode)})");
    }

    private static string DecodeAscii(byte[] raw)
    {
        return Encoding.ASCII.GetString(raw).Replace("\0", string.Empty).Trim();
    }

    private static string? ExtractDrawingNumber(string? programComment)
    {
        if (string.IsNullOrWhiteSpace(programComment))
        {
            return null;
        }

        var normalized = programComment.Trim();
        if (normalized == "()")
        {
            return null;
        }

        var match = Regex.Match(normalized, @"\((?<drawing>[^)]{1,200})\)", RegexOptions.CultureInvariant);
        if (match.Success)
        {
            var drawingNumber = match.Groups["drawing"].Value.Trim();
            return string.IsNullOrWhiteSpace(drawingNumber) ? null : drawingNumber;
        }

        return normalized;
    }

    private sealed record SpindleMetrics(int? SpindleSpeedRpm, double? SpindleLoadPercent);

    private sealed record ProgramMetadata(int? ProgramNumber, string? ProgramNo, string? ProgramName, string? DrawingNumber)
    {
        public static ProgramMetadata Empty { get; } = new(null, null, null, null);
    }
}
