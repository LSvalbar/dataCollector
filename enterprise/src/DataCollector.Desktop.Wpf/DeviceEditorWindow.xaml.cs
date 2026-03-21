using System.Windows;
using DataCollector.Contracts;

namespace DataCollector.Desktop.Wpf;

public partial class DeviceEditorWindow : Window
{
    public DeviceEditorWindow(DeviceDto? device = null)
    {
        InitializeComponent();
        DeviceId = device?.DeviceId;
        WorkshopCodeTextBox.Text = device?.WorkshopCode ?? string.Empty;
        WorkshopNameTextBox.Text = device?.WorkshopName ?? string.Empty;
        DeviceCodeTextBox.Text = device?.DeviceCode ?? string.Empty;
        DeviceNameTextBox.Text = device?.DeviceName ?? string.Empty;
        ManufacturerTextBox.Text = device?.Manufacturer ?? "FANUC 车削设备";
        ControllerModelTextBox.Text = device?.ControllerModel ?? "FANUC Series 0i-TF";
        ProtocolTextBox.Text = device?.ProtocolName ?? "FOCAS over Ethernet";
        IpTextBox.Text = device?.IpAddress ?? string.Empty;
        PortTextBox.Text = device?.Port.ToString() ?? "8193";
        AgentNodeTextBox.Text = device?.AgentNodeName ?? string.Empty;
        EnabledCheckBox.IsChecked = device?.IsEnabled ?? true;
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

        if (string.IsNullOrWhiteSpace(Request.WorkshopCode) ||
            string.IsNullOrWhiteSpace(Request.WorkshopName) ||
            string.IsNullOrWhiteSpace(Request.DeviceCode) ||
            string.IsNullOrWhiteSpace(Request.DeviceName) ||
            string.IsNullOrWhiteSpace(Request.IpAddress))
        {
            MessageBox.Show(this, "车间、设备编码、设备名称和 IP 地址不能为空。", "校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
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
