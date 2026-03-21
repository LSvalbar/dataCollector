using System.Text;
using DataCollector.Contracts;

namespace DataCollector.Agent.Worker.Focas;

internal sealed class FanucMachineSession : IDisposable
{
    private readonly MachineEndpointOptions _machine;
    private readonly ILogger _logger;
    private ushort _handle;
    private bool _connected;

    public FanucMachineSession(MachineEndpointOptions machine, ILogger logger)
    {
        _machine = machine;
        _logger = logger;
    }

    public MachineEndpointOptions Endpoint => _machine;

    public MachineRealtimeSnapshotDto Collect()
    {
        try
        {
            EnsureConnected();

            FanucNative.cnc_sysinfo(_handle, out _);
            var statusResult = FanucNative.cnc_statinfo(_handle, out var status);
            if (statusResult != FanucNative.EwOk)
            {
                throw new InvalidOperationException($"cnc_statinfo failed with code {statusResult}");
            }

            var spindleSpeed = ReadSpindleSpeed();
            var program = ReadCurrentProgram();
            var timers = ReadTimers();

            return new MachineRealtimeSnapshotDto
            {
                DeviceCode = _machine.DeviceCode,
                CollectedAt = DateTimeOffset.Now,
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
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Collect failed for {DeviceCode}", _machine.DeviceCode);
            Disconnect();
            return new MachineRealtimeSnapshotDto
            {
                DeviceCode = _machine.DeviceCode,
                CollectedAt = DateTimeOffset.Now,
                MachineOnline = false,
                CurrentState = MachineOperationalState.CommunicationInterrupted,
                DataQualityCode = "focas_error",
                ErrorMessage = exception.Message,
            };
        }
    }

    public void Dispose()
    {
        Disconnect();
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
            throw new InvalidOperationException($"cnc_allclibhndl3 failed with code {result}");
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

    private static string DecodeAscii(byte[] raw)
    {
        return Encoding.ASCII.GetString(raw).Replace("\0", string.Empty).Trim();
    }
}
