using System.Windows;
using DataCollector.Contracts;

namespace DataCollector.Desktop.Wpf;

public partial class FormulaConfigWindow : Window
{
    public FormulaConfigWindow(
        IReadOnlyList<FormulaVariableOptionDto> options,
        FormulaSelection powerOnSelection,
        FormulaSelection utilizationSelection)
    {
        InitializeComponent();
        WindowLayoutHelper.EnableResponsiveSizing(this, 0.74, 0.82);
        PowerOnNumeratorComboBox.ItemsSource = options;
        PowerOnDenominatorComboBox.ItemsSource = options;
        UtilizationNumeratorComboBox.ItemsSource = options;
        UtilizationDenominatorComboBox.ItemsSource = options;

        PowerOnNumeratorComboBox.SelectedValue = powerOnSelection.Numerator;
        PowerOnDenominatorComboBox.SelectedValue = powerOnSelection.Denominator;
        UtilizationNumeratorComboBox.SelectedValue = utilizationSelection.Numerator;
        UtilizationDenominatorComboBox.SelectedValue = utilizationSelection.Denominator;

        PowerOnNumeratorComboBox.SelectionChanged += (_, _) => UpdatePreview();
        PowerOnDenominatorComboBox.SelectionChanged += (_, _) => UpdatePreview();
        UtilizationNumeratorComboBox.SelectionChanged += (_, _) => UpdatePreview();
        UtilizationDenominatorComboBox.SelectionChanged += (_, _) => UpdatePreview();
        UpdatePreview();
    }

    public FormulaSelection? PowerOnSelection { get; private set; }

    public FormulaSelection? UtilizationSelection { get; private set; }

    private void UpdatePreview()
    {
        PowerOnPreviewTextBlock.Text = $"当前公式：{BuildPreview(PowerOnNumeratorComboBox.SelectedValue as string, PowerOnDenominatorComboBox.SelectedValue as string)}";
        UtilizationPreviewTextBlock.Text = $"当前公式：{BuildPreview(UtilizationNumeratorComboBox.SelectedValue as string, UtilizationDenominatorComboBox.SelectedValue as string)}";
    }

    private static string BuildPreview(string? numerator, string? denominator)
    {
        if (string.IsNullOrWhiteSpace(numerator) || string.IsNullOrWhiteSpace(denominator))
        {
            return "-";
        }

        return $"({numerator} / {denominator}) * 100";
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (PowerOnNumeratorComboBox.SelectedValue is not string powerOnNumerator ||
            PowerOnDenominatorComboBox.SelectedValue is not string powerOnDenominator ||
            UtilizationNumeratorComboBox.SelectedValue is not string utilizationNumerator ||
            UtilizationDenominatorComboBox.SelectedValue is not string utilizationDenominator)
        {
            MessageBox.Show(this, "请先从下拉列表中完整选择分子和分母。", "校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        PowerOnSelection = new FormulaSelection(powerOnNumerator, powerOnDenominator);
        UtilizationSelection = new FormulaSelection(utilizationNumerator, utilizationDenominator);
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

public sealed record FormulaSelection(string Numerator, string Denominator);
