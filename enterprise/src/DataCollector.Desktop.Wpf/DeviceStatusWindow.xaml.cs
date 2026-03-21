using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DataCollector.Contracts;

namespace DataCollector.Desktop.Wpf;

public partial class DeviceStatusWindow : Window
{
    private readonly Guid _deviceId;
    private readonly Func<DeviceDto?> _deviceProvider;
    private readonly DispatcherTimer _refreshTimer;

    public DeviceStatusWindow(Guid deviceId, Func<DeviceDto?> deviceProvider)
    {
        _deviceId = deviceId;
        _deviceProvider = deviceProvider;
        InitializeComponent();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _refreshTimer.Tick += (_, _) => RefreshView();

        Loaded += (_, _) =>
        {
            RefreshView();
            _refreshTimer.Start();
        };
        Closed += (_, _) => _refreshTimer.Stop();
    }

    private void RefreshView()
    {
        var device = _deviceProvider();
        if (device is null)
        {
            DeviceTitleTextBlock.Text = "设备已不存在";
            DeviceSubtitleTextBlock.Text = _deviceId.ToString();
            MetricsItemsControl.ItemsSource = Array.Empty<DeviceMetricItem>();
            return;
        }

        DeviceTitleTextBlock.Text = $"{device.DeviceCode} · {device.DeviceName}";
        DeviceSubtitleTextBlock.Text = $"{device.DepartmentName} / {device.WorkshopName} / {device.ControllerModel}";
        LastUpdatedTextBlock.Text = $"最近更新：{device.LastCollectedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-"}";
        DataQualityTextBlock.Text = $"数据质量：{device.DataQualityCode ?? "-"}";

        ApplyStateBlock(CurrentStateBorder, CurrentStateTextBlock, device.CurrentState.ToDisplayName(), GetStateBackground(device.CurrentState), GetStateForeground(device.CurrentState));
        ApplyStateBlock(OnlineStateBorder, OnlineStateTextBlock, device.MachineOnline ? "在线" : "离线", device.MachineOnline ? CreateBrush("#DCFCE7") : CreateBrush("#E2E8F0"), device.MachineOnline ? CreateBrush("#166534") : CreateBrush("#475569"));
        ApplyStateBlock(HealthStateBorder, HealthStateTextBlock, GetHealthText(device.HealthLevel), GetHealthBackground(device.HealthLevel), GetHealthForeground(device.HealthLevel));
        ProgramTextBlock.Text = string.IsNullOrWhiteSpace(device.CurrentProgramNo) ? "-" : $"{device.CurrentProgramNo} / {device.CurrentProgramName ?? "-"}";

