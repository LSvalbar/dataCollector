using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DataCollector.Contracts;
using DataCollector.Desktop.Wpf.Services;
using System.Globalization;

namespace DataCollector.Desktop.Wpf;

public partial class MainWindow : Window
{
    private readonly EnterpriseApiClient _apiClient = new();
    private readonly DispatcherTimer _autoRefreshTimer;
    private readonly Brush _whiteBrush = Brushes.White;
    private List<DeviceDto> _devices = [];
    private string _treeSignature = string.Empty;
    private ScopeNodeType _selectedScopeType = ScopeNodeType.All;
    private HashSet<string> _selectedScopeKeys = new(StringComparer.OrdinalIgnoreCase) { "ALL" };
    private bool _autoRefreshInProgress;
    private DeviceStatusWindow? _deviceStatusWindow;
    private OrganizationTreeNode? _treeContextNode;
    private SecurityOverviewDto? _securityOverview;
    private Guid? _deviceGridContextDeviceId;
    private string? _userGridContextCode;
    private string? _roleGridContextCode;
    private List<FormulaVariableOptionDto> _formulaOptions = [];
    private HashSet<string> _formulaVisibleOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "开机时间",
        "加工时间",
        "待机时间",
        "关机时间",
    };
    private FormulaSelection _powerOnFormulaSelection = new("开机时间", 10d, 1d);
    private FormulaSelection _utilizationFormulaSelection = new("加工时间", 10d, 1d);

    public MainWindow()
    {
        InitializeComponent();
        WindowLayoutHelper.EnableResponsiveSizing(this, maximizeWhenConstrained: true);
        ReportFromDatePicker.SelectedDate = DateTime.Today;
        ReportToDatePicker.SelectedDate = DateTime.Today;
        ReportAllDevicesCheckBox.IsChecked = true;
        TimelineDatePicker.SelectedDate = DateTime.Today;

        _autoRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;

        Loaded += async (_, _) =>
        {
            _autoRefreshTimer.Start();
            await RefreshAllAsync(true);
        };
        Closed += (_, _) =>
        {
            _autoRefreshTimer.Stop();
            _deviceStatusWindow?.Close();
            _apiClient.Dispose();
        };
    }

    private async void AutoRefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (_autoRefreshInProgress)
        {
            return;
        }

        _autoRefreshInProgress = true;
        try
        {
            await RefreshOverviewAsync(false);
            if (TimelineTab.IsSelected && TimelineDatePicker.SelectedDate?.Date == DateTime.Today)
            {
                await RefreshTimelineAsync(false);
            }
        }
        finally
        {
            _autoRefreshInProgress = false;
        }
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
                        "未连接到正式服务端，请先启动服务端 API。",
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

            UpdateServerStatus(true);
            LastOverviewRefreshTextBlock.Text = $"最近刷新：{overview.SnapshotAt:yyyy-MM-dd HH:mm:ss}";
            _devices = overview.Devices
                .OrderBy(device => device.DepartmentCode)
                .ThenBy(device => device.WorkshopCode)
                .ThenBy(device => device.DeviceCode)
                .ToList();

            EnsureTreeStructure();
            ApplyScopeToView(overview.SnapshotAt);
            RefreshTimelineDeviceList();
        }
        catch (Exception exception)
        {
            UpdateServerStatus(false);
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
            var reportDateFrom = DateOnly.FromDateTime(ReportFromDatePicker.SelectedDate ?? DateTime.Today);
            var reportDateTo = DateOnly.FromDateTime(ReportToDatePicker.SelectedDate ?? reportDateFrom.ToDateTime(TimeOnly.MinValue));
            var includeAllDevices = ReportAllDevicesCheckBox.IsChecked == true;
            Guid? selectedDeviceId = includeAllDevices ? null : ReportDeviceComboBox.SelectedValue as Guid?;
            if (!includeAllDevices && selectedDeviceId is null && ReportDeviceComboBox.SelectedValue is Guid comboDeviceId)
            {
                selectedDeviceId = comboDeviceId;
            }

            var response = await _apiClient.GetDailyReportAsync(reportDateFrom, reportDateTo, selectedDeviceId, includeAllDevices);
            if (response is null)
            {
                return;
            }

            _formulaOptions = (await _apiClient.GetFormulaOptionsAsync() ?? [])
                .OrderBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var powerOnFormula = response.Formulas.FirstOrDefault(formula => formula.Code == "power_on_rate");
            var utilizationFormula = response.Formulas.FirstOrDefault(formula => formula.Code == "utilization_rate");
            _powerOnFormulaSelection = ParseFormulaSelection(powerOnFormula, "开机时间");
            _utilizationFormulaSelection = ParseFormulaSelection(utilizationFormula, "加工时间");
            _formulaVisibleOptions = BuildVisibleOptionSet(powerOnFormula, utilizationFormula);
            BindFormulaSelections();
            var scopeText = response.IncludeAllDevices
                ? $"全部设备，共 {response.Rows.Count} 台"
                : response.Rows.FirstOrDefault() is { } firstRow
                    ? $"{firstRow.DeviceCode} / {firstRow.DeviceName}"
                    : "未选择设备";
            ReportSummaryTextBlock.Text = $"统计区间：{response.ReportDateFrom:yyyy-MM-dd} 至 {response.ReportDateTo:yyyy-MM-dd} | {scopeText} | 快照 {response.SnapshotAt:yyyy-MM-dd HH:mm:ss}";
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
                ProgramNoText = string.IsNullOrWhiteSpace(segment.ProgramNo) ? "-" : segment.ProgramNo,
                DrawingNumberText = string.IsNullOrWhiteSpace(segment.DrawingNumber) ? "-" : segment.DrawingNumber,
                DurationSecondsText = $"{Math.Max(0, segment.DurationSeconds)} 秒",
                AlarmNumberText = segment.AlarmNumber?.ToString() ?? "-",
                AlarmMessage = string.IsNullOrWhiteSpace(segment.AlarmMessage) ? "-" : segment.AlarmMessage,
                DataQualityText = TranslateDataQuality(segment.DataQualityCode),
                StateBackground = GetStateBackground(segment.State),
                StateForeground = GetStateForeground(segment.State),
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

            _securityOverview = security;
            UsersGrid.ItemsSource = security.Users.Select(user => new UserGridRow
            {
                UserCode = user.UserCode,
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

    private void EnsureTreeStructure()
    {
        var newSignature = string.Join(
            "|",
            _devices.Select(device =>
                $"{device.DepartmentCode}:{device.DepartmentName}:{device.WorkshopCode}:{device.WorkshopName}:{device.DeviceId}:{device.DeviceCode}:{device.DeviceName}"));

        if (newSignature == _treeSignature)
        {
            return;
        }

        _treeSignature = newSignature;
        DeviceTreeView.ItemsSource = _devices
            .GroupBy(device => device.DepartmentName.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(departmentGroup => new OrganizationTreeNode
            {
                NodeType = ScopeNodeType.Department,
                ScopeKey = departmentGroup.Select(device => device.DepartmentCode).FirstOrDefault() ?? departmentGroup.Key,
                ScopeKeys = departmentGroup.Select(device => device.DepartmentCode).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                Title = $"{departmentGroup.Key} ({departmentGroup.Count()})",
                Subtitle = $"{string.Join("、", departmentGroup.Select(device => device.DepartmentCode).Distinct(StringComparer.OrdinalIgnoreCase).Take(3))} | {departmentGroup.Count()} 台机床",
                Children = departmentGroup
                    .GroupBy(device => device.WorkshopName.Trim(), StringComparer.OrdinalIgnoreCase)
                    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(workshopGroup => new OrganizationTreeNode
                    {
                        NodeType = ScopeNodeType.Workshop,
                        ScopeKey = workshopGroup.Select(device => device.WorkshopCode).FirstOrDefault() ?? workshopGroup.Key,
                        ScopeKeys = workshopGroup.Select(device => device.WorkshopCode).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                        Title = $"{workshopGroup.Key} ({workshopGroup.Count()})",
                        Subtitle = $"{string.Join("、", workshopGroup.Select(device => device.WorkshopCode).Distinct(StringComparer.OrdinalIgnoreCase).Take(3))} | {workshopGroup.Count()} 台机床",
                        Children = workshopGroup
                            .OrderBy(device => device.DeviceCode)
                            .Select(device => new OrganizationTreeNode
                            {
                                NodeType = ScopeNodeType.Device,
                                ScopeKey = device.DeviceId.ToString(),
                                ScopeKeys = [device.DeviceId.ToString()],
                                DeviceId = device.DeviceId,
                                Title = $"{device.DeviceCode} - {device.DeviceName}",
                                Subtitle = $"{device.ControllerModel} | {device.IpAddress}:{device.Port}",
                            })
                            .ToList(),
                    })
                    .ToList(),
            })
            .ToList();
    }

    private void ApplyScopeToView(DateTimeOffset snapshotAt)
    {
        var filteredDevices = GetFilteredDevices();
        var scopeLabel = ResolveScopeLabel(filteredDevices);
        ScopeSummaryTextBlock.Text = scopeLabel;
        OverviewSummaryTextBlock.Text = $"共 {filteredDevices.Count} 台设备 | 快照时间 {snapshotAt:yyyy-MM-dd HH:mm:ss} | 自动刷新 1 秒";
        DrawScopeCards(filteredDevices);

        var previousSelectedId = (DevicesGrid.SelectedItem as DeviceGridRow)?.DeviceId;
        var rows = filteredDevices.Select(ToDeviceGridRow).ToList();
        DevicesGrid.ItemsSource = rows;
        if (previousSelectedId.HasValue)
        {
            DevicesGrid.SelectedItem = rows.FirstOrDefault(row => row.DeviceId == previousSelectedId.Value);
        }
    }

    private void DrawScopeCards(IReadOnlyList<DeviceDto> devices)
    {
        ScopeCardsPanel.Children.Clear();

        var cards = new[]
        {
            CreateMetricCard("设备总数", devices.Count.ToString(), "当前范围"),
            CreateMetricCard("加工中", devices.Count(device => device.CurrentState == MachineOperationalState.Processing).ToString(), "绿色"),
            CreateMetricCard("待机", devices.Count(device => device.CurrentState is MachineOperationalState.Waiting or MachineOperationalState.Standby).ToString(), "黄色"),
            CreateMetricCard("关机", devices.Count(device => device.CurrentState == MachineOperationalState.PowerOff).ToString(), "灰色"),
            CreateMetricCard("异常", devices.Count(device => device.CurrentState is MachineOperationalState.Alarm or MachineOperationalState.Emergency or MachineOperationalState.CommunicationInterrupted).ToString(), "红色"),
        };

        foreach (var card in cards)
        {
            ScopeCardsPanel.Children.Add(card);
        }
    }

    private Border CreateMetricCard(string title, string value, string hint)
    {
        var border = new Border
        {
            Width = 166,
            Margin = new Thickness(0, 0, 10, 10),
            Padding = new Thickness(10),
            Background = _whiteBrush,
            BorderBrush = CreateBrush("#C8C6C4"),
            BorderThickness = new Thickness(1),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var indicator = new Rectangle
        {
            Width = 8,
            Fill = title switch
            {
                "加工中" => CreateBrush("#107C10"),
                "等待中" or "待机" => CreateBrush("#986F0B"),
                "关机" => CreateBrush("#605E5C"),
                "异常" => CreateBrush("#D13438"),
                _ => CreateBrush("#0078D4"),
            },
        };
        Grid.SetColumn(indicator, 0);
        grid.Children.Add(indicator);

        var stack = new StackPanel
        {
            Margin = new Thickness(10, 0, 0, 0),
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    Foreground = CreateBrush("#605E5C"),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                },
                new TextBlock
                {
                    Margin = new Thickness(0, 6, 0, 0),
                    Text = value,
                    Foreground = CreateBrush("#201F1E"),
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                },
                new TextBlock
                {
                    Margin = new Thickness(0, 4, 0, 0),
                    Text = hint,
                    FontSize = 11,
                    Foreground = CreateBrush("#605E5C"),
                },
            },
        };
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);

        border.Child = grid;
        return border;
    }

    private void DrawTimelineTotals(IReadOnlyDictionary<string, double> totals)
    {
        TimelineTotalsPanel.Children.Clear();
        foreach (var total in totals)
        {
            var border = new Border
            {
                Width = 190,
                Margin = new Thickness(0, 0, 12, 12),
                Padding = new Thickness(12),
                Background = _whiteBrush,
                BorderBrush = CreateBrush("#C8C6C4"),
                BorderThickness = new Thickness(1),
            };

            border.Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = total.Key,
                        Foreground = CreateBrush("#605E5C"),
                        FontWeight = FontWeights.SemiBold,
                    },
                    new TextBlock
                    {
                        Margin = new Thickness(0, 8, 0, 0),
                        Text = DurationDisplayFormatter.FormatFromMinutes(total.Value),
                        FontSize = 20,
                        FontWeight = FontWeights.Bold,
                        Foreground = CreateBrush("#201F1E"),
                    },
                },
            };
            TimelineTotalsPanel.Children.Add(border);
        }
    }

    private void RefreshTimelineDeviceList()
    {
        var selectedDeviceId = TimelineDeviceComboBox.SelectedValue is Guid deviceId ? deviceId : (Guid?)null;
        var selectedReportDeviceId = ReportDeviceComboBox.SelectedValue is Guid reportDeviceId ? reportDeviceId : (Guid?)null;
        var items = _devices
            .OrderBy(device => device.WorkshopCode)
            .ThenBy(device => device.DeviceCode)
            .Select(device => new TimelineDeviceItem
            {
                DeviceId = device.DeviceId,
                DisplayText = $"{device.DepartmentName} / {device.WorkshopName} / {device.DeviceCode}",
            })
            .ToList();

        TimelineDeviceComboBox.ItemsSource = items;
        if (selectedDeviceId.HasValue && items.Any(item => item.DeviceId == selectedDeviceId.Value))
        {
            TimelineDeviceComboBox.SelectedValue = selectedDeviceId.Value;
        }
        else
        {
            TimelineDeviceComboBox.SelectedIndex = items.Count > 0 ? 0 : -1;
        }

        ReportDeviceComboBox.ItemsSource = items;
        if (selectedReportDeviceId.HasValue && items.Any(item => item.DeviceId == selectedReportDeviceId.Value))
        {
            ReportDeviceComboBox.SelectedValue = selectedReportDeviceId.Value;
        }
        else
        {
            ReportDeviceComboBox.SelectedIndex = items.Count > 0 ? 0 : -1;
        }

        ReportDeviceComboBox.IsEnabled = ReportAllDevicesCheckBox.IsChecked != true;
    }

    private IReadOnlyList<DeviceDto> GetFilteredDevices()
    {
        return _devices.Where(MatchesSelectedScope).ToList();
    }

    private bool MatchesSelectedScope(DeviceDto device)
    {
        return _selectedScopeType switch
        {
            ScopeNodeType.Department => _selectedScopeKeys.Contains(device.DepartmentCode),
            ScopeNodeType.Workshop => _selectedScopeKeys.Contains(device.WorkshopCode),
            ScopeNodeType.Device => _selectedScopeKeys.Contains(device.DeviceId.ToString()),
            _ => true,
        };
    }

    private string ResolveScopeLabel(IReadOnlyList<DeviceDto> filteredDevices)
    {
        if (_selectedScopeType == ScopeNodeType.Device)
        {
            var device = filteredDevices.FirstOrDefault();
            return device is null ? "设备详情" : $"{device.DepartmentName} / {device.WorkshopName} / {device.DeviceName}";
        }

        if (_selectedScopeType == ScopeNodeType.Workshop)
        {
            var device = filteredDevices.FirstOrDefault();
            return device is null ? "车间设备" : $"{device.DepartmentName} / {device.WorkshopName}";
        }

        if (_selectedScopeType == ScopeNodeType.Department)
        {
            var device = filteredDevices.FirstOrDefault();
            return device is null ? "部门设备" : device.DepartmentName;
        }

        return "全部设备";
    }

    private async Task SaveFormulaAsync(string code, string expression)
    {
        try
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
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "公式校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BindFormulaSelections()
    {
        var visibleOptions = GetVisibleFormulaOptions();

        PowerOnMetricComboBox.ItemsSource = visibleOptions;
        UtilizationMetricComboBox.ItemsSource = visibleOptions;

        PowerOnMetricComboBox.SelectedValue = _powerOnFormulaSelection.PrimaryVariable;
        UtilizationMetricComboBox.SelectedValue = _utilizationFormulaSelection.PrimaryVariable;

        PowerOnStandardHoursTextBlock.Text = $"{_powerOnFormulaSelection.StandardWorkHours:0.##} 小时";
        PowerOnCoefficientTextBlock.Text = _powerOnFormulaSelection.Coefficient.ToString("0.##", CultureInfo.InvariantCulture);
        UtilizationStandardHoursTextBlock.Text = $"{_utilizationFormulaSelection.StandardWorkHours:0.##} 小时";
        UtilizationCoefficientTextBlock.Text = _utilizationFormulaSelection.Coefficient.ToString("0.##", CultureInfo.InvariantCulture);

        PowerOnFormulaPreviewTextBlock.Text = $"当前公式：{BuildFormulaPreview(_powerOnFormulaSelection)}";
        UtilizationFormulaPreviewTextBlock.Text = $"当前公式：{BuildFormulaPreview(_utilizationFormulaSelection)}";
    }

    private IReadOnlyList<FormulaVariableOptionDto> GetVisibleFormulaOptions()
    {
        return _formulaOptions
            .Where(option => _formulaVisibleOptions.Contains(option.VariableName))
            .OrderBy(option =>
            {
                return option.VariableName switch
                {
                    "开机时间" => 0,
                    "加工时间" => 1,
                    "待机时间" => 2,
                    "关机时间" => 3,
                    _ => 10,
                };
            })
            .ThenBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static FormulaSelection ParseFormulaSelection(FormulaDefinitionDto? formula, string defaultVariable)
    {
        if (formula is not null &&
            !string.IsNullOrWhiteSpace(formula.PrimaryVariable) &&
            formula.StandardWorkHours > 0 &&
            formula.Coefficient > 0)
        {
            return new FormulaSelection(formula.PrimaryVariable, formula.StandardWorkHours, formula.Coefficient);
        }

        return new FormulaSelection(defaultVariable, 10d, 1d);
    }

    private static string BuildFormulaExpression(FormulaSelection selection)
    {
        return $"(({selection.PrimaryVariable} / ({selection.StandardWorkHours.ToString("0.####", CultureInfo.InvariantCulture)} * 60)) * {selection.Coefficient.ToString("0.####", CultureInfo.InvariantCulture)})";
    }

    private static string BuildFormulaPreview(FormulaSelection selection)
    {
        return $"{selection.PrimaryVariable} / 制式工时({selection.StandardWorkHours:0.##}小时) × 系数({selection.Coefficient:0.##})";
    }

    private static HashSet<string> BuildVisibleOptionSet(params FormulaDefinitionDto?[] formulas)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "开机时间",
            "加工时间",
            "待机时间",
            "关机时间",
        };

        foreach (var formula in formulas.Where(item => item is not null))
        {
            foreach (var option in formula!.VisibleOptions)
            {
                result.Add(option);
            }

            if (!string.IsNullOrWhiteSpace(formula.PrimaryVariable))
            {
                result.Add(formula.PrimaryVariable);
            }
        }

        return result;
    }

    private void UpdateServerStatus(bool online)
    {
        ServerStatusTextBlock.Text = online ? "服务状态：在线" : "服务状态：离线";
        ServerStatusTextBlock.Foreground = online ? CreateBrush("#107C10") : CreateBrush("#D13438");
    }

    private async Task OpenAddDeviceDialogAsync()
    {
        var window = new DeviceEditorWindow(GetAgentNodeOptions(), BuildDefaultRequest()) { Owner = this };
        if (window.ShowDialog() != true || window.Request is null)
        {
            return;
        }

        try
        {
            await _apiClient.CreateDeviceAsync(window.Request);
            await RefreshOverviewAsync(true);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "添加设备失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RenameTreeNodeAsync()
    {
        if (_treeContextNode is null)
        {
            MessageBox.Show(this, "请先在树菜单中选择要重命名的层级。", "未选择层级", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var currentName = _treeContextNode.NodeType switch
        {
            ScopeNodeType.Device => ResolveDeviceName(_treeContextNode.DeviceId),
            _ => ResolveNodeDisplayName(_treeContextNode.Title),
        };

        var window = new RenameNodeWindow(currentName, ResolveRenameTitle(_treeContextNode.NodeType)) { Owner = this };
        if (window.ShowDialog() != true)
        {
            return;
        }

        try
        {
            switch (_treeContextNode.NodeType)
            {
                case ScopeNodeType.Department:
                    foreach (var departmentCode in _treeContextNode.ScopeKeys)
                    {
                        await _apiClient.RenameDepartmentAsync(departmentCode, window.NodeName);
                    }
                    break;
                case ScopeNodeType.Workshop:
                    foreach (var workshopCode in _treeContextNode.ScopeKeys)
                    {
                        await _apiClient.RenameWorkshopAsync(workshopCode, window.NodeName);
                    }
                    break;
                case ScopeNodeType.Device when _treeContextNode.DeviceId.HasValue:
                    await _apiClient.RenameDeviceAsync(_treeContextNode.DeviceId.Value, window.NodeName);
                    break;
                default:
                    return;
            }

            await RefreshOverviewAsync(true);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "重命名失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string ResolveRenameTitle(ScopeNodeType nodeType) =>
        nodeType switch
        {
            ScopeNodeType.Department => "重命名部门",
            ScopeNodeType.Workshop => "重命名车间",
            ScopeNodeType.Device => "重命名设备",
            _ => "重命名",
        };

    private string ResolveDeviceName(Guid? deviceId)
    {
        if (!deviceId.HasValue)
        {
            return string.Empty;
        }

        return _devices.FirstOrDefault(device => device.DeviceId == deviceId.Value)?.DeviceName ?? string.Empty;
    }

    private static string ResolveNodeDisplayName(string title)
    {
        var suffixIndex = title.LastIndexOf(" (", StringComparison.Ordinal);
        return suffixIndex > 0 ? title[..suffixIndex] : title;
    }

    private DeviceUpsertRequest BuildDefaultRequest()
    {
        var request = new DeviceUpsertRequest
        {
            DepartmentCode = string.Empty,
            DepartmentName = string.Empty,
            WorkshopCode = string.Empty,
            WorkshopName = string.Empty,
            DeviceCode = string.Empty,
            DeviceName = string.Empty,
            Manufacturer = string.Empty,
            ControllerModel = "FANUC Series 0i-TF",
            ProtocolName = "FOCAS over Ethernet",
            IpAddress = string.Empty,
            Port = 8193,
            AgentNodeName = GetDefaultAgentNodeName(),
            ResponsiblePerson = string.Empty,
            IsEnabled = true,
        };

        var sourceNode = _treeContextNode ?? DeviceTreeView.SelectedItem as OrganizationTreeNode;
        if (sourceNode is null)
        {
            return request;
        }

        if (sourceNode.NodeType == ScopeNodeType.Device && sourceNode.DeviceId.HasValue)
        {
            var device = _devices.FirstOrDefault(item => item.DeviceId == sourceNode.DeviceId.Value);
            if (device is not null)
            {
                request.DepartmentCode = device.DepartmentCode;
                request.DepartmentName = device.DepartmentName;
                request.WorkshopCode = device.WorkshopCode;
                request.WorkshopName = device.WorkshopName;
                request.AgentNodeName = device.AgentNodeName;
                request.ResponsiblePerson = device.ResponsiblePerson;
            }

            return request;
        }

        if (sourceNode.NodeType == ScopeNodeType.Workshop)
        {
            var device = _devices.FirstOrDefault(item => sourceNode.ScopeKeys.Contains(item.WorkshopCode, StringComparer.OrdinalIgnoreCase));
            if (device is not null)
            {
                request.DepartmentCode = device.DepartmentCode;
                request.DepartmentName = device.DepartmentName;
                request.WorkshopCode = device.WorkshopCode;
                request.WorkshopName = device.WorkshopName;
                request.AgentNodeName = device.AgentNodeName;
                request.ResponsiblePerson = device.ResponsiblePerson;
            }

            return request;
        }

        if (sourceNode.NodeType == ScopeNodeType.Department)
        {
            var device = _devices.FirstOrDefault(item => sourceNode.ScopeKeys.Contains(item.DepartmentCode, StringComparer.OrdinalIgnoreCase));
            request.DepartmentCode = device?.DepartmentCode ?? sourceNode.ScopeKey;
            request.DepartmentName = device?.DepartmentName ?? ResolveNodeDisplayName(sourceNode.Title);
        }

        return request;
    }

    private IReadOnlyList<string> GetAgentNodeOptions()
    {
        return _devices
            .Select(device => device.AgentNodeName)
            .Append(GetDefaultAgentNodeName())
            .Where(agentNode => !string.IsNullOrWhiteSpace(agentNode))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(agentNode => agentNode, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetDefaultAgentNodeName()
    {
        return Environment.GetEnvironmentVariable("DATACOLLECTOR_AGENT_NODE")?.Trim() ?? string.Empty;
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T target)
            {
                return target;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private UserDto? GetSelectedUser()
    {
        if (_securityOverview is null)
        {
            return null;
        }

        if (UsersGrid.SelectedItem is UserGridRow row)
        {
            return _securityOverview.Users.FirstOrDefault(user => user.UserCode == row.UserCode);
        }

        if (!string.IsNullOrWhiteSpace(_userGridContextCode))
        {
            return _securityOverview.Users.FirstOrDefault(user => user.UserCode == _userGridContextCode);
        }

        return null;
    }

    private RoleDto? GetSelectedRole()
    {
        if (_securityOverview is null)
        {
            return null;
        }

        if (RolesGrid.SelectedItem is RoleGridRow row)
        {
            return _securityOverview.Roles.FirstOrDefault(role => role.RoleCode == row.RoleCode);
        }

        if (!string.IsNullOrWhiteSpace(_roleGridContextCode))
        {
            return _securityOverview.Roles.FirstOrDefault(role => role.RoleCode == _roleGridContextCode);
        }

        return null;
    }

    private async Task OpenUserEditorAsync(UserDto? user = null)
    {
        if (_securityOverview is null)
        {
            await RefreshSecurityAsync(true);
        }

        if (_securityOverview is null)
        {
            return;
        }

        var window = new UserEditorWindow(_securityOverview.Roles, user) { Owner = this };
        if (window.ShowDialog() != true || window.Request is null)
        {
            return;
        }

        try
        {
            await _apiClient.SaveUserAsync(window.Request);
            await RefreshSecurityAsync(true);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "用户保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task OpenRoleEditorAsync(RoleDto? role = null)
    {
        if (_securityOverview is null)
        {
            await RefreshSecurityAsync(true);
        }

        if (_securityOverview is null)
        {
            return;
        }

        var window = new RoleEditorWindow(_securityOverview.Permissions, role) { Owner = this };
        if (window.ShowDialog() != true || window.Request is null)
        {
            return;
        }

        try
        {
            await _apiClient.SaveRoleAsync(window.Request);
            await RefreshSecurityAsync(true);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "角色保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private DeviceDto? GetSelectedDevice()
    {
        if (DevicesGrid.SelectedItem is DeviceGridRow row)
        {
            return _devices.FirstOrDefault(device => device.DeviceId == row.DeviceId);
        }

        if (DeviceTreeView.SelectedItem is OrganizationTreeNode node && node.DeviceId.HasValue)
        {
            return _devices.FirstOrDefault(device => device.DeviceId == node.DeviceId.Value);
        }

        return null;
    }

    private void OpenDeviceStatus(DeviceDto device)
    {
        if (_deviceStatusWindow is not null)
        {
            _deviceStatusWindow.Close();
        }

        _deviceStatusWindow = new DeviceStatusWindow(
            device.DeviceId,
            () => _devices.FirstOrDefault(item => item.DeviceId == device.DeviceId))
        {
            Owner = this,
        };
        _deviceStatusWindow.Closed += (_, _) => _deviceStatusWindow = null;
        _deviceStatusWindow.Show();
    }

    private static DeviceGridRow ToDeviceGridRow(DeviceDto device)
    {
        var stateBackground = GetStateBackground(device.CurrentState);
        var healthBackground = GetHealthBackground(device.HealthLevel);
        var onlineBackground = device.MachineOnline ? CreateBrush("#DFF6DD") : CreateBrush("#EDEBE9");

        return new DeviceGridRow
        {
            DeviceId = device.DeviceId,
            DepartmentName = device.DepartmentName,
            WorkshopName = device.WorkshopName,
            DeviceCode = device.DeviceCode,
            DeviceName = device.DeviceName,
            ControllerModel = device.ControllerModel,
            IpAddress = device.IpAddress,
            MachineOnlineText = device.MachineOnline ? "在线" : "离线",
            OnlineBackground = onlineBackground,
            OnlineForeground = device.MachineOnline ? CreateBrush("#0B6A0B") : CreateBrush("#605E5C"),
            StateText = device.CurrentState.ToDisplayName(),
            StateBackground = stateBackground,
            StateForeground = GetStateForeground(device.CurrentState),
            HealthText = device.HealthLevel switch
            {
                DeviceHealthLevel.Normal => "正常",
                DeviceHealthLevel.Warning => "关注",
                DeviceHealthLevel.Critical => "异常",
                _ => "未知",
            },
            HealthBackground = healthBackground,
            HealthForeground = device.HealthLevel switch
            {
                DeviceHealthLevel.Normal => CreateBrush("#0B6A0B"),
                DeviceHealthLevel.Warning => CreateBrush("#8A6A00"),
                DeviceHealthLevel.Critical => CreateBrush("#A4262C"),
                _ => CreateBrush("#605E5C"),
            },
            CurrentProgramNo = device.CurrentProgramNo ?? "-",
            CurrentDrawingNumber = string.IsNullOrWhiteSpace(device.CurrentDrawingNumber) ? "-" : device.CurrentDrawingNumber,
            SpindleSpeedText = device.SpindleSpeedRpm is null ? "-" : $"{device.SpindleSpeedRpm} rpm",
            SpindleLoadText = device.SpindleLoadPercent is null ? "-" : $"{device.SpindleLoadPercent:F1}%",
            DataQualityText = TranslateDataQuality(device.DataQualityCode),
            LastCollectedAtText = device.LastCollectedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-",
            LastHeartbeatAtText = device.LastHeartbeatAt.ToString("yyyy-MM-dd HH:mm:ss"),
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
            PowerOnMinutesText = DurationDisplayFormatter.FormatFromMinutes(row.PowerOnMinutes),
            ProcessingMinutesText = DurationDisplayFormatter.FormatFromMinutes(row.ProcessingMinutes),
            StandbyMinutesText = DurationDisplayFormatter.FormatFromMinutes(row.StandbyMinutes),
            PowerOffMinutesText = DurationDisplayFormatter.FormatFromMinutes(row.PowerOffMinutes),
            AlarmMinutesText = DurationDisplayFormatter.FormatFromMinutes(row.AlarmMinutes),
            PowerOnRateText = $"{(row.PowerOnRate * 100d).ToString("F2", CultureInfo.InvariantCulture)}%",
            UtilizationRateText = $"{(row.UtilizationRate * 100d).ToString("F2", CultureInfo.InvariantCulture)}%",
            DataQualityText = TranslateDataQuality(row.DataQualityCode),
        };
    }

    private static Brush GetStateBackground(MachineOperationalState state) =>
        state switch
        {
            MachineOperationalState.Processing => CreateBrush("#DFF6DD"),
            MachineOperationalState.Waiting => CreateBrush("#FFF4CE"),
            MachineOperationalState.Standby => CreateBrush("#FFF4CE"),
            MachineOperationalState.PowerOff => CreateBrush("#EDEBE9"),
            MachineOperationalState.Alarm => CreateBrush("#FDE7E9"),
            MachineOperationalState.Emergency => CreateBrush("#FDE7E9"),
            MachineOperationalState.CommunicationInterrupted => CreateBrush("#E5F1FB"),
            _ => CreateBrush("#EDEBE9"),
        };

    private static Brush GetStateForeground(MachineOperationalState state) =>
        state switch
        {
            MachineOperationalState.Processing => CreateBrush("#0B6A0B"),
            MachineOperationalState.Waiting => CreateBrush("#8A6A00"),
            MachineOperationalState.Standby => CreateBrush("#8A6A00"),
            MachineOperationalState.PowerOff => CreateBrush("#605E5C"),
            MachineOperationalState.Alarm => CreateBrush("#A4262C"),
            MachineOperationalState.Emergency => CreateBrush("#A4262C"),
            MachineOperationalState.CommunicationInterrupted => CreateBrush("#005A9E"),
            _ => CreateBrush("#605E5C"),
        };

    private static Brush GetHealthBackground(DeviceHealthLevel healthLevel) =>
        healthLevel switch
        {
            DeviceHealthLevel.Normal => CreateBrush("#DFF6DD"),
            DeviceHealthLevel.Warning => CreateBrush("#FFF4CE"),
            DeviceHealthLevel.Critical => CreateBrush("#FDE7E9"),
            _ => CreateBrush("#EDEBE9"),
        };

    private static string TranslateDataQuality(string? qualityCode) =>
        qualityCode?.Trim().ToLowerInvariant() switch
        {
            "native_preferred" => "原生优先",
            "focas_realtime" => "实时采集",
            "focas_error" => "采集异常",
            "stale_snapshot" => "快照滞后",
            "manual_disabled" => "手动停用",
            "not_collected" => "未采集",
            "fallback" => "回退计算",
            "estimated" => "估算值",
            "gap" => "数据缺口",
            null or "" => "-",
            _ => qualityCode!,
        };

    private static Brush CreateBrush(string hex)
    {
        return (Brush)new BrushConverter().ConvertFromString(hex)!;
    }

    private async void RefreshAllButton_Click(object sender, RoutedEventArgs e) => await RefreshAllAsync(true);

    private async void RefreshOverviewButton_Click(object sender, RoutedEventArgs e) => await RefreshOverviewAsync(true);

    private async void RefreshReportButton_Click(object sender, RoutedEventArgs e) => await RefreshReportAsync(true);

    private async void RefreshTimelineButton_Click(object sender, RoutedEventArgs e) => await RefreshTimelineAsync(true);

    private void ReportAllDevicesCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        ReportDeviceComboBox.IsEnabled = ReportAllDevicesCheckBox.IsChecked != true;
    }

    private async void ConfigureFormulaButton_Click(object sender, RoutedEventArgs e)
    {
        if (_formulaOptions.Count == 0)
        {
            MessageBox.Show(this, "当前没有可用公式列，请先确认服务端和报表接口正常。", "无法配置公式", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var window = new FormulaConfigWindow(
            _formulaOptions,
            _formulaVisibleOptions,
            _powerOnFormulaSelection,
            _utilizationFormulaSelection)
        {
            Owner = this,
        };
        if (window.ShowDialog() != true || window.PowerOnSelection is null || window.UtilizationSelection is null)
        {
            return;
        }

        try
        {
            await _apiClient.UpdateFormulaAsync(
                "power_on_rate",
                new FormulaUpdateRequest
                {
                    Expression = BuildFormulaExpression(window.PowerOnSelection),
                    PrimaryVariable = window.PowerOnSelection.PrimaryVariable,
                    StandardWorkHours = window.PowerOnSelection.StandardWorkHours,
                    Coefficient = window.PowerOnSelection.Coefficient,
                    VisibleOptions = window.VisibleOptions.ToArray(),
                    UpdatedBy = Environment.UserName,
                });
            await _apiClient.UpdateFormulaAsync(
                "utilization_rate",
                new FormulaUpdateRequest
                {
                    Expression = BuildFormulaExpression(window.UtilizationSelection),
                    PrimaryVariable = window.UtilizationSelection.PrimaryVariable,
                    StandardWorkHours = window.UtilizationSelection.StandardWorkHours,
                    Coefficient = window.UtilizationSelection.Coefficient,
                    VisibleOptions = window.VisibleOptions.ToArray(),
                    UpdatedBy = Environment.UserName,
                });

            _powerOnFormulaSelection = window.PowerOnSelection;
            _utilizationFormulaSelection = window.UtilizationSelection;
            _formulaVisibleOptions = new HashSet<string>(window.VisibleOptions, StringComparer.OrdinalIgnoreCase);
            await RefreshReportAsync(true);
            MessageBox.Show(this, "公式配置已更新。", "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "保存公式失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void AddDeviceButton_Click(object sender, RoutedEventArgs e)
    {
        await OpenAddDeviceDialogAsync();
    }

    private async void EditDeviceButton_Click(object sender, RoutedEventArgs e)
    {
        var device = GetSelectedDevice();
        if (device is null)
        {
            MessageBox.Show(this, "请先选择要编辑的设备。", "未选择设备", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new DeviceEditorWindow(GetAgentNodeOptions(), device) { Owner = this };
        if (window.ShowDialog() != true || window.Request is null)
        {
            return;
        }

        try
        {
            await _apiClient.UpdateDeviceAsync(device.DeviceId, window.Request);
            await RefreshOverviewAsync(true);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "编辑设备失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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

        try
        {
            await _apiClient.DeleteDeviceAsync(device.DeviceId);
            await RefreshOverviewAsync(true);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "删除设备失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DevicesGrid_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is DeviceGridRow deviceRow)
        {
            row.IsSelected = true;
            _deviceGridContextDeviceId = deviceRow.DeviceId;
            return;
        }

        DevicesGrid.SelectedItem = null;
        _deviceGridContextDeviceId = null;
    }

    private void DevicesGridContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var hasSelection = _deviceGridContextDeviceId.HasValue || DevicesGrid.SelectedItem is DeviceGridRow;
        EditDeviceMenuItem.IsEnabled = hasSelection;
        DeleteDeviceMenuItem.IsEnabled = hasSelection;
    }

    private async void RefreshSecurityButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshSecurityAsync(true);
    }

    private void UsersGrid_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is UserGridRow userRow)
        {
            row.IsSelected = true;
            _userGridContextCode = userRow.UserCode;
            return;
        }

        UsersGrid.SelectedItem = null;
        _userGridContextCode = null;
    }

    private void UsersGridContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var hasSelection = GetSelectedUser() is not null;
        EditUserMenuItem.IsEnabled = hasSelection;
        DeleteUserMenuItem.IsEnabled = hasSelection;
    }

    private async void AddUserMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await OpenUserEditorAsync();
    }

    private async void EditUserMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var user = GetSelectedUser();
        if (user is null)
        {
            MessageBox.Show(this, "请先选择要编辑的用户。", "未选择用户", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await OpenUserEditorAsync(user);
    }

    private async void DeleteUserMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var user = GetSelectedUser();
        if (user is null)
        {
            MessageBox.Show(this, "请先选择要删除的用户。", "未选择用户", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this, $"确定删除用户 {user.UserName} 吗？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _apiClient.DeleteUserAsync(user.UserCode);
            await RefreshSecurityAsync(true);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "删除用户失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RolesGrid_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is RoleGridRow roleRow)
        {
            row.IsSelected = true;
            _roleGridContextCode = roleRow.RoleCode;
            return;
        }

        RolesGrid.SelectedItem = null;
        _roleGridContextCode = null;
    }

    private void RolesGridContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var hasSelection = GetSelectedRole() is not null;
        EditRoleMenuItem.IsEnabled = hasSelection;
        DeleteRoleMenuItem.IsEnabled = hasSelection;
    }

    private async void AddRoleMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await OpenRoleEditorAsync();
    }

    private async void EditRoleMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var role = GetSelectedRole();
        if (role is null)
        {
            MessageBox.Show(this, "请先选择要编辑的角色。", "未选择角色", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await OpenRoleEditorAsync(role);
    }

    private async void DeleteRoleMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var role = GetSelectedRole();
        if (role is null)
        {
            MessageBox.Show(this, "请先选择要删除的角色。", "未选择角色", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this, $"确定删除角色 {role.RoleName} 吗？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _apiClient.DeleteRoleAsync(role.RoleCode);
            await RefreshSecurityAsync(true);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "删除角色失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RefreshTreeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await RefreshOverviewAsync(true);
    }

    private async void AddDeviceFromTreeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await OpenAddDeviceDialogAsync();
    }

    private async void RenameTreeNodeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await RenameTreeNodeAsync();
    }

    private void DeviceTreeContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        RenameTreeNodeMenuItem.IsEnabled = _treeContextNode is not null;
        RenameTreeNodeMenuItem.Header = _treeContextNode is null
            ? "重命名"
            : ResolveRenameTitle(_treeContextNode.NodeType);
    }

    private void DeviceTreeView_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        var treeViewItem = FindAncestor<TreeViewItem>(source);
        if (treeViewItem?.DataContext is OrganizationTreeNode node)
        {
            treeViewItem.IsSelected = true;
            _treeContextNode = node;
            return;
        }

        _treeContextNode = null;
    }

    private void DeviceTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not OrganizationTreeNode node)
        {
            _selectedScopeType = ScopeNodeType.All;
            _selectedScopeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ALL" };
            _treeContextNode = null;
        }
        else
        {
            _selectedScopeType = node.NodeType;
            _selectedScopeKeys = new HashSet<string>(node.ScopeKeys, StringComparer.OrdinalIgnoreCase);
            _treeContextNode = node;
        }

        ApplyScopeToView(DateTimeOffset.Now);
    }

    private void DeviceTreeView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DeviceTreeView.SelectedItem is OrganizationTreeNode node && node.NodeType == ScopeNodeType.Device)
        {
            var device = _devices.FirstOrDefault(item => item.DeviceId == node.DeviceId);
            if (device is not null)
            {
                OpenDeviceStatus(device);
            }
        }
    }

    private void DevicesGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var device = GetSelectedDevice();
        if (device is not null)
        {
            OpenDeviceStatus(device);
        }
    }

    private void OpenSelectedDeviceButton_Click(object sender, RoutedEventArgs e)
    {
        var device = GetSelectedDevice();
        if (device is null)
        {
            MessageBox.Show(this, "请先在设备表格或树菜单中选择一台机床。", "未选择设备", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenDeviceStatus(device);
    }

    private sealed class DeviceGridRow
    {
        public Guid DeviceId { get; init; }
        public required string DepartmentName { get; init; }
        public required string WorkshopName { get; init; }
        public required string DeviceCode { get; init; }
        public required string DeviceName { get; init; }
        public required string ControllerModel { get; init; }
        public required string IpAddress { get; init; }
        public required string MachineOnlineText { get; init; }
        public required Brush OnlineBackground { get; init; }
        public required Brush OnlineForeground { get; init; }
        public required string StateText { get; init; }
        public required Brush StateBackground { get; init; }
        public required Brush StateForeground { get; init; }
        public required string HealthText { get; init; }
        public required Brush HealthBackground { get; init; }
        public required Brush HealthForeground { get; init; }
        public required string CurrentProgramNo { get; init; }
        public required string CurrentDrawingNumber { get; init; }
        public required string SpindleSpeedText { get; init; }
        public required string SpindleLoadText { get; init; }
        public required string DataQualityText { get; init; }
        public required string LastCollectedAtText { get; init; }
        public required string LastHeartbeatAtText { get; init; }
    }

    private sealed class DailyReportGridRow
    {
        public required string WorkshopName { get; init; }
        public required string DeviceCode { get; init; }
        public required string DeviceName { get; init; }
        public required string CurrentStateText { get; init; }
        public required string PowerOnMinutesText { get; init; }
        public required string ProcessingMinutesText { get; init; }
        public required string StandbyMinutesText { get; init; }
        public required string PowerOffMinutesText { get; init; }
        public required string AlarmMinutesText { get; init; }
        public required string PowerOnRateText { get; init; }
        public required string UtilizationRateText { get; init; }
        public required string DataQualityText { get; init; }
    }

    private sealed class TimelineGridRow
    {
        public required string StateText { get; init; }
        public required string StartAtText { get; init; }
        public required string EndAtText { get; init; }
        public required string ProgramNoText { get; init; }
        public required string DrawingNumberText { get; init; }
        public required string DurationSecondsText { get; init; }
        public required string AlarmNumberText { get; init; }
        public required string AlarmMessage { get; init; }
        public required string DataQualityText { get; init; }
        public required Brush StateBackground { get; init; }
        public required Brush StateForeground { get; init; }
    }

    private sealed class TimelineDeviceItem
    {
        public Guid DeviceId { get; init; }
        public required string DisplayText { get; init; }
    }

    private sealed class UserGridRow
    {
        public required string UserCode { get; init; }
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

public enum ScopeNodeType
{
    All = 0,
    Department = 1,
    Workshop = 2,
    Device = 3,
}

public sealed class OrganizationTreeNode
{
    public ScopeNodeType NodeType { get; init; }

    public required string ScopeKey { get; init; }

    public IReadOnlyList<string> ScopeKeys { get; init; } = [];

    public Guid? DeviceId { get; init; }

    public required string Title { get; init; }

    public required string Subtitle { get; init; }

    public List<OrganizationTreeNode> Children { get; init; } = [];
}
