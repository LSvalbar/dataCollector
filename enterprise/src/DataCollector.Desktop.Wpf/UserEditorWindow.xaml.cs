using System.Windows;
using DataCollector.Contracts;

namespace DataCollector.Desktop.Wpf;

public partial class UserEditorWindow : Window
{
    private readonly List<SelectableOption> _roleOptions;

    public UserEditorWindow(IEnumerable<RoleDto> availableRoles, UserDto? user = null)
    {
        InitializeComponent();
        _roleOptions = availableRoles
            .OrderBy(role => role.RoleCode, StringComparer.OrdinalIgnoreCase)
            .Select(role => new SelectableOption
            {
                Code = role.RoleCode,
                DisplayText = $"{role.RoleCode} - {role.RoleName}",
                IsSelected = user?.RoleCodes.Contains(role.RoleCode, StringComparer.OrdinalIgnoreCase) ?? false,
            })
            .ToList();

        RoleOptionsItemsControl.ItemsSource = _roleOptions;
        UserCodeTextBox.Text = user?.UserCode ?? string.Empty;
        UserNameTextBox.Text = user?.UserName ?? string.Empty;
        DisplayNameTextBox.Text = user?.DisplayName ?? string.Empty;
        DepartmentTextBox.Text = user?.Department ?? string.Empty;
        EnabledCheckBox.IsChecked = user?.IsEnabled ?? true;

        if (user is not null)
        {
            UserCodeTextBox.IsReadOnly = true;
        }
    }

    public UserUpsertRequest? Request { get; private set; }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedRoleCodes = _roleOptions.Where(option => option.IsSelected).Select(option => option.Code).ToArray();
        if (selectedRoleCodes.Length == 0)
        {
            MessageBox.Show(this, "请至少勾选一个角色。", "校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Request = new UserUpsertRequest
        {
            UserCode = UserCodeTextBox.Text.Trim(),
            UserName = UserNameTextBox.Text.Trim(),
            DisplayName = DisplayNameTextBox.Text.Trim(),
            Department = DepartmentTextBox.Text.Trim(),
            RoleCodes = selectedRoleCodes,
            IsEnabled = EnabledCheckBox.IsChecked ?? true,
        };

        if (string.IsNullOrWhiteSpace(Request.UserCode) ||
            string.IsNullOrWhiteSpace(Request.UserName) ||
            string.IsNullOrWhiteSpace(Request.DisplayName) ||
            string.IsNullOrWhiteSpace(Request.Department))
        {
            MessageBox.Show(this, "用户编码、用户名、显示名和所属部门不能为空。", "校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
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
