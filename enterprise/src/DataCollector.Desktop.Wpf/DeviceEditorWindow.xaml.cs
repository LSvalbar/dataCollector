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
        ManufacturerTextBox.Text = device?.Manufacturer ?? seedRequest?.Manufacturer ?? "FANUC";
        ControllerModelTextBox.Text = device?.ControllerModel ?? seedRequest?.ControllerModel ?? "FANUC Series 0i-TF";
        ProtocolTextBox.Text = device?.ProtocolName ?? seedRequest?.ProtocolName ?? "FOCAS over Ethernet";
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

        Request = new DeviceUpsertRequest
        {
            DeviceId = DeviceId,
            DepartmentCode = DepartmentCodeTextBox.Text.Trim(),
            DepartmentName = DepartmentNameTextBox.Text.Trim(),
            WorkshopCode = WorkshopCodeTextBox.Text.Trim(),
            WorkshopName = WorkshopNameTextBox.Text.Trim(),
            DeviceCode = DeviceCodeTextBox.Text.Trim(),
            DeviceName = DeviceNameTextBox.Text.Trim(),
            Manufacturer = ManufacturerTextBox.Text.Trim(),
            ControllerModel = ControllerModelTextBox.Text.Trim(),
            ProtocolName = ProtocolTextBox.Text.Trim(),
            IpAddress = IpTextBox.Text.Trim(),
            Port = port,
            AgentNodeName = AgentNodeTextBox.Text.Trim(),
            IsEnabled = EnabledCheckBox.IsChecked ?? true,
        };

        if (string.IsNullOrWhiteSpace(Request.DepartmentCode) ||
            string.IsNullOrWhiteSpace(Request.DepartmentName) ||
            string.IsNullOrWhiteSpace(Request.WorkshopCode) ||
            string.IsNullOrWhiteSpace(Request.WorkshopName) ||
            string.IsNullOrWhiteSpace(Request.DeviceCode) ||
            string.IsNullOrWhiteSpace(Request.DeviceName) ||
            string.IsNullOrWhiteSpace(Request.IpAddress) ||
            string.IsNullOrWhiteSpace(Request.AgentNodeName))
        {
            MessageBox.Show(
                this,
                "部门、车间、设备编码、设备名称、IP 地址和 Agent 节点不能为空。",
                "校验失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
