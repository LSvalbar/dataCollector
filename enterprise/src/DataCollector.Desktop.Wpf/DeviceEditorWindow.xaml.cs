using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using DataCollector.Contracts;

namespace DataCollector.Desktop.Wpf;

public partial class DeviceEditorWindow : Window
{
    public DeviceEditorWindow(DeviceDto? device = null)
        : this(device, null)
    {
    }

    public DeviceEditorWindow(DeviceUpsertRequest seedRequest)
        : this(null, seedRequest)
    {
    }

    private DeviceEditorWindow(DeviceDto? device, DeviceUpsertRequest? seedRequest)
    {
        InitializeComponent();
        DeviceId = device?.DeviceId;
        DepartmentCodeTextBox.Text = device?.DepartmentCode ?? seedRequest?.DepartmentCode ?? string.Empty;
        DepartmentNameTextBox.Text = device?.DepartmentName ?? seedRequest?.DepartmentName ?? string.Empty;
        WorkshopCodeTextBox.Text = device?.WorkshopCode ?? seedRequest?.WorkshopCode ?? string.Empty;
        WorkshopNameTextBox.Text = device?.WorkshopName ?? seedRequest?.WorkshopName ?? string.Empty;
        DeviceCodeTextBox.Text = device?.DeviceCode ?? seedRequest?.DeviceCode ?? string.Empty;
        DeviceNameTextBox.Text = device?.DeviceName ?? seedRequest?.DeviceName ?? string.Empty;
        SystemVersionTextBox.Text = device?.ControllerModel ?? seedRequest?.ControllerModel ?? "FANUC Series 0i-TF";
        ResponsiblePersonTextBox.Text = device?.ResponsiblePerson ?? seedRequest?.ResponsiblePerson ?? string.Empty;
        IpTextBox.Text = device?.IpAddress ?? seedRequest?.IpAddress ?? string.Empty;
        PortTextBox.Text = device?.Port.ToString() ?? seedRequest?.Port.ToString() ?? "8193";
        AgentNodeTextBox.Text = device?.AgentNodeName ?? seedRequest?.AgentNodeName ?? string.Empty;
        EnabledCheckBox.IsChecked = device?.IsEnabled ?? seedRequest?.IsEnabled ?? true;
    }

    public Guid? DeviceId { get; }

    public DeviceUpsertRequest? Request { get; private set; }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortTextBox.Text, out var port))
        {
            MessageBox.Show(this, "端口必须是数字。", "校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var departmentName = DepartmentNameTextBox.Text.Trim();
        var workshopName = WorkshopNameTextBox.Text.Trim();
        var deviceCode = DeviceCodeTextBox.Text.Trim();
        var deviceName = DeviceNameTextBox.Text.Trim();
        var ipAddress = IpTextBox.Text.Trim();
        var agentNodeName = AgentNodeTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(departmentName) ||
            string.IsNullOrWhiteSpace(workshopName) ||
            string.IsNullOrWhiteSpace(deviceCode) ||
            string.IsNullOrWhiteSpace(deviceName) ||
            string.IsNullOrWhiteSpace(ipAddress) ||
            string.IsNullOrWhiteSpace(agentNodeName))
        {
            MessageBox.Show(
                this,
                "部门名称、车间名称、设备编码、设备名称、IP 地址和 Agent 节点不能为空。",
                "校验失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var departmentCode = string.IsNullOrWhiteSpace(DepartmentCodeTextBox.Text)
            ? BuildCode(departmentName, "D")
            : DepartmentCodeTextBox.Text.Trim();
        var workshopCode = string.IsNullOrWhiteSpace(WorkshopCodeTextBox.Text)
            ? BuildCode($"{departmentName}-{workshopName}", "W")
            : WorkshopCodeTextBox.Text.Trim();

        Request = new DeviceUpsertRequest
        {
            DeviceId = DeviceId,
            DepartmentCode = departmentCode,
            DepartmentName = departmentName,
            WorkshopCode = workshopCode,
            WorkshopName = workshopName,
            DeviceCode = deviceCode,
            DeviceName = deviceName,
            Manufacturer = string.Empty,
            ControllerModel = SystemVersionTextBox.Text.Trim(),
            ProtocolName = "FOCAS over Ethernet",
            IpAddress = ipAddress,
            Port = port,
            AgentNodeName = agentNodeName,
            ResponsiblePerson = ResponsiblePersonTextBox.Text.Trim(),
            IsEnabled = EnabledCheckBox.IsChecked ?? true,
        };

        DialogResult = true;
        Close();
    }

    private static string BuildCode(string source, string prefix)
    {
        var normalized = Regex.Replace(source.ToUpperInvariant(), "[^A-Z0-9]+", string.Empty);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return $"{prefix}{normalized}";
        }

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        var hash = Convert.ToHexString(hashBytes)[..8];
        return $"{prefix}{hash}";
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
