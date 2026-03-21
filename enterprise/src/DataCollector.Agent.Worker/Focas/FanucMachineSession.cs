using System.Text;
using DataCollector.Contracts;

namespace DataCollector.Agent.Worker.Focas;

internal sealed class FanucMachineSession : IDisposable
{
    private readonly MachineEndpointOptions _machine;
    private readonly ILogger _logger;
    private readonly TimeSpan _transientFailureTolerance;
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

        var spindleSpeed = ReadSpindleSpeed();
        var program = ReadCurrentProgram();
        var timers = ReadTimers();

        return new MachineRealtimeSnapshotDto
        {
            DeviceCode = _machine.DeviceCode,
            CollectedAt = collectedAt,
            MachineOnline = true,
            CurrentState = DeriveState(status.OperationMode, status.Alarm != 0, status.Emergency != 0, spindleSpeed),
            AutomaticMode = status.AutomaticMode,
            OperationMode = status.OperationMode,
            EmergencyState = status.Emergency != 0,
            AlarmState = status.Alarm != 0,
            ControllerModeText = ControllerModeText(status.AutomaticMode),
            OeeStatusText = DeriveOeeText(status.OperationMode, status.Alarm != 0, status.Emergency != 0),
            SpindleSpeedRpm = spindleSpeed,
            CurrentProgramNo = program.ProgramNo,
            CurrentProgramName = program.ProgramName,
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

        var result = FanucNative.cnc_allclibhndl3(_machine.IpAddress, (ushort)_machine.Port, _machine.TimeoutSeconds, out _handle);
        if (result != FanucNative.EwOk)
        {
            throw CreateFocasException("cnc_allclibhndl3", result);
        }

        _connected = true;
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

    private int? ReadSpindleSpeed()
    {
        var result = FanucNative.cnc_acts(_handle, out var buffer);
        return result == FanucNative.EwOk ? Math.Max(0, buffer.Data) : null;
    }

    private (string? ProgramNo, string? ProgramName) ReadCurrentProgram()
    {
        var result = FanucNative.cnc_exeprgname(_handle, out var buffer);
        if (result != FanucNative.EwOk)
        {
            return (null, null);
        }

        var programName = DecodeAscii(buffer.Name);
        return (buffer.ONumber == 0 ? null : $"O{buffer.ONumber:0000}", string.IsNullOrWhiteSpace(programName) ? null : programName);
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

        if (_machine.WaitingOperationModes.Contains(operationMode))
        {
            return MachineOperationalState.Waiting;
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
            ControllerModeText = source.ControllerModeText,
            OeeStatusText = source.OeeStatusText,
            SpindleSpeedRpm = source.SpindleSpeedRpm,
            SpindleLoadPercent = source.SpindleLoadPercent,
            CurrentProgramNo = source.CurrentProgramNo,
            CurrentProgramName = source.CurrentProgramName,
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
}
