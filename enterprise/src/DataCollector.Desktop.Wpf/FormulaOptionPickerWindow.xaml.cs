using System.Windows;
using DataCollector.Contracts;

namespace DataCollector.Desktop.Wpf;

public partial class FormulaOptionPickerWindow : Window
{
    public FormulaOptionPickerWindow(IReadOnlyList<FormulaVariableOptionDto> options)
    {
        InitializeComponent();
        WindowLayoutHelper.EnableResponsiveSizing(this, 0.44, 0.36);
        OptionComboBox.ItemsSource = options;
        OptionComboBox.SelectedIndex = options.Count > 0 ? 0 : -1;
    }

    public FormulaVariableOptionDto? SelectedOption { get; private set; }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (OptionComboBox.SelectedItem is not FormulaVariableOptionDto option)
        {
            MessageBox.Show(this, "请先选择要加入的时间项。", "未选择选项", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SelectedOption = option;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
