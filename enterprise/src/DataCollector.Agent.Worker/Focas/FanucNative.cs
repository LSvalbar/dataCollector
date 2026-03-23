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
    public const short AlarmTypeAll = -1;
    public const short AlarmInformation2 = 1;
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
    public static extern short cnc_rdspmeter(ushort handle, short dataType, ref short dataCount, out OdbSpLoad buffer);

    [DllImport("Fwlib32.dll", CallingConvention = CallingConvention.Winapi)]
    public static extern short cnc_rdspload(ushort handle, short spindleNumber, out OdbSpn buffer);

    [DllImport("Fwlib32.dll", CallingConvention = CallingConvention.Winapi)]
    public static extern short cnc_exeprgname(ushort handle, out OdbExePrg buffer);

    [DllImport("Fwlib32.dll", CallingConvention = CallingConvention.Winapi)]
    public static extern short cnc_rdprogdir3(ushort handle, short type, ref long topProgramNumber, ref short readCount, [Out] PrgDir3[] buffer);

    [DllImport("Fwlib32.dll", CallingConvention = CallingConvention.Winapi)]
    public static extern short cnc_rdtimer(ushort handle, short timerType, out IodbTime buffer);

    [DllImport("Fwlib32.dll", CallingConvention = CallingConvention.Winapi)]
    public static extern short cnc_rdalmmsg2(ushort handle, short alarmType, ref short readCount, [Out] OdbAlmMsg2[] buffer);

    [DllImport("Fwlib32.dll", CallingConvention = CallingConvention.Winapi)]
    public static extern short cnc_rdalminfo2(ushort handle, short informationType, short alarmType, short axis, out AlmInfo2 buffer);

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
    public struct LoadElement
    {
        public int Data;
        public short Decimal;
        public short Unit;
        public byte Name;
        public byte Suffix1;
        public byte Suffix2;
        public byte Reserve;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OdbSpLoad
    {
        public LoadElement SpindleLoad;
        public LoadElement SpindleSpeed;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OdbSpn
    {
        public short DataNumber;
        public short Type;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public short[] Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OdbExePrg
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 36)]
        public byte[] Name;

        public int ONumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PrgDirDate
    {
        public short Year;
        public short Month;
        public short Day;
        public short Hour;
        public short Minute;
        public short Dummy;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PrgDir3
    {
        public int Number;
        public int Length;
        public int Page;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 52)]
        public byte[] Comment;

        public PrgDirDate ModifiedAt;
        public PrgDirDate CreatedAt;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IodbTime
    {
        public int Minute;
        public int Millisecond;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OdbAlmMsg2
    {
        public int AlarmNumber;
        public short Type;
        public short Axis;
        public short Dummy;
        public short MessageLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] AlarmMessage;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AlmInfo2Entry
    {
        public short Axis;
        public short AlarmNumber;
        public short MessageLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 34)]
        public byte[] AlarmMessage;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AlmInfo2Alarm2
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
        public AlmInfo2Entry[] Alarms;

        public short DataEnd;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct AlmInfo2Union
    {
        [FieldOffset(0)]
        public AlmInfo2Alarm2 Alarm2;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AlmInfo2
    {
        public AlmInfo2Union Union;
    }
}