        MetricsItemsControl.ItemsSource = BuildMetricItems(device);
    }

    private static void ApplyStateBlock(Border border, TextBlock textBlock, string text, Brush background, Brush foreground)
    {
        border.Background = background;
        textBlock.Text = text;
        textBlock.Foreground = foreground;
    }

    private static IReadOnlyList<DeviceMetricItem> BuildMetricItems(DeviceDto device)
    {
        return
        [
            CreateMetric("主轴转速", device.SpindleSpeedRpm is null ? "-" : $"{device.SpindleSpeedRpm} rpm", device.SpindleSpeedRpm.GetValueOrDefault() > 0 ? CreateBrush("#DCFCE7") : CreateBrush("#E2E8F0"), device.SpindleSpeedRpm.GetValueOrDefault() > 0 ? CreateBrush("#166534") : CreateBrush("#475569")),
            CreateMetric("主轴负载", device.SpindleLoadPercent is null ? "-" : $"{device.SpindleLoadPercent:F1}%", GetLoadBackground(device.SpindleLoadPercent), GetLoadForeground(device.SpindleLoadPercent)),
            CreateMetric("自动模式", device.AutomaticMode.ToString(), CreateBrush("#DBEAFE"), CreateBrush("#1D4ED8")),
            CreateMetric("运行模式", device.OperationMode.ToString(), CreateBrush("#E0F2FE"), CreateBrush("#0369A1")),
            CreateMetric("控制模式", device.ControllerModeText ?? "-", CreateBrush("#F8FAFC"), CreateBrush("#334155")),
            CreateMetric("OEE 状态", device.OeeStatusText ?? "-", CreateBrush("#F8FAFC"), CreateBrush("#334155")),
            CreateMetric("报警状态", device.AlarmState ? "报警中" : "正常", device.AlarmState ? CreateBrush("#FEE2E2") : CreateBrush("#DCFCE7"), device.AlarmState ? CreateBrush("#B91C1C") : CreateBrush("#166534")),
            CreateMetric("急停状态", device.EmergencyState ? "急停中" : "正常", device.EmergencyState ? CreateBrush("#FECACA") : CreateBrush("#DCFCE7"), device.EmergencyState ? CreateBrush("#991B1B") : CreateBrush("#166534")),
            CreateMetric("开机累计", FormatDuration(device.NativePowerOnTotalMs), CreateBrush("#E0F2FE"), CreateBrush("#0F766E")),
            CreateMetric("运行累计", FormatDuration(device.NativeOperatingTotalMs), CreateBrush("#DCFCE7"), CreateBrush("#166534")),
            CreateMetric("切削累计", FormatDuration(device.NativeCuttingTotalMs), CreateBrush("#DBEAFE"), CreateBrush("#1D4ED8")),
            CreateMetric("等待累计", FormatDuration(device.NativeFreeTotalMs), CreateBrush("#FEF3C7"), CreateBrush("#92400E")),
            CreateMetric("系统版本", device.ControllerModel, CreateBrush("#F8FAFC"), CreateBrush("#334155")),
            CreateMetric("负责人", string.IsNullOrWhiteSpace(device.ResponsiblePerson) ? "-" : device.ResponsiblePerson, CreateBrush("#F8FAFC"), CreateBrush("#334155")),
            CreateMetric("设备地址", $"{device.IpAddress}:{device.Port}", CreateBrush("#F8FAFC"), CreateBrush("#334155")),
            CreateMetric("Agent 节点", device.AgentNodeName, CreateBrush("#F8FAFC"), CreateBrush("#334155")),
            CreateMetric("最近心跳", device.LastHeartbeatAt.ToString("yyyy-MM-dd HH:mm:ss"), CreateBrush("#F8FAFC"), CreateBrush("#334155")),
            CreateMetric("最近采集错误", string.IsNullOrWhiteSpace(device.LastCollectionError) ? "-" : device.LastCollectionError, string.IsNullOrWhiteSpace(device.LastCollectionError) ? CreateBrush("#F8FAFC") : CreateBrush("#FEE2E2"), string.IsNullOrWhiteSpace(device.LastCollectionError) ? CreateBrush("#334155") : CreateBrush("#991B1B")),
            CreateMetric("数据质量", device.DataQualityCode ?? "-", GetDataQualityBackground(device.DataQualityCode), GetDataQualityForeground(device.DataQualityCode)),
        ];
    }

    private static DeviceMetricItem CreateMetric(string label, string value, Brush background, Brush foreground)
    {
        return new DeviceMetricItem
        {
            Label = label,
            Value = value,
            ValueBackground = background,
            ValueForeground = foreground,
        };
    }

    private static string FormatDuration(long? milliseconds)
    {
        if (milliseconds is null)
        {
            return "-";
        }

        var totalMinutes = milliseconds.Value / 1000d / 60d;
        return $"{totalMinutes:F1} 分钟";
    }

    private static string GetHealthText(DeviceHealthLevel healthLevel) =>
        healthLevel switch
        {
            DeviceHealthLevel.Normal => "正常",
            DeviceHealthLevel.Warning => "关注",
            DeviceHealthLevel.Critical => "异常",
            _ => "未知",
        };

    private static Brush GetHealthBackground(DeviceHealthLevel healthLevel) =>
        healthLevel switch
        {
            DeviceHealthLevel.Normal => CreateBrush("#DCFCE7"),
            DeviceHealthLevel.Warning => CreateBrush("#FEF3C7"),
            DeviceHealthLevel.Critical => CreateBrush("#FEE2E2"),
            _ => CreateBrush("#E2E8F0"),
        };

    private static Brush GetHealthForeground(DeviceHealthLevel healthLevel) =>
        healthLevel switch
        {
            DeviceHealthLevel.Normal => CreateBrush("#166534"),
            DeviceHealthLevel.Warning => CreateBrush("#92400E"),
            DeviceHealthLevel.Critical => CreateBrush("#991B1B"),
            _ => CreateBrush("#475569"),
        };

    private static Brush GetLoadBackground(double? loadPercent)
    {
        if (!loadPercent.HasValue)
        {
            return CreateBrush("#E2E8F0");
        }

        return loadPercent.Value switch
        {
            >= 80 => CreateBrush("#FEE2E2"),
            >= 60 => CreateBrush("#FEF3C7"),
            _ => CreateBrush("#DCFCE7"),
        };
    }

    private static Brush GetLoadForeground(double? loadPercent)
    {
        if (!loadPercent.HasValue)
        {
            return CreateBrush("#475569");
        }

        return loadPercent.Value switch
        {
            >= 80 => CreateBrush("#991B1B"),
            >= 60 => CreateBrush("#92400E"),
            _ => CreateBrush("#166534"),
        };
    }

    private static Brush GetDataQualityBackground(string? dataQualityCode)
    {
        return dataQualityCode switch
        {
            "focas_realtime" => CreateBrush("#DCFCE7"),
            "realtime_session" => CreateBrush("#DBEAFE"),
            "seed_demo" => CreateBrush("#E2E8F0"),
            _ when !string.IsNullOrWhiteSpace(dataQualityCode) => CreateBrush("#FEF3C7"),
            _ => CreateBrush("#E2E8F0"),
        };
    }

    private static Brush GetDataQualityForeground(string? dataQualityCode)
    {
        return dataQualityCode switch
        {
            "focas_realtime" => CreateBrush("#166534"),
            "realtime_session" => CreateBrush("#1D4ED8"),
            "seed_demo" => CreateBrush("#475569"),
            _ when !string.IsNullOrWhiteSpace(dataQualityCode) => CreateBrush("#92400E"),
            _ => CreateBrush("#475569"),
        };
    }

    private static Brush GetStateBackground(MachineOperationalState state) =>
        state switch
        {
            MachineOperationalState.Processing => CreateBrush("#DCFCE7"),
            MachineOperationalState.Waiting => CreateBrush("#FEF3C7"),
            MachineOperationalState.Standby => CreateBrush("#FEF3C7"),
            MachineOperationalState.PowerOff => CreateBrush("#E2E8F0"),
            MachineOperationalState.Alarm => CreateBrush("#FEE2E2"),
            MachineOperationalState.Emergency => CreateBrush("#FECACA"),
            MachineOperationalState.CommunicationInterrupted => CreateBrush("#F5E8FF"),
            _ => CreateBrush("#E2E8F0"),
        };

    private static Brush GetStateForeground(MachineOperationalState state) =>
        state switch
        {
            MachineOperationalState.Processing => CreateBrush("#166534"),
            MachineOperationalState.Waiting => CreateBrush("#92400E"),
            MachineOperationalState.Standby => CreateBrush("#92400E"),
            MachineOperationalState.PowerOff => CreateBrush("#475569"),
            MachineOperationalState.Alarm => CreateBrush("#B91C1C"),
            MachineOperationalState.Emergency => CreateBrush("#991B1B"),
            MachineOperationalState.CommunicationInterrupted => CreateBrush("#6D28D9"),
            _ => CreateBrush("#475569"),
        };

    private static Brush CreateBrush(string hex)
    {
        return (Brush)new BrushConverter().ConvertFromString(hex)!;
    }

    private sealed class DeviceMetricItem
    {
        public required string Label { get; init; }
        public required string Value { get; init; }
        public required Brush ValueBackground { get; init; }
        public required Brush ValueForeground { get; init; }
    }
}
