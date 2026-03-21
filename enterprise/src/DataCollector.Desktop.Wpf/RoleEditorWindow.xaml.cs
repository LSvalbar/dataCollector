using System.Windows;
using DataCollector.Contracts;

namespace DataCollector.Desktop.Wpf;

public partial class RoleEditorWindow : Window
{
    private readonly List<SelectableOption> _permissionOptions;

    public RoleEditorWindow(IEnumerable<PermissionDto> availablePermissions, RoleDto? role = null)
    {
        InitializeComponent();
        _permissionOptions = availablePermissions
            .OrderBy(permission => permission.PermissionCode, StringComparer.OrdinalIgnoreCase)
            .Select(permission => new SelectableOption
            {
                Code = permission.PermissionCode,
                DisplayText = $"{permission.PermissionCode} - {permission.PermissionName}",
                IsSelected = role?.Permissions.Any(item => item.PermissionCode.Equals(permission.PermissionCode, StringComparison.OrdinalIgnoreCase)) ?? false,
            })
            .ToList();

        PermissionOptionsItemsControl.ItemsSource = _permissionOptions;
        RoleCodeTextBox.Text = role?.RoleCode ?? string.Empty;
        RoleNameTextBox.Text = role?.RoleName ?? string.Empty;
        DescriptionTextBox.Text = role?.Description ?? string.Empty;

        if (role is not null)
        {
            RoleCodeTextBox.IsReadOnly = true;
        }
    }

    public RoleUpsertRequest? Request { get; private set; }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedPermissionCodes = _permissionOptions.Where(option => option.IsSelected).Select(option => option.Code).ToArray();
        if (selectedPermissionCodes.Length == 0)
        {
            MessageBox.Show(this, "请至少勾选一个权限。", "校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Request = new RoleUpsertRequest
        {
            RoleCode = RoleCodeTextBox.Text.Trim(),
            RoleName = RoleNameTextBox.Text.Trim(),
            Description = DescriptionTextBox.Text.Trim(),
            PermissionCodes = selectedPermissionCodes,
        };

        if (string.IsNullOrWhiteSpace(Request.RoleCode) ||
            string.IsNullOrWhiteSpace(Request.RoleName))
        {
            MessageBox.Show(this, "角色编码和角色名称不能为空。", "校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
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

    private sealed class SelectableOption
    {
        public required string Code { get; init; }

        public required string DisplayText { get; init; }

        public bool IsSelected { get; set; }
    }
}
