using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DataCollector.Contracts;
using DataCollector.Desktop.Wpf.Services;

namespace DataCollector.Desktop.Wpf;

public partial class MainWindow : Window
{
    private readonly EnterpriseApiClient _apiClient = new();
    private readonly DispatcherTimer _autoRefreshTimer;
    private List<DeviceDto> _devices = [];

    public MainWindow()
    {
        InitializeComponent();
        ReportDatePicker.SelectedDate = DateTime.Today;
        TimelineDatePicker.SelectedDate = DateTime.Today;
        ApiBaseUrlTextBlock.Text = $"API：{_apiClient.BaseAddress}";

        _autoRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(15),
        };
        _autoRefreshTimer.Tick += async (_, _) => await RefreshOverviewAsync(false);

        Loaded += async (_, _) =>
        {
            _autoRefreshTimer.Start();
            await RefreshAllAsync(true);
        };
        Closed += (_, _) =>
        {
            _autoRefreshTimer.Stop();
            _apiClient.Dispose();
        };
    }

    private async Task RefreshAllAsync(bool showErrors)
    {
        try
        {
            var serverOnline = await _apiClient.PingAsync();
            UpdateServerStatus(serverOnline);
            if (!serverOnline)
            {
                if (showErrors)
                {
                    MessageBox.Show(
                        this,
                        "未连接到正式服务端，请先启动 enterprise 服务端 API。",
                        "服务不可用",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                return;
            }

            await RefreshOverviewAsync(showErrors);
            await RefreshReportAsync(showErrors);
            await RefreshSecurityAsync(showErrors);
            await RefreshTimelineAsync(showErrors);
        }
        catch (Exception exception)
        {
            if (showErrors)
            {
                MessageBox.Show(this, exception.Message, "刷新失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            ServerStatusTextBlock.Text = $"服务状态：异常 - {exception.Message}";
            ServerStatusTextBlock.Foreground = Brushes.OrangeRed;
        }
    }

    private async Task RefreshOverviewAsync(bool showErrors)
    {
        try
        {
            var overview = await _apiClient.GetOverviewAsync();
            if (overview is null)
            {
                return;
            }

            _devices = overview.Devices.ToList();
            OverviewSummaryTextBlock.Text = $"共 {_devices.Count} 台设备 | 快照时间 {overview.SnapshotAt:yyyy-MM-dd HH:mm:ss}";
            DrawWorkshopCards(overview.Workshops);
            DevicesGrid.ItemsSource = _devices.Select(ToDeviceGridRow).ToList();

            var selectedDeviceId = TimelineDeviceComboBox.SelectedValue as Guid?;
            TimelineDeviceComboBox.ItemsSource = _devices
                .OrderBy(device => device.WorkshopCode)
                .ThenBy(device => device.DeviceCode)
                .Select(device => new TimelineDeviceItem
                {
                    DeviceId = device.DeviceId,
                    DisplayText = $"{device.WorkshopName} | {device.DeviceCode} | {device.DeviceName}",
                })
                .ToList();

            if (selectedDeviceId.HasValue &&
                TimelineDeviceComboBox.ItemsSource is IEnumerable<TimelineDeviceItem> items &&
                items.Any(item => item.DeviceId == selectedDeviceId.Value))
            {
                TimelineDeviceComboBox.SelectedValue = selectedDeviceId.Value;
            }
            else
            {
                TimelineDeviceComboBox.SelectedIndex = 0;
            }
        }
        catch (Exception exception)
        {
            if (showErrors)
            {
                MessageBox.Show(this, exception.Message, "设备刷新失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async Task RefreshReportAsync(bool showErrors)
    {
        try
        {
            var reportDate = DateOnly.FromDateTime(ReportDatePicker.SelectedDate ?? DateTime.Today);
            var response = await _apiClient.GetDailyReportAsync(reportDate);
            if (response is null)
            {
                return;
            }

            var powerOnFormula = response.Formulas.FirstOrDefault(formula => formula.Code == "power_on_rate");
            var utilizationFormula = response.Formulas.FirstOrDefault(formula => formula.Code == "utilization_rate");
            PowerOnFormulaTextBox.Text = powerOnFormula?.Expression ?? string.Empty;
            UtilizationFormulaTextBox.Text = utilizationFormula?.Expression ?? string.Empty;
            ReportSummaryTextBlock.Text = $"日报快照：{response.SnapshotAt:yyyy-MM-dd HH:mm:ss}";
            DailyReportGrid.ItemsSource = response.Rows.Select(ToReportGridRow).ToList();
        }
        catch (Exception exception)
        {
            if (showErrors)
            {
                MessageBox.Show(this, exception.Message, "报表刷新失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async Task RefreshTimelineAsync(bool showErrors)
    {
        try
        {
            if (TimelineDeviceComboBox.SelectedValue is not Guid deviceId)
            {
                return;
            }

            var reportDate = DateOnly.FromDateTime(TimelineDatePicker.SelectedDate ?? DateTime.Today);
            var timeline = await _apiClient.GetTimelineAsync(deviceId, reportDate);
            if (timeline is null)
            {
                return;
            }

            DrawTimelineTotals(timeline.DailyTotals);
            TimelineGrid.ItemsSource = timeline.Segments.Select(segment => new TimelineGridRow
            {
                StateText = segment.State.ToDisplayName(),
                StartAtText = segment.StartAt.ToString("yyyy-MM-dd HH:mm:ss"),
                EndAtText = segment.EndAt.ToString("yyyy-MM-dd HH:mm:ss"),
                DurationMinutesText = segment.DurationMinutes.ToString("F2"),
                DataQualityCode = segment.DataQualityCode,
            }).ToList();
        }
        catch (Exception exception)
        {
            if (showErrors)
            {
                MessageBox.Show(this, exception.Message, "时间线刷新失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async Task RefreshSecurityAsync(bool showErrors)
    {
        try
        {
            var security = await _apiClient.GetSecurityOverviewAsync();
            if (security is null)
            {
                return;
            }

            UsersGrid.ItemsSource = security.Users.Select(user => new UserGridRow
            {
                UserName = user.UserName,
                DisplayName = user.DisplayName,
                Department = user.Department,
                RoleCodesText = string.Join(" / ", user.RoleCodes),
                EnabledText = user.IsEnabled ? "启用" : "停用",
                LastLoginAtText = user.LastLoginAt.ToString("yyyy-MM-dd HH:mm:ss"),
            }).ToList();

            RolesGrid.ItemsSource = security.Roles.Select(role => new RoleGridRow
            {
                RoleCode = role.RoleCode,
                RoleName = role.RoleName,
                Description = role.Description,
                PermissionCount = role.Permissions.Count,
            }).ToList();

            PermissionsGrid.ItemsSource = security.Permissions.ToList();
        }
        catch (Exception exception)
        {
            if (showErrors)
            {
                MessageBox.Show(this, exception.Message, "权限刷新失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void DrawWorkshopCards(IEnumerable<WorkshopSummaryDto> workshops)
    {
        WorkshopCardsPanel.Children.Clear();
        foreach (var workshop in workshops)
        {
            var accent = workshop.AlarmCount > 0 || workshop.EmergencyCount > 0 ? "#A63B32" : "#1F6AA5";
            var border = new Border
            {
                Width = 280,
                Margin = new Thickness(0, 0, 14, 14),
                Padding = new Thickness(16),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accent)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(10),
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = workshop.WorkshopName,
                FontWeight = FontWeights.Bold,
                FontSize = 16,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#172635")),
            });
            stack.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 0),
                Text = $"设备总数：{workshop.MachineCount}",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#37516B")),
            });
            stack.Children.Add(new TextBlock { Text = $"加工：{workshop.ProcessingCount} | 等待：{workshop.WaitingCount} | 待机：{workshop.StandbyCount}" });
            stack.Children.Add(new TextBlock { Text = $"报警：{workshop.AlarmCount} | 急停：{workshop.EmergencyCount} | 关机：{workshop.PowerOffCount}" });
            stack.Children.Add(new TextBlock { Text = $"通信中断：{workshop.CommunicationInterruptedCount}" });
            border.Child = stack;
            WorkshopCardsPanel.Children.Add(border);
        }
    }

    private void DrawTimelineTotals(IReadOnlyDictionary<string, double> totals)
    {
        TimelineTotalsPanel.Children.Clear();
        foreach (var total in totals)
        {
            var border = new Border
            {
                Width = 190,
                Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(12),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D8E2F0")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
            };
            border.Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = total.Key, FontWeight = FontWeights.SemiBold, Foreground = Brushes.DarkSlateBlue },
                    new TextBlock { Margin = new Thickness(0, 6, 0, 0), Text = $"{total.Value:F2} 分钟", FontSize = 16, FontWeight = FontWeights.Bold },
                },
            };
            TimelineTotalsPanel.Children.Add(border);
        }
    }

    private async Task SaveFormulaAsync(string code, string expression)
    {
        await _apiClient.UpdateFormulaAsync(
            code,
            new FormulaUpdateRequest
            {
                Expression = expression.Trim(),
                UpdatedBy = Environment.UserName,
            });

        await RefreshReportAsync(true);
        MessageBox.Show(this, "公式已保存。", "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static DeviceGridRow ToDeviceGridRow(DeviceDto device)
    {
        return new DeviceGridRow
        {
            DeviceId = device.DeviceId,
            WorkshopName = device.WorkshopName,
            DeviceCode = device.DeviceCode,
            DeviceName = device.DeviceName,
            ControllerModel = device.ControllerModel,
            IpAddress = device.IpAddress,
            Port = device.Port,
            AgentNodeName = device.AgentNodeName,
            MachineOnlineText = device.MachineOnline ? "在线" : "离线",
            StateText = device.CurrentState.ToDisplayName(),
            HealthText = device.HealthLevel switch
            {
                DeviceHealthLevel.Normal => "正常",
                DeviceHealthLevel.Warning => "关注",
                DeviceHealthLevel.Critical => "异常",
                _ => "未知",
            },
            CurrentProgramNo = device.CurrentProgramNo ?? "-",
            SpindleSpeedText = device.SpindleSpeedRpm?.ToString() ?? "-",
            SpindleLoadText = device.SpindleLoadPercent is null ? "-" : $"{device.SpindleLoadPercent:F1}%",
            DataQualityCode = device.DataQualityCode ?? "-",
            LastCollectedAtText = device.LastCollectedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-",
            LastHeartbeatAtText = device.LastHeartbeatAt.ToString("yyyy-MM-dd HH:mm:ss"),
            LastCollectionError = string.IsNullOrWhiteSpace(device.LastCollectionError) ? "-" : device.LastCollectionError,
        };
    }

    private static DailyReportGridRow ToReportGridRow(DailyReportRowDto row)
    {
        return new DailyReportGridRow
        {
            WorkshopName = row.WorkshopName,
            DeviceCode = row.DeviceCode,
            DeviceName = row.DeviceName,
            CurrentStateText = row.CurrentState.ToDisplayName(),
            PowerOnMinutesText = row.PowerOnMinutes.ToString("F2"),
            ProcessingMinutesText = row.ProcessingMinutes.ToString("F2"),
            WaitingMinutesText = row.WaitingMinutes.ToString("F2"),
            StandbyMinutesText = row.StandbyMinutes.ToString("F2"),
            PowerOffMinutesText = row.PowerOffMinutes.ToString("F2"),
            PowerOnRateText = $"{row.PowerOnRate:F2}%",
            UtilizationRateText = $"{row.UtilizationRate:F2}%",
            DataQualityCode = row.DataQualityCode,
        };
    }

    private void UpdateServerStatus(bool online)
    {
        ServerStatusTextBlock.Text = online ? "服务状态：在线" : "服务状态：离线";
        ServerStatusTextBlock.Foreground = online ? Brushes.LightGreen : Brushes.OrangeRed;
    }

    private DeviceDto? GetSelectedDevice()
    {
        if (DevicesGrid.SelectedItem is not DeviceGridRow row)
        {
            return null;
        }

        return _devices.FirstOrDefault(device => device.DeviceId == row.DeviceId);
    }

    private async void RefreshAllButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAllAsync(true);
    }

    private async void RefreshOverviewButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshOverviewAsync(true);
    }

    private async void RefreshReportButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshReportAsync(true);
    }

    private async void RefreshTimelineButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshTimelineAsync(true);
    }

    private async void SavePowerOnFormulaButton_Click(object sender, RoutedEventArgs e)
    {
        await SaveFormulaAsync("power_on_rate", PowerOnFormulaTextBox.Text);
    }

    private async void SaveUtilizationFormulaButton_Click(object sender, RoutedEventArgs e)
    {
        await SaveFormulaAsync("utilization_rate", UtilizationFormulaTextBox.Text);
    }

    private async void AddDeviceButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new DeviceEditorWindow { Owner = this };
        if (window.ShowDialog() != true || window.Request is null)
        {
            return;
        }

        await _apiClient.CreateDeviceAsync(window.Request);
        await RefreshOverviewAsync(true);
    }

    private async void EditDeviceButton_Click(object sender, RoutedEventArgs e)
    {
        var device = GetSelectedDevice();
        if (device is null)
        {
            MessageBox.Show(this, "请先选择要编辑的设备。", "未选择设备", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new DeviceEditorWindow(device) { Owner = this };
        if (window.ShowDialog() != true || window.Request is null)
        {
            return;
        }

        await _apiClient.UpdateDeviceAsync(device.DeviceId, window.Request);
        await RefreshOverviewAsync(true);
    }

    private async void DeleteDeviceButton_Click(object sender, RoutedEventArgs e)
    {
        var device = GetSelectedDevice();
        if (device is null)
        {
            MessageBox.Show(this, "请先选择要删除的设备。", "未选择设备", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this, $"确定删除设备 {device.DeviceCode} 吗？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        await _apiClient.DeleteDeviceAsync(device.DeviceId);
        await RefreshOverviewAsync(true);
    }

    private sealed class DeviceGridRow
    {
        public Guid DeviceId { get; init; }
        public required string WorkshopName { get; init; }
        public required string DeviceCode { get; init; }
        public required string DeviceName { get; init; }
        public required string ControllerModel { get; init; }
        public required string IpAddress { get; init; }
        public int Port { get; init; }
        public required string AgentNodeName { get; init; }
        public required string MachineOnlineText { get; init; }
        public required string StateText { get; init; }
        public required string HealthText { get; init; }
        public required string CurrentProgramNo { get; init; }
        public required string SpindleSpeedText { get; init; }
        public required string SpindleLoadText { get; init; }
        public required string DataQualityCode { get; init; }
        public required string LastCollectedAtText { get; init; }
        public required string LastHeartbeatAtText { get; init; }
        public required string LastCollectionError { get; init; }
    }

    private sealed class DailyReportGridRow
    {
        public required string WorkshopName { get; init; }
        public required string DeviceCode { get; init; }
        public required string DeviceName { get; init; }
        public required string CurrentStateText { get; init; }
        public required string PowerOnMinutesText { get; init; }
        public required string ProcessingMinutesText { get; init; }
        public required string WaitingMinutesText { get; init; }
        public required string StandbyMinutesText { get; init; }
        public required string PowerOffMinutesText { get; init; }
        public required string PowerOnRateText { get; init; }
        public required string UtilizationRateText { get; init; }
        public required string DataQualityCode { get; init; }
    }

    private sealed class TimelineGridRow
    {
        public required string StateText { get; init; }
        public required string StartAtText { get; init; }
        public required string EndAtText { get; init; }
        public required string DurationMinutesText { get; init; }
        public required string DataQualityCode { get; init; }
    }

    private sealed class TimelineDeviceItem
    {
        public Guid DeviceId { get; init; }
        public required string DisplayText { get; init; }
    }

    private sealed class UserGridRow
    {
        public required string UserName { get; init; }
        public required string DisplayName { get; init; }
        public required string Department { get; init; }
        public required string RoleCodesText { get; init; }
        public required string EnabledText { get; init; }
        public required string LastLoginAtText { get; init; }
    }

    private sealed class RoleGridRow
    {
        public required string RoleCode { get; init; }
        public required string RoleName { get; init; }
        public required string Description { get; init; }
        public int PermissionCount { get; init; }
    }
}
