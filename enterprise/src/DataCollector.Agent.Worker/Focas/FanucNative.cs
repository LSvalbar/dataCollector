using System.Runtime.InteropServices;

namespace DataCollector.Agent.Worker.Focas;

internal static class FanucNative
{
    public const short EwProtocol = -17;
    public const short EwSocket = -16;
    public const short EwNoDll = -15;
    public const short EwIniError = -14;
    public const short EwBus = -11;
    public const short EwSystem2 = -10;
    public const short EwHssb = -9;
    public const short EwHandle = -8;
    public const short EwVersion = -7;
    public const short EwUnexpected = -6;
    public const short EwSystem = -5;
    public const short EwReset = -2;
    public const short EwBusy = -1;
    public const short EwOk = 0;
    public const short PanelSignalAll = -1;
    public const short TimerPowerOn = 0;
    public const short TimerOperating = 1;
    public const short TimerCutting = 2;
    public const short TimerCycle = 3;
    public const short TimerFree = 4;

    [DllImport("Fwlib32.dll", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Ansi)]
    public static extern short cnc_allclibhndl3(string ipAddress, ushort port, int timeoutSeconds, out ushort handle);

    [DllImport("Fwlib32.dll", CallingConvention = CallingConvention.Winapi)]
    public static extern short cnc_freelibhndl(ushort handle);

    [DllImport("Fwlib32.dll", CallingConvention = CallingConvention.Winapi)]
    public static extern short cnc_sysinfo(ushort handle, out OdbSys buffer);

    [DllImport("Fwlib32.dll", CallingConvention = CallingConvention.Winapi)]
    public static extern short cnc_statinfo(ushort handle, out OdbSt buffer);

    [DllImport("Fwlib32.dll", CallingConvention = CallingConvention.Winapi)]
    public static extern short cnc_acts(ushort handle, out OdbAct buffer);

    [DllImport("Fwlib32.dll", CallingConvention = CallingConvention.Winapi)]
    public static extern short cnc_exeprgname(ushort handle, out OdbExePrg buffer);

    [DllImport("Fwlib32.dll", CallingConvention = CallingConvention.Winapi)]
    public static extern short cnc_rdtimer(ushort handle, short timerType, out IodbTime buffer);

    public static string DescribeError(short code)
    {
        return code switch
        {
            EwProtocol => "EW_PROTOCOL",
            EwSocket => "EW_SOCKET",
            EwNoDll => "EW_NODLL",
            EwIniError => "EW_INIERR",
            EwBus => "EW_BUS",
            EwSystem2 => "EW_SYSTEM2",
            EwHssb => "EW_HSSB",
            EwHandle => "EW_HANDLE",
            EwVersion => "EW_VERSION",
            EwUnexpected => "EW_UNEXP",
            EwSystem => "EW_SYSTEM",
            EwReset => "EW_RESET",
            EwBusy => "EW_BUSY",
            EwOk => "EW_OK",
            _ => $"EW_UNKNOWN({code})",
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OdbSys
    {
        public short AddInfo;
        public short MaxAxis;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] CncType;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] MtType;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] Series;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] Version;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] Axes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OdbSt
    {
        public short Dummy;
        public short TmMode;
        public short AutomaticMode;
        public short OperationMode;
        public short Motion;
        public short Mstb;
        public short Emergency;
        public short Alarm;
        public short Edit;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OdbAct
    {
        public short Dummy1;
        public short Dummy2;
        public int Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OdbExePrg
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 36)]
        public byte[] Name;

        public int ONumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IodbTime
    {
        public int Minute;
        public int Millisecond;
    }
}
