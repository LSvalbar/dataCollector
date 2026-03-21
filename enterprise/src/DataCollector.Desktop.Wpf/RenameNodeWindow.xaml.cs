using System.Windows;

namespace DataCollector.Desktop.Wpf;

public partial class RenameNodeWindow : Window
{
    public RenameNodeWindow(string currentName, string titleText)
    {
        InitializeComponent();
        WindowLayoutHelper.EnableResponsiveSizing(this, 0.56, 0.46);
        Title = titleText;
        PromptTextBlock.Text = titleText;
        NameTextBox.Text = currentName;
        NameTextBox.SelectAll();
        Loaded += (_, _) => NameTextBox.Focus();
    }

    public string NodeName { get; private set; } = string.Empty;

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "名称不能为空。", "校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        NodeName = name;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
