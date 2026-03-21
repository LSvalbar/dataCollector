namespace DataCollector.Contracts;

public enum MachineOperationalState
{
    Processing = 1,
    Waiting = 2,
    Standby = 3,
    PowerOff = 4,
    Alarm = 5,
    Emergency = 6,
    CommunicationInterrupted = 7,
}

public enum DeviceHealthLevel
{
    Normal = 1,
    Warning = 2,
    Critical = 3,
}

public static class MachineOperationalStateExtensions
{
    public static string ToDisplayName(this MachineOperationalState state) =>
        state switch
        {
            MachineOperationalState.Processing => "加工",
            MachineOperationalState.Waiting => "等待",
            MachineOperationalState.Standby => "待机",
            MachineOperationalState.PowerOff => "关机",
            MachineOperationalState.Alarm => "报警",
            MachineOperationalState.Emergency => "急停",
            MachineOperationalState.CommunicationInterrupted => "通信中断",
            _ => "未知",
        };
}
